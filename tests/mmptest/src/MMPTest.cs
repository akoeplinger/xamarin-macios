using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Reflection;

using Xamarin.Tests;

namespace Xamarin.MMP.Tests
{
	[TestFixture]
	public partial class MMPTests 
	{
		public static void RunMMPTest (Action <string> test, string directoryName = null)
		{
			test (Cache.CreateTemporaryDirectory (directoryName));
		}

		// TODO - We have multiple tests using this. It doesn't take that long, but is it worth caching?
		public static string [] GetUnifiedProjectClangInvocation (string tmpDir, string projectConfig = "")
		{
			TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { CSProjConfig = projectConfig };
			string buildOutput = TI.TestUnifiedExecutable (test).BuildOutput;
			string [] splitBuildOutput = TI.TestUnifiedExecutable (test).BuildOutput.Split (new string[] { Environment.NewLine }, StringSplitOptions.None);
			string clangInvocation = splitBuildOutput.Single (x => x.Contains ("clang"));
			return clangInvocation.Split (new string[] { " " }, StringSplitOptions.None);
		}

		[Test]
		public void CollisionsBetweenLibraryNameAndEXE_ShouldFailBuild ()
		{
			RunMMPTest (tmpDir => {
				Action <string, string, bool> testCore = (projectName, assemblyName, XM45) => {
					// Build a library with the conflicting name
					TI.UnifiedTestConfig libConfig = new TI.UnifiedTestConfig (tmpDir) { XM45 = XM45, ProjectName = projectName, AssemblyName = assemblyName };
					string csprojTarget = TI.GenerateUnifiedLibraryProject (libConfig);
					TI.BuildProject (csprojTarget, isUnified : true);

					// Build an exe using that library, it should fail the build
					string referenceCode = string.Format (@"<Reference Include=""TestApp""><HintPath>{0}</HintPath></Reference>", Path.Combine (tmpDir, "bin/Debug", assemblyName + ".dll"));

					TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { XM45 = XM45, References = referenceCode };
					TI.TestUnifiedExecutable (test, shouldFail : true);
				};
				// These library assembly names conflict with the exe name
				testCore ("UnifiedLibrary", "UnifiedExample", false);
				testCore ("XM45Library", "XM45Example", true);
			});
		}

		[Test]
		public void CollisionBetweenEXEAndSDKAssembly_ShouldFailBuild ()
		{
			RunMMPTest (tmpDir => {
				Action <string, bool> testCore = (assemblyName, XM45) => {
					// Build a library with the conflicting name
					TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { XM45 = XM45, AssemblyName = assemblyName };
					TI.TestUnifiedExecutable (test, shouldFail : true);
				};
				// These library assembly names conflict with the exe name
				testCore ("Xamarin.Mac", false);
				testCore ("Xamarin.Mac", true);

				testCore ("System.Xml", false);
				testCore ("System.Xml", true);
			});
		}

		[Test]
		public void XM_45_NotAddingIncorrectDependencies_LicenseTest ()
		{
			RunMMPTest (tmpDir => {
				TI.TestUnifiedExecutable (new TI.UnifiedTestConfig (tmpDir) { XM45 = true });

				// XM 4.5 projects were accidently pulling in every assembly in the 4.5 folder. Assert that isn't happening again.
				string monoBundlePath = Path.Combine (tmpDir, "bin/Debug/XM45Example.app/Contents/MonoBundle/");
				Assert.IsFalse (Directory.GetFiles (monoBundlePath).Any (x => x.Contains ("FSharp.Core.dll")), "F# was pulled in?");
			});
		}

		[Test]
		public void Mobile_NewRefCount_Warns ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { CSProjConfig = "<MonoBundlingExtraArgs>--new-refcount=false</MonoBundlingExtraArgs>" };
				string buildOutput = TI.TestUnifiedExecutable (test).BuildOutput;
				Assert.IsTrue (buildOutput.Contains ("Disabling the new refcount logic is deprecated"), "Mobile_NewRefCount_Warns did not warn as expected:\n\n", buildOutput);
			});
		}

		[Test]
		public void Mobile_Verbose_Test ()
		{
			RunMMPTest (tmpDir => {
				// Just make sure we accept the option and it doesn't crash
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { CSProjConfig = "<MonoBundlingExtraArgs>-v -v</MonoBundlingExtraArgs>" };
				TI.TestUnifiedExecutable (test);
			});
		}

		// The check for name collisions between the root executable and other assemblies was mistakenly comparing against the entire path, not just the assembly name.
		// This meant that an assembly located in a folder/subfolder with a name matching the root executable name would incorrectly report as an error.
		[Test]
		public void FilePathCollisionShouldNotFailBuild ()
		{
			RunMMPTest (tmpDir => {
				var libraryDirectory = tmpDir + "/UnifiedExample/";
				var libraryName = "UnifiedLibrary";
				Directory.CreateDirectory (libraryDirectory);

				TI.UnifiedTestConfig libConfig = new TI.UnifiedTestConfig (libraryDirectory) { ProjectName = libraryName };
				string csprojTarget = TI.GenerateUnifiedLibraryProject (libConfig);

				TI.BuildProject (csprojTarget, isUnified : true);

				string referenceCode = string.Format (@"<Reference Include=""UnifiedLibrary""><HintPath>{0}</HintPath></Reference>", Path.Combine (libraryDirectory, "bin/Debug/", $"{libraryName}.dll"));

				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { References = referenceCode };
				TI.TestUnifiedExecutable  (test);
			});
		}

		[TestCase (false)]
		[TestCase (true)]
		public void Unified_HelloWorld_ShouldHaveNoWarnings (bool release)
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir);
				test.Release = release;

				// Due to https://bugzilla.xamarin.com/show_bug.cgi?id=48311 we can get warnings related to the registrar
				Func<string, bool> hasLegitWarning = results =>
					results.Split (Environment.NewLine.ToCharArray ()).Any (x => x.Contains ("warning") && !x.Contains ("deviceBrowserView:selectionDidChange:"));

				// Mobile
				string buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
				Assert.IsTrue (!hasLegitWarning (buildResults), "Unified_HelloWorld_ShouldHaveNoWarnings - Mobile had warning: \n" + buildResults);

				// XM45
				test.XM45 = true;
				buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
				Assert.IsTrue (!hasLegitWarning (buildResults), "Unified_HelloWorld_ShouldHaveNoWarnings - XM45 had warning: \n" + buildResults);
			});
		}

		[Test]
		public void Unified_HelloWorld_ShouldWarnOn32Bit ()
		{
			Configuration.AssertXcodeSupports32Bit ();

			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = "<XamMacArch>i386</XamMacArch>"
				};

				string buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
				Assert.True (buildResults.Contains ("MM0134"), "32-bit Modern did not contain MM0134");
	
				test.XM45 = true;
				buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
				Assert.True (buildResults.Contains ("MM0134"), "32-bit Full did not contain MM0134");
			});
		}

		[Test]
		public void Unified_HelloWorld_ShouldHaveNoRegistrarWarnings ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir);
				// Mobile
				string output = TI.TestUnifiedExecutable (test).RunOutput;
				Assert.IsTrue (!output.Contains ("Could not register the assembly"), "Unified_HelloWorld_ShouldHaveNoRegistrarWarnings - Mobile had registrar issues: \n" + output);

				// XM45
				test.XM45 = true;
				output = TI.TestUnifiedExecutable (test).RunOutput;
				Assert.IsTrue (!output.Contains ("Could not register the assembly"), "Unified_HelloWorld_ShouldHaveNoRegistrarWarnings - XM45 had registrar issues: \n" + output);
			});
		}

		[Test]
		public void BuildUnifiedMobile_Program_WithNonASCIIName ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { AssemblyName = "piñata" };
				TI.TestUnifiedExecutable (test);

				test = new TI.UnifiedTestConfig (tmpDir) { AssemblyName = "你好世界" };
				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void BuildUnifiedMobile_Program_WithSpace ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { AssemblyName = "Test With Space" };
				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void Dontlink_Allow_ReadonlyAssembly ()
		{
			var sb = new StringBuilder ();
			RunMMPTest (tmpDir =>
			{
				// build b.dll
				sb.Clear ();
				string assemblyPath = string.Format ("{0}/b.dll", tmpDir);
				sb.AppendFormat ("-target:library -debug -out:{0} {1}/b.cs", assemblyPath, tmpDir);
				File.WriteAllText (Path.Combine (tmpDir, "b.cs"), "public class B { }");
				TI.RunAndAssert ("/Library/Frameworks/Mono.framework/Commands/mcs", sb, "b");

				File.SetAttributes (assemblyPath, FileAttributes.ReadOnly);
				File.SetAttributes (assemblyPath + ".mdb", FileAttributes.ReadOnly);

				// build project referencing a.dll
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir)
				{
					References = string.Format (" <Reference Include=\"b\" > <HintPath>{0}</HintPath> </Reference> ", assemblyPath),
					TestCode = "System.Console.WriteLine (typeof (B));",
				};
				TI.GenerateAndBuildUnifiedExecutable (test, shouldFail: false);

				test.CSProjConfig = "<LinkMode>SdkOnly</LinkMode>";
				TI.GenerateAndBuildUnifiedExecutable (test, shouldFail: false);
			});
		}

		[Test]
		public void DefaultProject_ShouldPullInMonoPosix_AndNaitveLib ()
		{
			RunMMPTest (tmpDir =>
			{
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { XM45 = true };
				MonoPosixTestCore (tmpDir, test);
				test.XM45 = false;
				MonoPosixTestCore (tmpDir, test);
			});
		}

		static void MonoPosixTestCore (string tmpDir, TI.UnifiedTestConfig test)
		{
			TI.TestUnifiedExecutable (test, shouldFail: false);

			Assert.IsTrue (File.Exists (Path.Combine (tmpDir, "bin/Debug/XM45Example.app/Contents/MonoBundle/Mono.Posix.dll")));
			Assert.IsTrue (File.Exists (Path.Combine (tmpDir, "bin/Debug/XM45Example.app/Contents/MonoBundle/libMonoPosixHelper.dylib")));
		}


		const string machineConfigMobileLocation = "bin/Debug/UnifiedExample.app/Contents/MonoBundle/mono/4.5/machine.config";
		const string machineConfigXM45Location = "bin/Debug/XM45Example.app/Contents/MonoBundle/mono/4.5/machine.config";

		[Test]
		public void Unified_ShouldNotGenerateMachineConfigInBundle_WithoutOption ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir);
				// Mobile
				TI.TestUnifiedExecutable (test);

				Assert.IsFalse (File.Exists (Path.Combine (tmpDir, machineConfigMobileLocation)));

				// XM45
				test.XM45 = true;
				TI.TestUnifiedExecutable (test);

				Assert.IsFalse (File.Exists (Path.Combine (tmpDir, machineConfigXM45Location)));
			});
		}

		[Test]
		public void Unified_InvalidMachineConfigInBundle_ThrowsError ()
		{
			RunMMPTest (tmpDir => {
				string invalidConfigPath = Path.Combine (tmpDir, "nonexistant/machine.config");
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = string.Format ("<MonoBundlingExtraArgs>--machine-config={0}</MonoBundlingExtraArgs>", invalidConfigPath)
				};
				string buildOutput = TI.TestUnifiedExecutable (test, shouldFail : true).BuildOutput;
				Assert.IsTrue (buildOutput.Contains ("can not be found"), "Unified_InvalidMachineConfigInBundle_ThrowsError did not error as expected (1):\n\n", buildOutput);
				Assert.IsTrue (buildOutput.Contains ("97"), "Unified_InvalidMachineConfigInBundle_ThrowsError did not error as expected (2):\n\n", buildOutput);
			});
		}

		[Test]
		public void Unified_SgenConcurrent_Test ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = "<EnableSGenConc>true</EnableSGenConc>"
				};
				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void Unified_ShouldGenerateMachineConfigInBundle_WithEmptyOption ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = "<MonoBundlingExtraArgs>--machine-config=\"\"</MonoBundlingExtraArgs>"
				};
				TI.TestUnifiedExecutable (test);
				Assert.IsTrue (File.Exists (Path.Combine (tmpDir, machineConfigMobileLocation)));

				test.XM45 = true;
				TI.TestUnifiedExecutable (test);
				Assert.IsTrue (File.Exists (Path.Combine (tmpDir, machineConfigXM45Location)));
			});
		}

		[Test]
		public void Unified_ShouldGenerateMachineConfigInBundle_WhenPassedIn ()
		{
			RunMMPTest (tmpDir => {
				const string configText = "THIS_IS_NOT_A_REAL_CONFIG_FILE";
				string configPath = Path.Combine (tmpDir, "machine.config");
				File.WriteAllLines (configPath, new string [] { configText });

				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = string.Format ("<MonoBundlingExtraArgs>--machine-config={0}</MonoBundlingExtraArgs>", configPath)
				};

				// Mobile
				TI.TestUnifiedExecutable (test);

				Assert.IsTrue (File.Exists (Path.Combine (tmpDir, machineConfigMobileLocation)));
				string [] text = File.ReadAllLines (Path.Combine (tmpDir, machineConfigMobileLocation));
				Assert.IsTrue (text.Length == 1 && text[0] == configText);

				// XM45
				test.XM45 = true;
				TI.TestUnifiedExecutable (test);

				Assert.IsTrue (File.Exists (Path.Combine (tmpDir, machineConfigXM45Location)));
				text = File.ReadAllLines (Path.Combine (tmpDir, machineConfigXM45Location));
				Assert.IsTrue (text.Length == 1 && text[0] == configText);
			});
		}

		[Test]
		public void Unified_FailedBuild_ShouldRequireAnotherBuildNotSkipMMP ()
		{
			RunMMPTest (tmpDir => {
				foreach (bool xm45 in new bool [] {false, true})
				{
					// First build with a Non-existant file to force us to error inside mmp test
					TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { CSProjConfig = "<MonoBundlingExtraArgs>--resource=Foo.bar</MonoBundlingExtraArgs>", XM45 = xm45 };
					TI.GenerateAndBuildUnifiedExecutable (test, shouldFail: true);

					// Next, build again without the error MonoBundlingExtraArgs
					test.CSProjConfig = "";
					TI.GenerateUnifiedExecutableProject (test);

					// And try again. 
					// If we fail, we'll likley fail with "did not generate an exe" before returning but let's check anyway
					string secondBuildOutput = TI.BuildProject (Path.Combine (tmpDir, TI.GetUnifiedExecutableProjectName (test)), true);
					Assert.IsTrue (!secondBuildOutput.Contains ("Skipping target \"_CompileToNative"), "Did not skip");
					Assert.IsTrue (secondBuildOutput.Contains ("Building target \"_CompileToNative\" completely"), "Did need to build");
				}
			});
		}	

		public const string BundleResourceTemplate = "<ItemGroup><BundleResource Include=\"{0}\" /></ItemGroup>";

		[Test]
		public void UnifiedWithDepLib_ThatContainsUserResource_ShouldBeRemovedUnderFullLink ()
		{
			RunMMPTest (tmpDir =>
			{
				string resoucePath = Path.Combine (tmpDir, "foo.xml");
				File.Create (resoucePath);

				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir)
				{
					ProjectName = "UnifiedLibrary",
					ItemGroup = string.Format (BundleResourceTemplate, resoucePath),
				};
				var libProject = TI.GenerateUnifiedLibraryProject (test);
				TI.BuildProject (libProject, true);

				string referenceCode = string.Format (@"<Reference Include=""UnifiedLibrary""><HintPath>{0}</HintPath></Reference>", Path.Combine (tmpDir, "bin/Debug", "UnifiedLibrary.dll"));

				test = new TI.UnifiedTestConfig (tmpDir) { References = referenceCode, TestCode = "System.Console.WriteLine (typeof (Library.MyClass));" };
				TI.TestUnifiedExecutable (test);

				string libPath = Path.Combine (tmpDir, "bin/Debug/UnifiedExample.app/Contents/MonoBundle/UnifiedLibrary.dll");
				Assert.True (File.Exists (libPath));
				string monoDisResults = TI.RunAndAssert ("/Library/Frameworks/Mono.framework/Commands/monodis", new StringBuilder ("--presources " + libPath), "monodis");
				Assert.IsFalse (monoDisResults.Contains ("foo.xml"));
			});
		}

		[Test]
		public void Unified_SideBySideXamMac_ConsoleTest ()
		{
			RunMMPTest (tmpDir =>
			{
				string testPath = Path.Combine (TI.FindSourceDirectory (), @"ConsoleXMApp.csproj");
				TI.BuildProject (testPath, isUnified: true);
				string exePath = Path.Combine (TI.FindSourceDirectory (), @"bin/Debug/ConsoleXMApp.exe");
				var output = TI.RunAndAssert ("/Library/Frameworks/Mono.framework/Commands/mono64", new StringBuilder (exePath), "RunSideBySizeXamMac");
				Assert.IsTrue (output.Split (Environment.NewLine.ToCharArray ()).Any (x => x.Contains ("True")), "Unified_SideBySideXamMac_ConsoleTest run"); 
			});
		}

		[Test]
		public void UnifiedDebugBuilds_ShouldLinkToPartialStatic_UnlessDisabled ()
		{
			RunMMPTest (tmpDir =>
			{
				foreach (bool xm45 in new bool[] { false, true })
				{
					TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { XM45 = xm45 };

					test.Release = true;
					string buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
					Assert.IsFalse (buildResults.Contains ("Xamarin.Mac.registrar"), "Release build should not use partial static registrar\n" + buildResults);

					test.Release = false;
					test.CSProjConfig = "<DebugSymbols>true</DebugSymbols>";
					buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
					Assert.IsTrue (buildResults.Contains ("Xamarin.Mac.registrar"), "Debug build should use partial static registrar\n" + buildResults );

					test.CSProjConfig = "<DebugSymbols>true</DebugSymbols><MonoBundlingExtraArgs>--registrar=dynamic</MonoBundlingExtraArgs><XamMacArch>x86_64</XamMacArch>";
					buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
					Assert.IsFalse (buildResults.Contains ("Xamarin.Mac.registrar"), "registrar=dynamic build should not use partial static registrar\n" + buildResults);

					test.CSProjConfig = "<DebugSymbols>true</DebugSymbols><MonoBundlingExtraArgs>--registrar=partial</MonoBundlingExtraArgs><XamMacArch>x86_64</XamMacArch>";
					buildResults = TI.TestUnifiedExecutable (test).BuildOutput;
					Assert.IsTrue (buildResults.Contains ("Xamarin.Mac.registrar"), "registrar=partial build should use partial static registrar\n" + buildResults);
				}
			});
		}

		//https://testrail.xamarin.com/index.php?/cases/view/234141&group_by=cases:section_id&group_order=asc&group_id=51097
		[Test]
		public void Unified45Build_CompileToNativeOutput ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { XM45 = true };
				var output = TI.TestUnifiedExecutable (test);
				Assert.That (output.BuildOutput, Contains.Substring ("Selected target framework: .NETFramework,Version=v4.5; API: Unified"));
			});
		}

		[Test]
		public void UnifiedDebug_ShouldOnlyHaveOne_ObjCArgument ()
		{
			RunMMPTest (tmpDir => {
				string [] clangParts = GetUnifiedProjectClangInvocation (tmpDir);
				int objcCount = clangParts.Count (x => x.Contains ("-ObjC"));
				Assert.AreEqual (1, objcCount, "Found more than one -OjbC");
			});
		}

		[Test]
		[TestCase ("CFNetworkHandler", "CFNetworkHandler")]
		[TestCase ("NSUrlSessionHandler", "NSUrlSessionHandler")]
		[TestCase ("HttpClientHandler", "HttpClientHandler")]
		[TestCase (null, "HttpClientHandler")]
		[TestCase ("", "HttpClientHandler")]
		public void HttpClientHandler (string mmpHandler, string expectedHandler)
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					References = " <Reference Include=\"System.Net.Http\" />",
					TestCode = $@"
			var client = new System.Net.Http.HttpClient ();
			var field = client.GetType ().BaseType.GetField (""handler"", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (field == null)
				throw new System.Exception (""Could not find the field 'handler' in HttpClient's base type (which should be 'HttpMessageInvoker')."");
			var fieldValue = field.GetValue (client);
			if (fieldValue == null)
				throw new System.Exception (""Unexpected null value found in 'HttpMessageInvoker.handler' field."");
			var fieldValueType = fieldValue.GetType ().Name;
			if (fieldValueType != ""{expectedHandler}"")
				throw new System.Exception ($""Unexpected field type, found '{{fieldValueType}}', expected '{expectedHandler}'"");
",
				};
				if (mmpHandler != null)
					test.CSProjConfig = "<HttpClientHandler>" + mmpHandler + "</HttpClientHandler>";
				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		[TestCase (false)]
		[TestCase (true)]
		public void SystemMonoNotEmbedded (bool release)
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir);
				test.Release = release;
				test.CSProjConfig = "<MonoBundlingExtraArgs>--embed-mono=no</MonoBundlingExtraArgs>";
				TI.TestSystemMonoExecutable (test);
			});
		}

		[Test]
		public void Unified_ShouldSupportDynamic ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) { 
					TestCode = @"
						NSObject o = new NSObject ();
						dynamic w = o;
						string x1 = o.Description;
						string x2 = w.Description;",
					References = " <Reference Include=\"Microsoft.CSharp\" />",
				};

				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void UnifiedFull_AllowsLinking_WithForceFlag ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					XM45 = true,
					CSProjConfig = "<LinkMode>Full</LinkMode>"
				};
				TI.TestUnifiedExecutable (test, shouldFail: true);

				test.CSProjConfig = test.CSProjConfig + "<MonoBundlingExtraArgs>--force-unsupported-linker</MonoBundlingExtraArgs>";
				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void Unified32BitWithXMRequiringLibrary_ShouldReferenceCorrectXM_AndNotCrash ()
		{
			Configuration.AssertXcodeSupports32Bit ();

			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig libConfig = new TI.UnifiedTestConfig (tmpDir) {
					ProjectName = "UnifiedLibrary",
					TestCode = "namespace Library { public static class Foo { public static void Bar () { var v = new Foundation.NSObject (); } } }"
				};

				string csprojTarget = TI.GenerateUnifiedLibraryProject (libConfig);
				TI.BuildProject (csprojTarget, isUnified: true);

				string referenceCode = string.Format (@"<Reference Include=""UnifiedLibrary""><HintPath>{0}</HintPath></Reference>", Path.Combine (tmpDir, "bin/Debug/UnifiedLibrary.dll"));

				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = @"<PlatformTarget>x86</PlatformTarget><XamMacArch>i386</XamMacArch>",
					ReferencesBeforePlatform = referenceCode,
					TestCode = "Library.Foo.Bar ();"
				};

				TI.TestUnifiedExecutable (test);
			});
		}

		[Test]
		public void OldXcodeTest ()
		{
			var oldXcode = Xamarin.Tests.Configuration.GetOldXcodeRoot ();

			if (string.IsNullOrEmpty (oldXcode))
				Assert.Ignore ("This test needs an old Xcode.");

			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = "<DebugSymbols>True</DebugSymbols>", // This makes the msbuild tasks pass /debug to mmp
				};
				TI.TestUnifiedExecutable (test, shouldFail: false, environment: new string [] { "MD_APPLE_SDK_ROOT", Path.GetDirectoryName (Path.GetDirectoryName (oldXcode)) });
			});
		}

		[Test]
		public void BuildUnified_BelowMinDeploymentTargetShouldFail ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					PlistReplaceStrings = new Dictionary<string, string> {
						{ "<string>10.7</string>", "<string>10.4</string>"}
					}
				};
				TI.TestUnifiedExecutable (test, shouldFail: true);
			});
		}

		[Test]
		public void BuildUnified_AboveSDKDeploymentTargetShouldFail ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					PlistReplaceStrings = new Dictionary<string, string> {
						{ "<string>10.7</string>", "<string>11.0</string>"}
					}
				};
				TI.TestUnifiedExecutable (test, shouldFail: true);
			});
		}

		[Test]
		public void BuildUnified_ValidSDKDeploymentTargetShouldWork ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					PlistReplaceStrings = new Dictionary<string, string> {
						{ "<string>10.7</string>", "<string>10.12</string>"}
					}
				};
				TI.TestUnifiedExecutable (test, shouldFail: false);
			});
		}

		[Test]
		[TestCase ("all")]
		[TestCase ("-all")]
		[TestCase ("remove-uithread-checks,dead-code-elimination,inline-isdirectbinding,inline-intptr-size")]
		public void Optimizations (string opt)
		{
			RunMMPTest (tmpDir => {
				var test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = $"<MonoBundlingExtraArgs>--optimize={opt}</MonoBundlingExtraArgs>" + 
						"<LinkMode>Full</LinkMode>",
				};
				// register-protocols requires static registrar which is default in Release
				test.Release = true;

				var rv = TI.TestUnifiedExecutable (test, shouldFail: false);
				rv.Messages.AssertNoWarnings ();
				rv.Messages.AssertErrorCount (0);
			});
		}

		[Test]
		[TestCase ("inline-runtime-arch")] // This is valid for Xamarin.iOS
		[TestCase ("foo")]
		public void MM0132 (string opt)
		{
			RunMMPTest (tmpDir => {
				var test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = $"<MonoBundlingExtraArgs>--optimize={opt}</MonoBundlingExtraArgs>" + 
						"<LinkMode>Full</LinkMode>",
				};
				var rv = TI.TestUnifiedExecutable (test, shouldFail: false);
				rv.Messages.AssertWarning (132, $"Unknown optimization: '{opt}'. Valid optimizations are: remove-uithread-checks, dead-code-elimination, inline-isdirectbinding, inline-intptr-size, blockliteral-setupblock, register-protocols, inline-dynamic-registration-supported, static-block-to-delegate-lookup, trim-architectures.");
				rv.Messages.AssertErrorCount (0);
			});
		}

		[Test]
		public void MM0138 ()
		{
			MMPTests.RunMMPTest (tmpDir => {
				var rv = TI.TestClassicExecutable (tmpDir, csprojConfig: "<IncludeMonoRuntime>true</IncludeMonoRuntime>", shouldFail: true);
				rv.Messages.AssertError (138, "Building 32-bit apps is not possible when using Xcode 10. Please migrate project to the Unified API.");
				rv.Messages.AssertWarningCount (0);
			});
		}

		[Test]
		public void MM0139 ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					CSProjConfig = "<XamMacArch>i386</XamMacArch>"
				};

				var rv = TI.TestUnifiedExecutable (test, shouldFail: true);
				rv.Messages.AssertError (139, "Building 32-bit apps is not possible when using Xcode 10. Please change the architecture in the project's Mac Build options to 'x86_64'.");
				rv.Messages.AssertWarningCount (0);
			});
		}

		[Test]
		public void BuildingSameSolutionTwice_ShouldNotRunACToolTwice ()
		{
			RunMMPTest (tmpDir => {
				TI.UnifiedTestConfig test = new TI.UnifiedTestConfig (tmpDir) {
					AssetIcons = true
				};

				string project = TI.GenerateUnifiedExecutableProject (test);

				string buildOutput = TI.BuildProject (project, true);
				Assert.True (buildOutput.Contains ("actool execution started with arguments"), $"Initial build should run actool");

				buildOutput = TI.BuildProject (project, true);
				Assert.False (buildOutput.Contains ("actool execution started with arguments"), $"Second build should not run actool");

				TI.RunAndAssert ("touch", Path.Combine (tmpDir, "Assets.xcassets/AppIcon.appiconset/AppIcon-256@2x.png"), "touch icon");

				buildOutput = TI.BuildProject (project, true);
				Assert.True (buildOutput.Contains ("actool execution started with arguments"), $"Build after touching icon must run actool");
			});
		}
	}
}
