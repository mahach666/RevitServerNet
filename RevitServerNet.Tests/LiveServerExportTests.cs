using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitServerNet.Enterprise;
using RevitServerNet.Extensions;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Settings of the live Revit Server used by <see cref="LiveServerExportTests"/>, from environment variables:
    /// RSN_TEST_RS_HOST (net.tcp host[:port] of the ModelService), RSN_TEST_RS_YEAR (server year),
    /// RSN_TEST_RS_MODEL (pipe path of a model; may be percent-encoded), optional RSN_TEST_RS_REST_HOST
    /// (HTTP host[:port] of the REST service used to check locks; default RSN_TEST_RS_HOST).
    /// </summary>
    internal static class LiveServer
    {
        public const string SkipReason =
            "Live Revit Server test: set RSN_TEST_RS_HOST (net.tcp host[:port]), RSN_TEST_RS_YEAR and RSN_TEST_RS_MODEL (model pipe path); "
            + "optional RSN_TEST_RS_REST_HOST (HTTP host[:port], default RSN_TEST_RS_HOST).";

        public static string Host => Get("RSN_TEST_RS_HOST");
        public static string Year => Get("RSN_TEST_RS_YEAR");
        public static string RestHost => Get("RSN_TEST_RS_REST_HOST") ?? Host;

        public static string Model
        {
            get
            {
                var value = Get("RSN_TEST_RS_MODEL");
                return value != null && value.Contains("%") ? Uri.UnescapeDataString(value) : value;
            }
        }

        public static bool IsConfigured => Host != null && Year != null && Model != null;

        /// <summary>
        /// The ModelService runs on the REST host, on the default net.tcp port (what ExportModelAsync assumes).
        /// </summary>
        public static bool ModelServiceOnRestHost =>
            IsConfigured && string.Equals(new Uri("http://" + RestHost).Host, Host, StringComparison.OrdinalIgnoreCase);

        private static string Get(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Fact that runs only when <see cref="LiveServer"/> is configured.
    /// </summary>
    public sealed class LiveRevitServerFactAttribute : FactAttribute
    {
        /// <param name="modelServiceOnRestHost">Also require the ModelService on the REST host's default net.tcp port.</param>
        public LiveRevitServerFactAttribute(bool modelServiceOnRestHost = false)
        {
            if (!LiveServer.IsConfigured)
                Skip = LiveServer.SkipReason;
            else if (modelServiceOnRestHost && !LiveServer.ModelServiceOnRestHost)
                Skip = "RSN_TEST_RS_HOST is not the host of RSN_TEST_RS_REST_HOST: ExportModelAsync reaches the ModelService on the REST host's default net.tcp port.";
        }
    }

    /// <summary>
    /// Direct export against a live Revit Server (skipped unless configured, see <see cref="LiveServer"/>).
    /// The tests of this class run one after another, also across test processes (a named semaphore): they check the server read lock
    /// of the export's user name, which exports from another process (for example the other target framework's test host in a runner that
    /// runs them in parallel) would hold at the same time.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class LiveServerExportTests : IDisposable
    {
        private readonly Semaphore _liveTestsLock;

        public LiveServerExportTests()
        {
            // A semaphore, not a mutex: xUnit may run the constructor and Dispose of an async test on different threads.
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

        private static ModelExporterOptions Options(string destination, bool createLocal)
        {
            return new ModelExporterOptions
            {
                ServerHost = LiveServer.Host,
                RevitVersion = LiveServer.Year,
                ModelPipePath = LiveServer.Model,
                DestinationFile = destination,
                CreateLocal = createLocal,
            };
        }

        private static string ModelFileName => LiveServer.Model.Split('|', '\\').Last();

        [LiveRevitServerFact]
        public async Task DefaultExport_KeepsTheServerBasicFileInfo_AndReleasesTheLock()
        {
            using (var temp = new TempDirectory())
            {
                var destination = temp.Path(ModelFileName);
                var reports = new List<long>();

                var result = await ModelExporter.ExportAsync(Options(destination, false), new SyncProgress(reports.Add));

                Assert.Equal(destination, result);
                Assert.True(new FileInfo(destination).Length > 0);
                Assert.NotEmpty(reports);
                var info = BasicFileInfo.Read(destination);
                Assert.Equal("WS_Central", info.WorksharingState);
                Assert.Equal(info.CentralIdentity, info.Identity);
                await AssertNoExportLockAsync();
            }
        }

        [LiveRevitServerFact]
        public async Task CreateLocal_WritesTheBasicFileInfoOfALocalCopy_AndReleasesTheLock()
        {
            using (var temp = new TempDirectory())
            {
                var destination = temp.Path(ModelFileName);

                await ModelExporter.ExportAsync(Options(destination, true));

                var info = BasicFileInfo.Read(destination);
                Assert.Equal("WS_CreatedLocal", info.WorksharingState);
                var relativePath = LiveServer.Model.TrimStart('|').Replace('|', '/').Replace('\\', '/');
                Assert.Equal("RSN://" + LiveServer.Host + "/" + relativePath, info.CentralPath);
                Assert.NotEqual(Guid.Empty, info.CentralIdentity);
                Assert.NotEqual(info.CentralIdentity, info.Identity);
                await AssertNoExportLockAsync();
            }
        }

        [LiveRevitServerFact]
        public async Task CancellationDuringDownload_Throws_ReleasesTheLock_AndLeavesNoFiles()
        {
            using (var temp = new TempDirectory())
            using (var cts = new CancellationTokenSource())
            {
                var destination = temp.Path(ModelFileName);
                // The export's own work root, not %TEMP%: other exports on this machine do not affect the check.
                var workRoot = temp.Create("work");
                var options = new RsModelExporterOptions
                {
                    ServerHost = LiveServer.Host,
                    RevitVersion = LiveServer.Year,
                    ModelPipePath = LiveServer.Model,
                    DestinationFile = destination,
                    WorkRoot = workRoot,
                };
                var cancelledAt = -1L;
                var progress = new SyncProgress(bytes =>
                {
                    if (cancelledAt < 0)
                    {
                        cancelledAt = bytes;
                        cts.Cancel();
                    }
                });

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RsModelExporter().ExportAsync(options, progress, cts.Token));

                Assert.True(cancelledAt > 0);
                Assert.False(File.Exists(destination));
                Assert.Empty(Directory.GetFileSystemEntries(workRoot));
                await AssertNoExportLockAsync();
            }
        }

        [LiveRevitServerFact(modelServiceOnRestHost: true)]
        public async Task ExportModelAsync_ReachesTheModelServiceOnTheRestHostWithoutItsHttpPort()
        {
            using (var temp = new TempDirectory())
            {
                var api = new RevitServerApi(LiveServer.RestHost, "RevitServerNet.Tests", false, LiveServer.Year);
                var destination = temp.Path(ModelFileName);

                // The signature of 1.2.x: it must keep the server's BasicFileInfo (createLocal false).
                await api.ExportModelAsync(LiveServer.Model, destination);

                Assert.True(File.Exists(destination));
                Assert.Equal("WS_Central", BasicFileInfo.Read(destination).WorksharingState);
                await AssertNoExportLockAsync();
            }
        }

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

        private sealed class SyncProgress : IProgress<long>
        {
            private readonly Action<long> _report;

            public SyncProgress(Action<long> report)
            {
                _report = report;
            }

            public void Report(long value) => _report(value);
        }

        /// <summary>
        /// BasicFileInfo of an RVT file, read with the Autodesk Helper of the bundled set (ModelBasicFileInfoStream).
        /// </summary>
        private sealed class BasicFileInfo
        {
            public string WorksharingState { get; private set; }
            public string CentralPath { get; private set; }
            public Guid Identity { get; private set; }
            public Guid CentralIdentity { get; private set; }

            public static BasicFileInfo Read(string rvtPath)
            {
                var set = RsAssemblyLoader.Load(LiveServer.Year, null);
                var formatVersion = Enum.Parse(set.GetRequiredType("Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.DataFormatVersion"), "Latest");
                var streamType = set.GetRequiredType("Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.ModelStorage.ModelBasicFileInfoStream");
                var stream = Activator.CreateInstance(streamType, formatVersion);
                var info = streamType.GetMethod("ReadFromRVTFile").Invoke(stream, new object[] { rvtPath });
                Assert.NotNull(info);
                object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
                Guid IdentityGuid(object identity) => (Guid)Get(Get(identity, "IdentityGUID"), "GUID");
                return new BasicFileInfo
                {
                    WorksharingState = Get(info, "WorksharingState").ToString(),
                    CentralPath = (string)Get(info, "CentralPath"),
                    Identity = IdentityGuid(Get(info, "Identity")),
                    CentralIdentity = IdentityGuid(Get(info, "CentralIdentity")),
                };
            }
        }
    }
}
