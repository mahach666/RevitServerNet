using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitServerNet.Enterprise;
using RevitServerNet.Tools;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Fact that runs only when <see cref="LiveServer"/> is configured and the real RevitServerTool.exe of the server's year is installed
    /// (Program Files\Autodesk\Revit {year}\RevitServerToolCommand).
    /// </summary>
    public sealed class LiveRevitServerToolFactAttribute : FactAttribute
    {
        public LiveRevitServerToolFactAttribute()
        {
            if (!LiveServer.IsConfigured)
                Skip = LiveServer.SkipReason;
            else if (LiveExportTests.FindTool() == null)
                Skip = $"RevitServerTool.exe of Revit {LiveServer.Year} is not installed on this machine.";
        }
    }

    /// <summary>
    /// Direct export and RevitServerToolClient against a live Revit Server (skipped unless configured, see <see cref="LiveServer"/>):
    /// model path spellings, destination checks before the download, cancellation while connecting, and the central path of a
    /// local copy compared with the one RevitServerTool writes. Runs one test at a time with <see cref="LiveServerExportTests"/>
    /// (same named semaphore): all of them take the read lock of the export's user name.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class LiveExportTests : IDisposable
    {
        private readonly Semaphore _liveTestsLock;

        public LiveExportTests()
        {
            _liveTestsLock = new Semaphore(1, 1, "RevitServerNet.Tests.Live");
            if (!_liveTestsLock.WaitOne(TimeSpan.FromMinutes(30)))
            {
                _liveTestsLock.Dispose();
                throw new TimeoutException("Another test process has been running the live Revit Server tests for 30 min.");
            }
        }

        public void Dispose()
        {
            _liveTestsLock.Release();
            _liveTestsLock.Dispose();
        }

        /// <summary>Path of the real RevitServerTool.exe of the live server's year, or null.</summary>
        internal static string FindTool()
        {
            if (!LiveServer.IsConfigured) return null;
            var roots = new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }
                .Where(r => !string.IsNullOrEmpty(r));
            return roots.Select(r => Path.Combine(r, "Autodesk", "Revit " + LiveServer.Year, "RevitServerToolCommand", "RevitServerTool.exe")).FirstOrDefault(File.Exists);
        }

        private static ModelExporterOptions Options(string modelPath, string destination, bool createLocal = false, bool overwrite = false)
        {
            return new ModelExporterOptions
            {
                ServerHost = LiveServer.Host,
                RevitVersion = LiveServer.Year,
                ModelPipePath = modelPath,
                DestinationFile = destination,
                CreateLocal = createLocal,
                Overwrite = overwrite,
            };
        }

        /// <summary>The model path without a leading separator, as RevitServerTool takes it.</summary>
        private static string RelativeModel => LiveServer.Model.TrimStart('|', '\\', '/').Replace('|', '\\').Replace('/', '\\');

        // ---- model path spellings ----

        // Revit Server 2022 and 2025 answered ModelMissingFault ("Model Test\M.rvt\ was not found") to IdentifyModel for the paths
        // with a trailing, leading or doubled separator, while the REST service (EncodePath) takes them.
        [LiveRevitServerFact]
        public async Task ModelPathWithTrailingLeadingOrDoubledSeparators_IsExported_AndReleasesTheLock()
        {
            var segments = RelativeModel.Split('\\');
            var spellings = new[]
            {
                "|" + string.Join("|", segments) + "|",
                "||" + string.Join("||", segments),
                string.Join("/", segments),
                "\\" + string.Join("\\", segments) + "\\",
                "|" + string.Join("|", segments) + "||",
            };
            using (var temp = new TempDirectory())
            {
                foreach (var spelling in spellings)
                {
                    var destination = temp.Path("m.rvt");

                    await ModelExporter.ExportAsync(Options(spelling, destination, overwrite: true));

                    Assert.True(new FileInfo(destination).Length > 0, spelling);
                    await AssertNoExportLockAsync();
                }
            }
        }

        [LiveRevitServerFact]
        public async Task UnknownModel_IsReportedAsAFaultOfTheServer_WithoutALockLeftBehind()
        {
            using (var temp = new TempDirectory())
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    ModelExporter.ExportAsync(Options("|Test|RSN_export_no_such_model_" + Guid.NewGuid().ToString("N") + ".rvt", temp.Path("m.rvt"))));

                Assert.StartsWith("IdentifyModel failed for model", ex.Message);
                Assert.Contains("ModelMissingFault", ex.Message);
                Assert.False(File.Exists(temp.Path("m.rvt")));
                await AssertNoExportLockAsync();
            }
        }

        // ---- destination ----

        [LiveRevitServerFact]
        public async Task UnwritableDestinations_FailBeforeAnythingIsDownloaded()
        {
            using (var temp = new TempDirectory())
            {
                var readOnly = temp.Path("readonly.rvt");
                File.WriteAllText(readOnly, "previous");
                File.SetAttributes(readOnly, FileAttributes.ReadOnly);
                var locked = temp.Path("locked.rvt");
                File.WriteAllText(locked, "previous");
                var folder = temp.Create("folder.rvt");
                try
                {
                    using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        await AssertFailsWithoutDownloading<UnauthorizedAccessException>(readOnly);
                        await AssertFailsWithoutDownloading<IOException>(locked);
                    }
                    await AssertFailsWithoutDownloading<IOException>(folder);
                }
                finally
                {
                    File.SetAttributes(readOnly, FileAttributes.Normal);
                }

                Assert.Equal("previous", File.ReadAllText(readOnly));
                Assert.Equal("previous", File.ReadAllText(locked));
                await AssertNoExportLockAsync();
            }
        }

        private static async Task AssertFailsWithoutDownloading<TException>(string destination) where TException : Exception
        {
            var downloaded = new List<long>();
            var options = new RsModelExporterOptions
            {
                ServerHost = LiveServer.Host,
                RevitVersion = LiveServer.Year,
                ModelPipePath = LiveServer.Model,
                DestinationFile = destination,
                Overwrite = true,
            };

            var stopwatch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TException>(() => new RsModelExporter().ExportAsync(options, new SyncProgress(downloaded.Add)));

            // The download of this model takes seconds and reports its progress as it goes. The bound is generous: it only separates
            // "failed before the download" from "downloaded the model", and includes the first call's JIT and assembly load.
            Assert.Empty(downloaded);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"{destination}: failed after {stopwatch.Elapsed}");
        }

        // ---- cancellation ----

        // Cancelling while WCF connects to a host that does not answer used to block: the cancellation callback (Cancel itself) and the
        // cleanup of the export waited for the connect, which ends only when the OS gives up (about 20 s). The host here is a local
        // listener that accepts the TCP connection and never speaks net.tcp: the connect stays pending, whatever the network.
        [LiveRevitServerFact]
        public async Task CancellationWhileConnecting_ReturnsFromCancelAndEndsTheExportAtOnce()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            System.Net.Sockets.TcpClient accepted = null;
            try
            {
                using (var temp = new TempDirectory())
                using (var cts = new CancellationTokenSource())
                {
                    var options = Options(LiveServer.Model, temp.Path("m.rvt"));
                    options.ServerHost = "127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                    var acceptTask = listener.AcceptTcpClientAsync();
                    var export = Task.Run(() => ModelExporter.ExportAsync(options, null, cts.Token));
                    var first = await Task.WhenAny(acceptTask, export, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                    Assert.Same(acceptTask, first);
                    accepted = acceptTask.Result;
                    // The connection is up and the WCF handshake is waiting for an answer that never comes.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    Assert.False(export.IsCompleted);

                    var stopwatch = Stopwatch.StartNew();
                    cts.Cancel();
                    var cancelTime = stopwatch.Elapsed;
                    var ex = await Record.ExceptionAsync(() => export).ConfigureAwait(false);

                    Assert.IsAssignableFrom<OperationCanceledException>(ex);
                    Assert.True(cancelTime < TimeSpan.FromSeconds(5), $"Cancel() took {cancelTime}");
                    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"the export ended {stopwatch.Elapsed} after the cancellation");
                }
            }
            finally
            {
                accepted?.Close();
                listener.Stop();
            }
        }

        // ---- central path of a local copy ----

        // RevitServerTool createLocalRvt writes the server name exactly as given to -s, with a port if one is given (checked with
        // Revit 2022's tool: "-s localhost:808" gives RSN://localhost:808/...). CreateLocal does the same: stripping the port would point
        // the copy at the default port of another server.
        [LiveRevitServerToolFact]
        public async Task CreateLocal_CentralPathIsTheOneRevitServerToolWrites()
        {
            using (var temp = new TempDirectory())
            {
                var viaTool = Path.Combine(temp.Create("tool output"), "m.rvt");
                var viaLibrary = temp.Path("library.rvt");

                var result = await RevitServerToolClient.CreateLocalModelAsync(FindTool(), LiveServer.Host, RelativeModel, viaTool);
                await ModelExporter.ExportAsync(Options(LiveServer.Model, viaLibrary, createLocal: true));

                Assert.True(result.Success, result.StandardOutput + result.StandardError);
                Assert.Equal(CentralPath(viaTool), CentralPath(viaLibrary));
                Assert.StartsWith("RSN://" + LiveServer.Host + "/", CentralPath(viaLibrary));
                await AssertNoExportLockAsync();
            }
        }

        // ---- RevitServerToolClient with the real tool ----

        // The tool parses its command line like any .NET Framework program: the quoted host, a destination folder with a space and
        // a model path of the server's form must arrive as separate arguments.
        [LiveRevitServerToolFact]
        public async Task ToolClient_RealTool_AcceptsTheQuotedArguments_AndADestinationFolderWithASpace()
        {
            using (var temp = new TempDirectory())
            {
                var destination = Path.Combine(temp.Create("out dir"), "m.rvt");

                var result = await RevitServerToolClient.CreateLocalModelAsync(FindTool(), LiveServer.Host, RelativeModel, destination);

                Assert.True(result.Success, result.StandardOutput + result.StandardError);
                Assert.True(new FileInfo(destination).Length > 0);
            }
        }

        [LiveRevitServerToolFact]
        public async Task ToolClient_RealTool_DestinationWithoutADirectoryPart_IsCreatedInTheCurrentDirectory()
        {
            var name = "RSN_export_" + Guid.NewGuid().ToString("N") + ".rvt";
            try
            {
                var result = await RevitServerToolClient.CreateLocalModelAsync(FindTool(), LiveServer.Host, RelativeModel, name);

                Assert.True(result.Success, result.StandardOutput + result.StandardError);
                Assert.True(new FileInfo(Path.GetFullPath(name)).Length > 0);
            }
            finally
            {
                File.Delete(Path.GetFullPath(name));
            }
        }

        // ---- helpers ----

        /// <summary>
        /// REST DirectoryInfo of the model: no lock in progress may belong to the export's user name.
        /// </summary>
        private static async Task AssertNoExportLockAsync()
        {
            var api = new RevitServerApi(LiveServer.RestHost, "RevitServerNet.Tests", false, LiveServer.Year);
            var json = await api.GetAsync(RevitServerApi.EncodePath(LiveServer.Model) + "/DirectoryInfo");
            var locks = JObject.Parse(json)["ModelLocksInProgress"] as JArray;
            var users = locks == null ? new List<string>() : locks.Select(l => (string)l["UserName"]).ToList();
            Assert.DoesNotContain(RsModelExporter.ExportUserName, users);
        }

        /// <summary>
        /// CentralPath of the BasicFileInfo of an RVT file, read with the Autodesk Helper of the bundled set (ModelBasicFileInfoStream).
        /// </summary>
        private static string CentralPath(string rvtPath)
        {
            var set = RsAssemblyLoader.Load(LiveServer.Year, null);
            var formatVersion = Enum.Parse(set.GetRequiredType("Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.DataFormatVersion"), "Latest");
            var streamType = set.GetRequiredType("Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.ModelStorage.ModelBasicFileInfoStream");
            var stream = Activator.CreateInstance(streamType, formatVersion);
            var info = streamType.GetMethod("ReadFromRVTFile").Invoke(stream, new object[] { rvtPath });
            Assert.NotNull(info);
            return (string)info.GetType().GetProperty("CentralPath").GetValue(info);
        }

        private sealed class SyncProgress : IProgress<long>
        {
            private readonly Action<long> _report;

            public SyncProgress(Action<long> report)
            {
                _report = report;
            }

            public void Report(long value) => _report(value);
        }
    }
}
