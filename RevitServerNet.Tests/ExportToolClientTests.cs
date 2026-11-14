using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RevitServerNet.Tools;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Fact that runs only where a C# compiler of the .NET Framework is present (it builds the fake RevitServerTool.exe).
    /// </summary>
    public sealed class FakeToolFactAttribute : FactAttribute
    {
        public FakeToolFactAttribute()
        {
            if (FakeToolFixture.FindCompiler() == null)
                Skip = "The .NET Framework C# compiler (csc.exe) was not found: the fake RevitServerTool.exe cannot be built, so the RevitServerToolClient process tests "
                    + "(argument quoting, destination, output reading, timeout kill, ERROR detection) are NOT executed on this machine.";
        }
    }

    /// <summary>
    /// A tiny console exe built once per test class that stands in for RevitServerTool.exe: it prints the arguments it received (as the
    /// C runtime parsed them, non-ASCII characters as \uXXXX) and acts as RSN_FAKE_MODE says.
    /// </summary>
    public sealed class FakeToolFixture : IDisposable
    {
        private const string Source = @"
using System;
using System.IO;
using System.Text;
using System.Threading;

static class FakeTool
{
    static string Escape(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c >= 32 && c < 127) sb.Append(c); else sb.Append(""\\u"" + ((int)c).ToString(""x4""));
        }
        return sb.ToString();
    }

    static int Main(string[] args)
    {
        var mode = Environment.GetEnvironmentVariable(""RSN_FAKE_MODE"") ?? """";
        if (mode == ""sleep"")
        {
            File.WriteAllText(Environment.GetEnvironmentVariable(""RSN_FAKE_PIDFILE""), System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            Thread.Sleep(120000);
            return 0;
        }
        if (mode == ""sleepchild"")
        {
            Thread.Sleep(30000);
            return 0;
        }
        if (mode == ""spawn"")
        {
            // A child that inherits the redirected output pipes and outlives the tool.
            var psi = new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location) { UseShellExecute = false };
            psi.EnvironmentVariables[""RSN_FAKE_MODE""] = ""sleepchild"";
            var child = System.Diagnostics.Process.Start(psi);
            File.WriteAllText(Environment.GetEnvironmentVariable(""RSN_FAKE_PIDFILE""), child.Id.ToString());
            Thread.Sleep(120000);
            return 0;
        }
        if (mode == ""big"")
        {
            for (var i = 0; i < 20000; i++) { Console.Out.WriteLine(""out line "" + i); if (i % 10 == 0) Console.Error.WriteLine(""err line "" + i); }
            return 0;
        }
        for (var i = 0; i < args.Length; i++) Console.Out.WriteLine(""["" + i + ""]"" + Escape(args[i]));
        if (mode == ""fail"")
        {
            Console.Error.WriteLine(""boom: the model is locked"");
            return 3;
        }
        if (mode == ""error0"")
        {
            // what RevitServerTool 2022 prints for an unknown model; it exits with 0 and writes no file
            Console.Out.WriteLine(""ERROR: Cannot create the local model because the model \""Test\\x.rvt\"" does not exist on the Revit server \""localhost\""."");
            return 0;
        }
        return 0;
    }
}";

        private readonly TempDirectory _temp = new TempDirectory();

        public FakeToolFixture()
        {
            var compiler = FindCompiler();
            if (compiler == null) return; // the facts are skipped
            var source = _temp.Path("FakeTool.cs");
            File.WriteAllText(source, Source);
            ToolPath = _temp.Path("RevitServerTool.exe");
            var psi = new ProcessStartInfo(compiler, $"/nologo /target:exe /out:\"{ToolPath}\" \"{source}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(ToolPath))
                    throw new InvalidOperationException("Building the fake RevitServerTool.exe failed: " + output);
            }
        }

        public string ToolPath { get; }

        public static string FindCompiler()
        {
            var windows = Environment.GetEnvironmentVariable("windir");
            if (string.IsNullOrEmpty(windows)) return null;
            return new[] { "Framework64", "Framework" }
                .Select(f => Path.Combine(windows, "Microsoft.NET", f, "v4.0.30319", "csc.exe"))
                .FirstOrDefault(File.Exists);
        }

        public void Dispose() => _temp.Dispose();
    }

    /// <summary>
    /// <see cref="RevitServerToolClient"/> against a fake RevitServerTool.exe: argument quoting, destination handling, output and
    /// exit code capture, timeout. The tests set environment variables that the fake reads, so they must not run in parallel with each
    /// other (one class: xUnit runs its tests one after another).
    /// </summary>
    public class ExportToolClientTests : IClassFixture<FakeToolFixture>
    {
        private readonly FakeToolFixture _tool;

        public ExportToolClientTests(FakeToolFixture tool)
        {
            _tool = tool;
        }

        private static string Escape(string value)
        {
            var sb = new StringBuilder();
            foreach (var c in value)
                sb.Append(c >= 32 && c < 127 ? c.ToString() : "\\u" + ((int)c).ToString("x4"));
            return sb.ToString();
        }

        /// <summary>The arguments the fake tool received, in the form it prints them.</summary>
        private static List<string> ReceivedArguments(RevitServerToolResult result)
        {
            return result.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("[")).Select(line => line.Substring(line.IndexOf(']') + 1)).ToList();
        }

        private static async Task<T> WithMode<T>(string mode, string pidFile, Func<Task<T>> run)
        {
            Environment.SetEnvironmentVariable("RSN_FAKE_MODE", mode);
            Environment.SetEnvironmentVariable("RSN_FAKE_PIDFILE", pidFile);
            try
            {
                return await run();
            }
            finally
            {
                Environment.SetEnvironmentVariable("RSN_FAKE_MODE", null);
                Environment.SetEnvironmentVariable("RSN_FAKE_PIDFILE", null);
            }
        }

        // ---- arguments ----

        [Theory]
        [InlineData("", "\"\"")]
        [InlineData("abc", "\"abc\"")]
        [InlineData("a b", "\"a b\"")]
        [InlineData("a\"b", "\"a\\\"b\"")]
        [InlineData("a\\", "\"a\\\\\"")]
        [InlineData("a\\\\", "\"a\\\\\\\\\"")]
        [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
        [InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
        [InlineData("C:\\dir\\file.rvt", "\"C:\\dir\\file.rvt\"")]
        public void QuoteArgument_FollowsTheWindowsCommandLineRules(string argument, string expected)
        {
            Assert.Equal(expected, RevitServerToolClient.QuoteArgument(argument));
        }

        [Fact]
        public void BuildCreateLocalArguments_QuotesEveryValue_AndAddsTheOverwriteOptionLast()
        {
            Assert.Equal("createLocalRvt \"A\\b.rvt\" -s \"host\" -d \"C:\\x y\\f.rvt\"",
                RevitServerToolClient.BuildCreateLocalArguments("host", "A\\b.rvt", "C:\\x y\\f.rvt", false));
            Assert.Equal("createLocalRvt \"A\\b.rvt\" -s \"host\" -d \"C:\\x y\\f.rvt\" -o",
                RevitServerToolClient.BuildCreateLocalArguments("host", "A\\b.rvt", "C:\\x y\\f.rvt", true));
        }

        [FakeToolFact]
        public async Task CreateLocalModel_EveryValueReachesTheToolAsOneArgument()
        {
            using (var temp = new TempDirectory())
            {
                // A host with a space would have added "-o" as an option; a quote and a trailing backslash used to merge the rest of the line.
                var host = "h -o";
                var model = "Dir with space\\Model \"q\" Модель.rvt";
                var destination = Path.Combine(temp.Create("out dir"), "f ile.rvt");

                var result = await WithMode("", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, host, model, destination));

                Assert.True(result.Success);
                Assert.Equal(new[] { "createLocalRvt", Escape(model), "-s", Escape(host), "-d", Escape(destination) }, ReceivedArguments(result));
            }
        }

        [FakeToolFact]
        public async Task CreateLocalModel_ModelPathEndingInABackslash_DoesNotEscapeTheClosingQuote()
        {
            using (var temp = new TempDirectory())
            {
                var destination = temp.Path("x.rvt");

                var result = await WithMode("", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "Folder one\\", destination, overwrite: true));

                Assert.Equal(new[] { "createLocalRvt", "Folder one\\", "-s", "host", "-d", Escape(destination), "-o" }, ReceivedArguments(result));
            }
        }

        // ---- destination ----

        [FakeToolFact]
        public async Task CreateLocalModel_DestinationWithoutADirectoryPart_IsRelativeToTheCurrentDirectory()
        {
            var name = "RsnExportTool_" + Guid.NewGuid().ToString("N") + ".rvt";

            var result = await WithMode("", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", name));

            Assert.True(result.Success);
            Assert.Equal(new[] { "createLocalRvt", "A\\b.rvt", "-s", "host", "-d", Escape(Path.GetFullPath(name)) }, ReceivedArguments(result));
        }

        [FakeToolFact]
        public async Task CreateLocalModel_CreatesTheDestinationFolder()
        {
            using (var temp = new TempDirectory())
            {
                var destination = temp.Path(@"new\deeper\x.rvt");

                await WithMode("", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", destination));

                Assert.True(Directory.Exists(Path.GetDirectoryName(destination)));
            }
        }

        [FakeToolFact]
        public async Task CreateLocalModel_DestinationEndingWithASeparator_IsRejectedBeforeTheToolRuns()
        {
            using (var temp = new TempDirectory())
            {
                var folder = temp.Path("folder") + Path.DirectorySeparatorChar;

                var ex = await Assert.ThrowsAsync<ArgumentException>(() => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", folder));

                Assert.Equal("destinationFile", ex.ParamName);
                Assert.False(Directory.Exists(temp.Path("folder")));
            }
        }

        // ---- result ----

        [FakeToolFact]
        public async Task CreateLocalModel_ErrorLineWithExitCode0_IsNotSuccess()
        {
            using (var temp = new TempDirectory())
            {
                var result = await WithMode("error0", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "localhost", "Test\\x.rvt", temp.Path("x.rvt")));

                Assert.Equal(0, result.ExitCode);
                Assert.False(result.Success);
                Assert.StartsWith("ERROR: Cannot create the local model", result.ErrorMessage);
            }
        }

        [FakeToolFact]
        public async Task CreateLocalModel_ReturnsTheExitCodeAndBothOutputs()
        {
            using (var temp = new TempDirectory())
            {
                var result = await WithMode("fail", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", temp.Path("x.rvt")));

                Assert.False(result.Success);
                Assert.Equal(3, result.ExitCode);
                Assert.Contains("boom: the model is locked", result.StandardError);
                Assert.Contains("[0]createLocalRvt", result.StandardOutput);
            }
        }

        [FakeToolFact]
        public async Task CreateLocalModel_ReturnsAllOfALongOutput()
        {
            using (var temp = new TempDirectory())
            {
                var result = await WithMode("big", null, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", temp.Path("x.rvt")));

                Assert.True(result.Success);
                var lines = result.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(20000, lines.Length);
                Assert.Equal("out line 19999", lines.Last());
                Assert.Equal(2000, result.StandardError.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length);
            }
        }

        [FakeToolFact]
        public async Task CreateLocalModel_Timeout_KillsTheToolAndThrows()
        {
            using (var temp = new TempDirectory())
            {
                var pidFile = temp.Path("pid.txt");
                var stopwatch = Stopwatch.StartNew();

                var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                    WithMode("sleep", pidFile, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\\b.rvt", temp.Path("x.rvt"), timeoutMs: 3000)));

                Assert.Contains("killed", ex.Message);
                Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
                var pid = int.Parse(File.ReadAllText(pidFile));
                // Killed and gone by the time the exception is thrown.
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
            }
        }

        // The tool's child process keeps the output pipes open after the tool is killed: the wait for the end of the output used to
        // have no bound, so the timeout never surfaced until the child ended (30 s here).
        [FakeToolFact]
        public async Task CreateLocalModel_Timeout_ThrowsEvenWhenAChildProcessKeepsTheOutputOpen()
        {
            using (var temp = new TempDirectory())
            {
                var pidFile = temp.Path("pid.txt");
                try
                {
                    var stopwatch = Stopwatch.StartNew();

                    await Assert.ThrowsAsync<TimeoutException>(() =>
                        WithMode("spawn", pidFile, () => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "host", "A\b.rvt", temp.Path("x.rvt"), timeoutMs: 2000)));

                    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"the timeout surfaced after {stopwatch.Elapsed}");
                }
                finally
                {
                    if (File.Exists(pidFile))
                    {
                        try { using (var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)))) child.Kill(); }
                        catch (ArgumentException) { /* already gone */ }
                    }
                }
            }
        }

        // ---- validation ----

        [Fact]
        public async Task CreateLocalModel_MissingTool_Throws()
        {
            await Assert.ThrowsAsync<RevitServerToolException>(() => RevitServerToolClient.CreateLocalModelAsync(@"C:\no\such\RevitServerTool.exe", "host", "A\\b.rvt", "x.rvt"));
            await Assert.ThrowsAsync<RevitServerToolException>(() => RevitServerToolClient.CreateLocalModelAsync(null, "host", "A\\b.rvt", "x.rvt"));
        }

        [FakeToolFact]
        public async Task CreateLocalModel_EmptyArguments_Throw()
        {
            Assert.Equal("serverHost", (await Assert.ThrowsAsync<ArgumentException>(() => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, " ", "m", "x.rvt"))).ParamName);
            Assert.Equal("modelRelativePath", (await Assert.ThrowsAsync<ArgumentException>(() => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "h", "", "x.rvt"))).ParamName);
            Assert.Equal("destinationFile", (await Assert.ThrowsAsync<ArgumentException>(() => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "h", "m", null))).ParamName);
            Assert.Equal("timeoutMs", (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RevitServerToolClient.CreateLocalModelAsync(_tool.ToolPath, "h", "m", "x.rvt", timeoutMs: -5))).ParamName);
        }

        // ---- locating the tool ----

        [Fact]
        public void TryLocateToolPath_EnvironmentVariableWithAnExistingFile_Wins()
        {
            using (var temp = new TempDirectory())
            {
                var file = temp.Path("RevitServerTool.exe");
                File.WriteAllText(file, "x");
                var previous = Environment.GetEnvironmentVariable("REVITSERVER_TOOL_PATH");
                Environment.SetEnvironmentVariable("REVITSERVER_TOOL_PATH", file);
                try
                {
                    Assert.Equal(file, RevitServerToolClient.TryLocateToolPath());
                    Assert.Equal(file, RevitServerToolClient.TryLocateToolPath("2022"));
                }
                finally
                {
                    Environment.SetEnvironmentVariable("REVITSERVER_TOOL_PATH", previous);
                }
            }
        }

        [Fact]
        public void TryLocateToolPath_EnvironmentVariableWithAMissingFile_IsIgnored()
        {
            using (var temp = new TempDirectory())
            {
                var missing = temp.Path("RevitServerTool.exe");
                var previous = Environment.GetEnvironmentVariable("REVITSERVER_TOOL_PATH");
                Environment.SetEnvironmentVariable("REVITSERVER_TOOL_PATH", missing);
                try
                {
                    Assert.NotEqual(missing, RevitServerToolClient.TryLocateToolPath());
                }
                finally
                {
                    Environment.SetEnvironmentVariable("REVITSERVER_TOOL_PATH", previous);
                }
            }
        }

        [Theory]
        [InlineData("2022")]
        [InlineData("1999")]
        [InlineData("bad|name")]
        [InlineData(@"..\..\x")]
        public void TryLocateToolPath_VersionHint_NeverThrows_AndReturnsAnExistingFileOrNull(string hint)
        {
            var path = RevitServerToolClient.TryLocateToolPath(hint);

            if (path != null) Assert.True(File.Exists(path));
        }

        [Theory]
        [InlineData("|A|b.rvt", "A\\b.rvt")]
        [InlineData("A|b|c.rvt", "A\\b\\c.rvt")]
        [InlineData("  |A|b.rvt ", "A\\b.rvt")]
        public void ConvertPipePathToRelativeWindowsPath_DropsTheLeadingPipe(string pipePath, string expected)
        {
            Assert.Equal(expected, RevitServerToolClient.ConvertPipePathToRelativeWindowsPath(pipePath));
        }

        [Theory]
        [InlineData("http://h:2022/RevitServerAdminRESTService2022/AdminRESTService.svc", "2022")]
        [InlineData("http://h/revitserveradminrestservice2025/x", "2025")]
        [InlineData("http://h/RevitServerAdminRESTService/x", null)]
        [InlineData("", null)]
        public void ParseVersionFromBaseUrl_TakesTheYearAfterTheServiceName(string baseUrl, string expected)
        {
            Assert.Equal(expected, RevitServerToolClient.ParseVersionFromBaseUrl(baseUrl));
        }
    }
}
