﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.DotNet.Cli.Build.Framework;

using static Microsoft.DotNet.Cli.Build.FS;
using static Microsoft.DotNet.Cli.Build.Framework.BuildHelpers;
using static Microsoft.DotNet.Cli.Build.Utils;

namespace Microsoft.DotNet.Cli.Build
{
    public class TestTargets
    {
        private static string s_testPackageBuildVersionSuffix = "<buildversion>";

        public static readonly string[] TestProjects = new[]
        {
            "HostActivationTests"
        };

        [Target(
            nameof(PrepareTargets.Init),
            nameof(RestoreTestAssets),
            nameof(RestoreTests),
            nameof(BuildTests),
            nameof(RunTests))]
        public static BuildTargetResult Test(BuildTargetContext c) => c.Success();

        [Target]
        public static BuildTargetResult RestoreTestAssets(BuildTargetContext c)
        {
            CleanBinObj(c, Path.Combine(Dirs.RepoRoot, "TestAssets"));

            DotNetCli.Stage0.Restore("--verbosity", "verbose")
                .WorkingDirectory(Path.Combine(Dirs.RepoRoot, "TestAssets"))
                .Execute()
                .EnsureSuccessful();

            return c.Success();
        }

        [Target]
        public static BuildTargetResult RestoreTests(BuildTargetContext c)
        {
            CleanBinObj(c, Path.Combine(Dirs.RepoRoot, "test"));
            
            DotNetCli.Stage0.Restore("--verbosity", "verbose")
                .WorkingDirectory(Path.Combine(Dirs.RepoRoot, "test"))
                .Execute()
                .EnsureSuccessful();
            return c.Success();
        }

        [Target]
        public static BuildTargetResult BuildTests(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage0;

            var configuration = c.BuildContext.Get<string>("Configuration");

            foreach (var testProject in GetTestProjects())
            {
                c.Info($"Building tests: {testProject}");
                dotnet.Build("--configuration", configuration)
                    .WorkingDirectory(Path.Combine(Dirs.RepoRoot, "test", testProject))
                    .Execute()
                    .EnsureSuccessful();
            }
            return c.Success();
        }

        [Target]
        public static BuildTargetResult RunTests(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage0;
            var vsvars = LoadVsVars(c);

            var configuration = c.BuildContext.Get<string>("Configuration");

            // Copy the test projects
            var testProjectsDir = Path.Combine(Dirs.TestOutput, "TestProjects");
            Rmdir(testProjectsDir);
            Mkdirp(testProjectsDir);
            CopyRecursive(Path.Combine(Dirs.RepoRoot, "TestAssets", "TestProjects"), testProjectsDir);

            // Run the tests
            var failingTests = new List<string>();
            foreach (var project in GetTestProjects())
            {
                c.Info($"Running tests in: {project}");
                var result = dotnet.Test("--configuration", configuration, "-xml", $"{project}-testResults.xml", "-notrait", "category=failing")
                    .WorkingDirectory(Path.Combine(Dirs.RepoRoot, "test", project))
                    .Environment(vsvars)
                    .EnvironmentVariable("PATH", $"{DotNetCli.Stage0.BinPath}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}")
                    .EnvironmentVariable("TEST_ARTIFACTS", Dirs.TestArtifacts)
                    .Execute();
                if (result.ExitCode != 0)
                {
                    failingTests.Add(project);
                }
            }

            if (failingTests.Any())
            {
                foreach (var project in failingTests)
                {
                    c.Error($"{project} failed");
                }
                return c.Failed("Tests failed!");
            }

            return c.Success();
        }

        private static IEnumerable<string> GetTestProjects()
        {
            List<string> testProjects = new List<string>();
            testProjects.AddRange(TestProjects);

            if (CurrentPlatform.IsWindows)
            {
                testProjects.AddRange(WindowsTestProjects);
            }

            return testProjects;
        }
    }
}