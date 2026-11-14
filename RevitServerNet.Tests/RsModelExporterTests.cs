using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using RevitServerNet.Enterprise;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Detail type of a lock contention fault; the export recognises the fault by this type name.
    /// </summary>
    public sealed class DataLockContentionFault
    {
    }

    /// <summary>
    /// Export flow of <see cref="RsModelExporter"/> with a fake server session: lock lifecycle, retries, error reporting,
    /// destination and temp folder handling, and the model mutex. No server is contacted.
    /// </summary>
    public class RsModelExporterTests
    {
        private static readonly RsExportRetryPolicy NoDelays = new RsExportRetryPolicy(5, TimeSpan.Zero, 5, TimeSpan.Zero, TimeSpan.Zero);

        private static RsModelExporterOptions Options(TempDirectory temp, bool overwrite = false, bool createLocal = false)
        {
            return new RsModelExporterOptions
            {
                ServerHost = "fake.invalid",
                // A model per test: exports of the same model wait for each other (named mutex).
                ModelPipePath = "|Tests|" + Guid.NewGuid().ToString("N") + ".rvt",
                DestinationFile = temp.Path(@"out\model.rvt"),
                RevitVersion = "2024",
                Overwrite = overwrite,
                CreateLocal = createLocal,
                WorkRoot = temp.Create("work"),
            };
        }

        private static Task<string> Export(RsModelExporterOptions options, FakeSession session, RsExportRetryPolicy policy = null,
            IProgress<long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            return new RsModelExporter().ExportAsync(options, progress, ct, (o, endpoint, where) => session, policy ?? NoDelays);
        }

        private static void AssertNoLeftovers(RsModelExporterOptions options)
        {
            Assert.Empty(Directory.GetFileSystemEntries(options.WorkRoot));
            var destinationDir = Path.GetDirectoryName(options.DestinationFile);
            if (Directory.Exists(destinationDir))
                Assert.Empty(Directory.GetFiles(destinationDir, "*.partial"));
        }

        // ---- success ----

        [Fact]
        public async Task Success_CallsInOrder_ReleasesTheLockOnce_AndWritesTheDestination()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var options = Options(temp);
                var reports = new List<long>();

                var result = await Export(options, session, progress: new TestProgress(reports.Add));

                Assert.Equal(options.DestinationFile, result);
                Assert.Equal(
                    new[]
                    {
                        "IdentifyModel", "LockData", "GetListOfModelDataFiles",
                        "DownloadFile a.dat", "DisposeResponse", "DownloadFile b.dat", "DisposeResponse",
                        "ModelDataFormatVersion", "UnlockData", "Close", "GenerateRvtFile",
                    },
                    session.Calls);
                Assert.Equal("rvt from a.dat,b.dat", File.ReadAllText(options.DestinationFile));
                Assert.Equal(new[] { false }, session.UnlockCancelledFlags);
                Assert.Equal(new[] { 81920L, 163840L, FakeSession.FileSize, 81920L, 163840L, FakeSession.FileSize }, reports);
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task CreateLocal_RewritesTheBasicFileInfoAfterTheRvtIsAssembled()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var options = Options(temp, createLocal: true);

                await Export(options, session);

                var calls = session.Calls;
                Assert.Equal(calls.IndexOf("GenerateRvtFile") + 1, calls.IndexOf("MakeCreatedLocal"));
                Assert.True(File.Exists(options.DestinationFile));
            }
        }

        // ---- LockData ----

        [Fact]
        public async Task LockDataFault_IsReported_WithoutUnlock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Lock = mark =>
                {
                    mark();
                    throw new FaultException("the model is locked by an administrator");
                };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith("LockData failed for", ex.Message);
                Assert.IsType<FaultException>(ex.InnerException);
                Assert.Equal(1, session.Count("LockData"));
                Assert.Equal(0, session.Count("UnlockData"));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task LockDataBusy_IsRetried_ThenReported_WithoutUnlock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Lock = mark =>
                {
                    mark();
                    return "Busy";
                };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains("LockData returned Busy", ex.Message);
                Assert.Contains("after 5 attempts", ex.Message);
                Assert.Equal(5, session.Count("LockData"));
                Assert.Equal(0, session.Count("UnlockData"));
            }
        }

        [Fact]
        public async Task LockDataBusy_ThenLocked_Succeeds()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var answers = new Queue<string>(new[] { "Busy", "Busy", "Locked" });
                session.Lock = mark =>
                {
                    mark();
                    return answers.Dequeue();
                };
                var options = Options(temp);

                await Export(options, session);

                Assert.Equal(3, session.Count("LockData"));
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.True(File.Exists(options.DestinationFile));
            }
        }

        [Fact]
        public async Task LockDataContentionFault_IsRetried()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var attempt = 0;
                session.Lock = mark =>
                {
                    mark();
                    if (++attempt == 1) throw new FaultException<DataLockContentionFault>(new DataLockContentionFault(), new FaultReason("lock database busy"));
                    return "WasLocked";
                };
                var options = Options(temp);

                await Export(options, session);

                Assert.Equal(2, session.Count("LockData"));
                Assert.Equal(1, session.Count("UnlockData"));
            }
        }

        [Fact]
        public async Task CancellationDuringTheBusyWait_Throws_WithoutUnlock()
        {
            using (var temp = new TempDirectory())
            using (var cts = new CancellationTokenSource())
            {
                var session = new FakeSession();
                session.Lock = mark =>
                {
                    mark();
                    cts.CancelAfter(100);
                    return "Busy";
                };
                var options = Options(temp);
                var policy = new RsExportRetryPolicy(5, TimeSpan.FromSeconds(30), 5, TimeSpan.Zero, TimeSpan.Zero);
                var watch = Stopwatch.StartNew();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Export(options, session, policy, ct: cts.Token));

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "the wait before the retry was not interrupted");
                Assert.Equal(1, session.Count("LockData"));
                Assert.Equal(0, session.Count("UnlockData"));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task LockDataTimeout_ReleasesTheLock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Lock = mark =>
                {
                    mark();
                    throw new TimeoutException("no reply");
                };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.IsType<TimeoutException>(ex.InnerException);
                Assert.Equal(1, session.Count("LockData"));
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.False(ex.Data.Contains(RsModelExporter.UnlockErrorDataKey));
            }
        }

        [Fact]
        public async Task LockDataTimeout_UnlockFindsNoLock_AsksOnceMore()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Lock = mark =>
                {
                    mark();
                    throw new TimeoutException("no reply");
                };
                session.Unlock = cancelled => "WasNotLocked";
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                // The server may still apply the interrupted LockData after the first UnlockData.
                Assert.Equal(2, session.Count("UnlockData"));
                Assert.False(ex.Data.Contains(RsModelExporter.UnlockErrorDataKey));
                Assert.False(ex.Data.Contains(RsModelExporter.ReadLockLostDataKey));
            }
        }

        // ---- DownloadFile ----

        [Fact]
        public async Task DownloadFailure_ThrowsTheCopyError_NotTheCloseError_AndReleasesTheLockOnce()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.OpenStream = file => new FailingStream(new IOException("There is not enough space on the disk."));
                session.DisposeResponse = () => throw new CommunicationObjectAbortedException("the connection was aborted");
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<IOException>(() => Export(options, session));

                Assert.Equal("There is not enough space on the disk.", ex.Message);
                Assert.Contains("CommunicationObjectAbortedException", (string)ex.Data[RsModelExporter.DownloadCloseErrorDataKey]);
                var calls = session.Calls;
                // The connection is aborted before the response is disposed: WCF would otherwise read the rest of the reply.
                Assert.InRange(calls.IndexOf("Abort"), 0, calls.IndexOf("DisposeResponse") - 1);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(0, session.Count("DownloadFile b.dat"));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task CancellationDuringADownload_ReleasesTheLockWithTheShortTimeout_AndLeavesNoFiles()
        {
            using (var temp = new TempDirectory())
            using (var cts = new CancellationTokenSource())
            {
                var session = new FakeSession();
                var options = Options(temp);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Export(options, session, progress: new TestProgress(bytes => cts.Cancel()), ct: cts.Token));

                Assert.Contains("Abort", session.Calls);
                Assert.Equal(new[] { true }, session.UnlockCancelledFlags);
                Assert.Equal(0, session.Count("GenerateRvtFile"));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        // ---- UnlockData ----

        [Fact]
        public async Task UnlockFailsAfterSuccess_TheFileIsWritten_AndTheExceptionSaysSo()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Unlock = cancelled => throw new CommunicationException("connection reset");
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith($"The model was exported to '{options.DestinationFile}', but releasing the server read lock failed", ex.Message);
                Assert.Equal(options.DestinationFile, ex.Data[RsModelExporter.ExportedFileDataKey]);
                Assert.Contains("connection reset", (string)ex.Data[RsModelExporter.UnlockErrorDataKey]);
                Assert.IsType<CommunicationException>(ex.InnerException);
                Assert.Equal(5, session.Count("UnlockData"));
                Assert.True(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task UnlockFailsOnce_IsRetried_AndTheExportSucceeds()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var attempt = 0;
                session.Unlock = cancelled =>
                {
                    if (++attempt == 1) throw new CommunicationException("connection reset");
                    return "Unlocked";
                };
                var options = Options(temp);

                await Export(options, session);

                Assert.Equal(2, session.Count("UnlockData"));
                Assert.True(File.Exists(options.DestinationFile));
            }
        }

        [Fact]
        public async Task UnlockTimeout_IsNotRetried()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Unlock = cancelled => throw new TimeoutException("no reply");
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.IsType<TimeoutException>(ex.InnerException);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(options.DestinationFile, ex.Data[RsModelExporter.ExportedFileDataKey]);
            }
        }

        [Fact]
        public async Task UnlockReturnsAnUnexpectedStatus_IsReportedOnceTheFileIsWritten()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Unlock = cancelled => "Unknown";
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains("UnlockData returned Unknown", ex.Message);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(options.DestinationFile, ex.Data[RsModelExporter.ExportedFileDataKey]);
                Assert.True(File.Exists(options.DestinationFile));
            }
        }

        [Fact]
        public async Task UnlockFailsAfterAnExportError_IsAddedToThatError()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.FormatVersion = () => throw new CommunicationException("connection reset");
                session.Unlock = cancelled => throw new CommunicationException("still down");
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith("ModelDataFormatVersion failed for", ex.Message);
                Assert.Contains("still down", (string)ex.Data[RsModelExporter.UnlockErrorDataKey]);
                Assert.False(ex.Data.Contains(RsModelExporter.ExportedFileDataKey));
                Assert.False(File.Exists(options.DestinationFile));
            }
        }

        [Fact]
        public async Task AssemblingFailsAfterAnUnlockFailure_TheUnlockFailureIsAddedToThatError()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Unlock = cancelled => throw new CommunicationException("still down");
                session.Generate = (dataDir, version, rvtPath) => throw new InvalidDataException("bad stream");
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith("Assembling the RVT file", ex.Message);
                Assert.Contains("still down", (string)ex.Data[RsModelExporter.UnlockErrorDataKey]);
                Assert.False(ex.Data.Contains(RsModelExporter.ExportedFileDataKey));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task LockAlreadyReleased_AfterAnExportError_IsAddedToThatError()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.FormatVersion = () => throw new CommunicationException("connection reset");
                session.Unlock = cancelled => "WasNotLocked";
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains("was already released when UnlockData ran (LockData returned Locked)", (string)ex.Data[RsModelExporter.ReadLockLostDataKey]);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.False(ex.Data.Contains(RsModelExporter.UnlockErrorDataKey));
            }
        }

        [Fact]
        public async Task LockAlreadyReleased_AfterASuccessfulExport_IsTracedAsAWarning()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                session.Unlock = cancelled => "WasNotLocked";
                var options = Options(temp);
                var listener = new CollectingTraceListener();
                Trace.Listeners.Add(listener);
                try
                {
                    await Export(options, session);
                }
                finally
                {
                    Trace.Listeners.Remove(listener);
                }

                Assert.True(File.Exists(options.DestinationFile));
                Assert.Contains(listener.Lines, line => line.Contains("was already released when UnlockData ran") && line.Contains(options.ModelPipePath));
            }
        }

        [Fact]
        public async Task WasNotLockedAfterAFailedUnlockAttempt_IsNotReportedAsALostLock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var attempt = 0;
                session.FormatVersion = () => throw new CommunicationException("connection reset");
                // The first attempt may have released the lock before its reply was lost.
                session.Unlock = cancelled =>
                {
                    if (++attempt == 1) throw new CommunicationException("connection reset");
                    return "WasNotLocked";
                };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Equal(2, session.Count("UnlockData"));
                Assert.False(ex.Data.Contains(RsModelExporter.ReadLockLostDataKey));
            }
        }

        // ---- destination and temp folders ----

        [Fact]
        public async Task DestinationInUseAtTheEnd_KeepsThePreviousFile_AndLeavesNoPartialFile()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var options = Options(temp, overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(options.DestinationFile));
                File.WriteAllText(options.DestinationFile, "previous");
                // The destination is free when the export starts and in use when the file is moved there.
                FileStream holder = null;
                var generate = session.Generate;
                session.Generate = (dataDir, version, rvtPath) =>
                {
                    generate(dataDir, version, rvtPath);
                    holder = new FileStream(options.DestinationFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                };

                try
                {
                    await Assert.ThrowsAsync<IOException>(() => Export(options, session));
                }
                finally
                {
                    holder?.Dispose();
                }

                Assert.Equal("previous", File.ReadAllText(options.DestinationFile));
                Assert.Equal(1, session.Count("UnlockData"));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public void MoveToDestination_ReplacesThePreviousFile_ThroughATemporaryNameInTheSameFolder()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.Path("model.rvt");
                File.WriteAllText(source, "new");
                var destination = temp.Path(@"out\model.rvt");
                Directory.CreateDirectory(temp.Path("out"));
                File.WriteAllText(destination, "previous");

                RsModelExporter.MoveToDestination(source, destination, overwrite: true);

                Assert.Equal("new", File.ReadAllText(destination));
                Assert.False(File.Exists(source));
                Assert.Equal(new[] { destination }, Directory.GetFiles(temp.Path("out")));
            }
        }

        [Fact]
        public void MoveToDestination_ExistingFileWithoutOverwrite_IsKept()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.Path("model.rvt");
                File.WriteAllText(source, "new");
                var destination = temp.Path("existing.rvt");
                File.WriteAllText(destination, "previous");

                Assert.Throws<IOException>(() => RsModelExporter.MoveToDestination(source, destination, overwrite: false));

                Assert.Equal("previous", File.ReadAllText(destination));
                Assert.Empty(Directory.GetFiles(temp.Root, "*.partial"));
            }
        }

        [Fact]
        public void DeleteWorkDirectory_FileInUse_IsReportedOnTheExportFailure()
        {
            using (var temp = new TempDirectory())
            {
                var work = temp.Create("RevitServerNet_" + Guid.NewGuid().ToString("N"));
                var held = Path.Combine(work, "held.dat");
                File.WriteAllText(held, "x");
                var failure = new InvalidOperationException("export failed");

                using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
                    RsModelExporter.DeleteWorkDirectory(work, failure);

                Assert.Contains(work, (string)failure.Data[RsModelExporter.TempCleanupErrorDataKey]);
            }
        }

        [Fact]
        public void SweepStaleWorkDirectories_RemovesOnlyExportFoldersOlderThanSevenDays()
        {
            using (var temp = new TempDirectory())
            {
                var root = temp.Create("root");
                var now = DateTime.UtcNow;
                string Make(string name, double ageDays)
                {
                    var dir = Path.Combine(root, name);
                    Directory.CreateDirectory(Path.Combine(dir, "data"));
                    File.WriteAllText(Path.Combine(dir, "data", "file.dat"), "x");
                    Directory.SetLastWriteTimeUtc(dir, now - TimeSpan.FromDays(ageDays));
                    return dir;
                }
                var old = Make("RevitServerNet_" + Guid.NewGuid().ToString("N"), 8);
                var oldBefore130 = Make("RevitServerNet_ModelData_" + Guid.NewGuid().ToString("N"), 30);
                var recent = Make("RevitServerNet_" + Guid.NewGuid().ToString("N"), 6);
                var oldOtherName = Make("RevitServerNet_Other", 30);
                var oldUpperCaseGuid = Make("RevitServerNet_" + Guid.NewGuid().ToString("N").ToUpperInvariant(), 30);
                var oldForeign = Make("RevitServerTool_ModelDataFileDownload_" + Guid.NewGuid().ToString("N"), 30);

                RsModelExporter.SweepStaleWorkDirectories(root, now);

                Assert.False(Directory.Exists(old));
                Assert.False(Directory.Exists(oldBefore130));
                Assert.True(Directory.Exists(recent));
                Assert.True(Directory.Exists(oldOtherName));
                Assert.True(Directory.Exists(oldUpperCaseGuid));
                Assert.True(Directory.Exists(oldForeign));
            }
        }

        [Fact]
        public void SweepStaleWorkDirectories_MissingRoot_DoesNotThrow()
        {
            using (var temp = new TempDirectory())
                RsModelExporter.SweepStaleWorkDirectories(temp.Path("missing"), DateTime.UtcNow);
        }

        // ---- model data listing ----

        [Theory]
        [InlineData("ModelDoesNotExist", "does not exist")]
        [InlineData("ModelInUse", "in use")]
        [InlineData("ModelCorrupt", "corrupt")]
        [InlineData("Failed", "could not list the model data files")]
        public async Task ListingStatusOtherThanSuccess_IsReportedWithItsExplanation_AndReleasesTheLockOnce(string status, string explanation)
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { ListStatus = status };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains($"GetListOfModelDataFilesWithoutLocking returned {status}", ex.Message);
                Assert.Contains(explanation, ex.Message);
                Assert.Contains(options.ModelPipePath, ex.Message);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(0, session.Calls.Count(c => c.StartsWith("DownloadFile")));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task EmptyOrMissingFileList_IsReported_AndReleasesTheLockOnce()
        {
            foreach (var files in new List<string>[] { new List<string>(), null })
            {
                using (var temp = new TempDirectory())
                {
                    var session = new FakeSession { Files = files };
                    var options = Options(temp);

                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                    Assert.Contains("returned no model data files", ex.Message);
                    Assert.Equal(1, session.Count("UnlockData"));
                    Assert.Equal(0, session.Count("GenerateRvtFile"));
                    AssertNoLeftovers(options);
                }
            }
        }

        [Theory]
        [InlineData("dir/")]
        [InlineData("")]
        public async Task ListedNameWithoutAFileName_IsReported_AndReleasesTheLockOnce(string listed)
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { Files = new List<string> { "a.dat", listed } };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains($"invalid file name '{listed}'", ex.Message);
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(0, session.Count("DownloadFile " + listed));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task TwoListedFilesWithTheSameName_FailTheSecondDownload_AndReleaseTheLockOnce()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { Files = new List<string> { "x/a.dat", "y/a.dat" } };
                var options = Options(temp);

                await Assert.ThrowsAsync<IOException>(() => Export(options, session));

                Assert.Equal(1, session.Count("UnlockData"));
                Assert.Equal(0, session.Count("GenerateRvtFile"));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        // ---- IdentifyModel ----

        [Fact]
        public async Task IdentifyModelFailure_EndsTheExportBeforeTheMutexAndTheLock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { Identify = ct => throw new FaultException("Model Tests\\x.rvt was not found") };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith("IdentifyModel failed for model", ex.Message);
                Assert.IsAssignableFrom<FaultException>(ex.InnerException);
                Assert.Equal(new[] { "IdentifyModel", "Close" }, session.Calls);
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task IdentifyModelReturningNoIdentity_EndsTheExportBeforeTheLock()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { Identify = ct => throw new InvalidOperationException("IdentifyModel returned no valid model identity.") };
                var options = Options(temp);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.Contains("no valid model identity", ex.Message);
                Assert.Equal(0, session.Count("LockData"));
                Assert.Equal(0, session.Count("UnlockData"));
                AssertNoLeftovers(options);
            }
        }

        /// <summary>
        /// WCF connects inside the first call, and aborting the channel does not end a connect that is pending (an unreachable host: about
        /// 20 s until the OS gives up). The export must still end when the token is cancelled.
        /// </summary>
        [Fact]
        public async Task CancellationWhileIdentifyModelIsStuckInTheConnect_EndsTheExportAtOnce()
        {
            using (var temp = new TempDirectory())
            using (var cts = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                // Ignores the abort, like the pending connect.
                var session = new FakeSession { Identify = ct => { entered.Set(); release.Wait(); } };
                var options = Options(temp);
                try
                {
                    var export = Export(options, session, ct: cts.Token);
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

                    cts.Cancel();
                    var finished = await Task.WhenAny(export, Task.Delay(TimeSpan.FromSeconds(5)));

                    Assert.Same(export, finished);
                    var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
                    Assert.Contains("cancelled during IdentifyModel", ex.Message);
                    Assert.Contains("Abort", session.Calls);
                    Assert.Equal(0, session.Count("LockData"));
                    Assert.Equal(0, session.Count("UnlockData"));
                    AssertNoLeftovers(options);
                }
                finally
                {
                    release.Set();
                }
            }
        }

        [Fact]
        public async Task IdentifyModelFailingAfterTheCancellation_IsObservedAndDoesNotChangeTheResult()
        {
            using (var temp = new TempDirectory())
            using (var cts = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                // Only this test's exception counts: UnobservedTaskException is process-wide and other classes run in parallel.
                var marker = new CommunicationObjectAbortedException("aborted " + Guid.NewGuid());
                var unobserved = new List<Exception>();
                EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) =>
                {
                    if (e.Exception.Flatten().InnerExceptions.Contains(marker)) lock (unobserved) unobserved.Add(e.Exception);
                };
                TaskScheduler.UnobservedTaskException += handler;
                try
                {
                    var session = new FakeSession { Identify = ct => { entered.Set(); release.Wait(); throw marker; } };
                    var options = Options(temp);
                    var export = Export(options, session, ct: cts.Token);
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                    cts.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);

                    // The abandoned call fails now; the export is already over.
                    release.Set();
                    Thread.Sleep(200);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    lock (unobserved) Assert.Empty(unobserved);
                }
                finally
                {
                    TaskScheduler.UnobservedTaskException -= handler;
                }
            }
        }

        // ---- CreateLocal ----

        [Fact]
        public async Task CreateLocalNotSupported_KeepsTheExceptionType_WritesNothing_AndTheLockWasReleasedBefore()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { MakeLocal = () => throw new NotSupportedException("BasicFileInfo stream version 9 is too new") };
                var options = Options(temp, createLocal: true);

                var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Export(options, session));

                Assert.StartsWith("CreateLocal failed for model", ex.Message);
                Assert.Contains("too new", ex.Message);
                Assert.IsType<NotSupportedException>(ex.InnerException);
                var calls = session.Calls;
                Assert.True(calls.IndexOf("UnlockData") < calls.IndexOf("MakeCreatedLocal"));
                Assert.Equal(1, session.Count("UnlockData"));
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task CreateLocalFailure_OtherThanNotSupported_IsWrappedInInvalidOperation()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession { MakeLocal = () => throw new IOException("disk full") };
                var options = Options(temp, createLocal: true);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Export(options, session));

                Assert.StartsWith("CreateLocal failed for model", ex.Message);
                Assert.Contains("disk full", ex.Message);
                Assert.IsType<IOException>(ex.InnerException);
                Assert.False(File.Exists(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        // ---- destination checked before the download ----

        [Fact]
        public async Task ReadOnlyDestinationWithOverwrite_FailsBeforeTheServerIsContacted_AndIsKept()
        {
            using (var temp = new TempDirectory())
            {
                var factoryCalls = 0;
                var options = Options(temp, overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(options.DestinationFile));
                File.WriteAllText(options.DestinationFile, "previous");
                File.SetAttributes(options.DestinationFile, FileAttributes.ReadOnly);
                try
                {
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        new RsModelExporter().ExportAsync(options, null, CancellationToken.None, (o, e, w) => { factoryCalls++; return new FakeSession(); }, NoDelays));
                }
                finally
                {
                    File.SetAttributes(options.DestinationFile, FileAttributes.Normal);
                }

                Assert.Equal(0, factoryCalls);
                Assert.Equal("previous", File.ReadAllText(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task DestinationOpenInAnotherProcessWithOverwrite_FailsBeforeTheServerIsContacted_AndIsKept()
        {
            using (var temp = new TempDirectory())
            {
                var factoryCalls = 0;
                var options = Options(temp, overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(options.DestinationFile));
                File.WriteAllText(options.DestinationFile, "previous");

                using (new FileStream(options.DestinationFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await Assert.ThrowsAsync<IOException>(() =>
                        new RsModelExporter().ExportAsync(options, null, CancellationToken.None, (o, e, w) => { factoryCalls++; return new FakeSession(); }, NoDelays));

                Assert.Equal(0, factoryCalls);
                Assert.Equal("previous", File.ReadAllText(options.DestinationFile));
                AssertNoLeftovers(options);
            }
        }

        [Fact]
        public async Task DestinationThatIsAFolder_FailsBeforeTheServerIsContacted()
        {
            using (var temp = new TempDirectory())
            {
                var factoryCalls = 0;
                var options = Options(temp, overwrite: true);
                Directory.CreateDirectory(options.DestinationFile);

                var ex = await Assert.ThrowsAsync<IOException>(() =>
                    new RsModelExporter().ExportAsync(options, null, CancellationToken.None, (o, e, w) => { factoryCalls++; return new FakeSession(); }, NoDelays));

                Assert.Contains("is an existing folder", ex.Message);
                Assert.Equal(0, factoryCalls);
                Assert.True(Directory.Exists(options.DestinationFile));
            }
        }

        [Fact]
        public async Task WritableNewDestination_IsProbedWithoutLeavingAnything()
        {
            using (var temp = new TempDirectory())
            {
                var session = new FakeSession();
                var options = Options(temp);

                await Export(options, session);

                AssertNoLeftovers(options);
                Assert.Equal(new[] { "model.rvt" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(options.DestinationFile)).Select(Path.GetFileName).ToArray());
            }
        }

        [Theory]
        [InlineData("a<b>.rvt")]
        [InlineData("a|b.rvt")]
        [InlineData("a?.rvt")]
        [InlineData("dir:model.rvt")]
        [InlineData(@"out\")]
        public void DestinationWithoutAValidFileName_IsRejectedByTheOptionsCheck(string destinationName)
        {
            using (var temp = new TempDirectory())
            {
                var options = Options(temp);
                options.DestinationFile = temp.Root + Path.DirectorySeparatorChar + destinationName;

                var ex = Assert.Throws<ArgumentException>(() => RsModelExporter.ValidateOptions(options));

                Assert.Equal("DestinationFile", ex.ParamName);
                Assert.False(Directory.Exists(Path.Combine(temp.Root, "out")));
            }
        }

        [Fact]
        public void ValidOptions_PassTheOptionsCheck()
        {
            using (var temp = new TempDirectory())
                Assert.Equal("net.tcp", RsModelExporter.ValidateOptions(Options(temp)).Scheme);
        }

        [Theory]
        [InlineData("|")]
        [InlineData("||")]
        [InlineData(@"\")]
        public void ModelPathWithoutAnyName_IsRejectedByTheOptionsCheck(string modelPath)
        {
            using (var temp = new TempDirectory())
            {
                var options = Options(temp);
                options.ModelPipePath = modelPath;

                Assert.Equal("ModelPipePath", Assert.Throws<ArgumentException>(() => RsModelExporter.ValidateOptions(options)).ParamName);
            }
        }

        // ---- model path ----

        // The ModelService answered ModelMissingFault for the doubled, leading and trailing separators (Revit Server 2022 and 2025)
        // and found the model for the other spellings.
        [Theory]
        [InlineData("|Test|Model.rvt", @"Test\Model.rvt")]
        [InlineData("|Test|Model.rvt|", @"Test\Model.rvt")]
        [InlineData("|Test|Model.rvt||", @"Test\Model.rvt")]
        [InlineData("||Test||Model.rvt", @"Test\Model.rvt")]
        [InlineData("Test|Model.rvt", @"Test\Model.rvt")]
        [InlineData("Test/Model.rvt", @"Test\Model.rvt")]
        [InlineData("|Test/Sub|Model.rvt", @"Test\Sub\Model.rvt")]
        [InlineData(@"Test\Model.rvt", @"Test\Model.rvt")]
        [InlineData(@"\Test\Model.rvt\", @"Test\Model.rvt")]
        [InlineData("  |Test|Model.rvt|  ", @"Test\Model.rvt")]
        [InlineData("|Test|Модель #1.rvt", @"Test\Модель #1.rvt")]
        [InlineData("|", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void NormalizeModelPath_GivesSingleBackslashSeparatorsWithoutLeadingOrTrailingOnes(string given, string expected)
        {
            Assert.Equal(expected, RsModelExporter.NormalizeModelPath(given));
        }

        // ---- LockData retry helper ----

        [Fact]
        public void LockWithRetry_BusyThenLocked_ReturnsLocked()
        {
            var answers = new Queue<string>(new[] { "Busy", "Locked" });

            var status = RsModelExporter.LockWithRetry(() => answers.Dequeue(), 5, TimeSpan.Zero, CancellationToken.None, out var attempts);

            Assert.Equal("Locked", status);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public void LockWithRetry_AlwaysBusy_ReturnsBusyAfterTheLastAttempt()
        {
            var calls = 0;

            var status = RsModelExporter.LockWithRetry(() => { calls++; return "Busy"; }, 5, TimeSpan.Zero, CancellationToken.None, out var attempts);

            Assert.Equal("Busy", status);
            Assert.Equal(5, attempts);
            Assert.Equal(5, calls);
        }

        [Theory]
        [InlineData("Missing")]
        [InlineData("GaveUp")]
        [InlineData("Locked")]
        public void LockWithRetry_OtherStatus_IsNotRetried(string answer)
        {
            var calls = 0;

            var status = RsModelExporter.LockWithRetry(() => { calls++; return answer; }, 5, TimeSpan.Zero, CancellationToken.None, out var attempts);

            Assert.Equal(answer, status);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void LockWithRetry_OtherFault_IsNotRetried()
        {
            var calls = 0;

            Assert.Throws<InvalidOperationException>(() => RsModelExporter.LockWithRetry(() =>
            {
                calls++;
                throw new InvalidOperationException("LockData failed", new FaultException("admin lock"));
            }, 5, TimeSpan.Zero, CancellationToken.None, out var attempts));

            Assert.Equal(1, calls);
        }

        [Fact]
        public void LockWithRetry_ContentionFaultOnEveryAttempt_ThrowsWithTheNumberOfAttempts()
        {
            var calls = 0;

            var ex = Assert.Throws<InvalidOperationException>(() => RsModelExporter.LockWithRetry(() =>
            {
                calls++;
                throw new InvalidOperationException("LockData failed", new FaultException<DataLockContentionFault>(new DataLockContentionFault(), new FaultReason("busy")));
            }, 3, TimeSpan.Zero, CancellationToken.None, out var attempts));

            Assert.Equal(3, calls);
            Assert.EndsWith("(after 3 attempts)", ex.Message);
            Assert.IsType<FaultException<DataLockContentionFault>>(ex.InnerException);
        }

        [Fact]
        public void LockWithRetry_CancelledDuringTheWait_Throws()
        {
            using (var cts = new CancellationTokenSource())
            {
                var calls = 0;
                var watch = Stopwatch.StartNew();

                Assert.ThrowsAny<OperationCanceledException>(() => RsModelExporter.LockWithRetry(() =>
                {
                    calls++;
                    cts.CancelAfter(100);
                    return "Busy";
                }, 5, TimeSpan.FromSeconds(30), cts.Token, out var attempts));

                Assert.Equal(1, calls);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
            }
        }

        // ---- model mutex ----

        [Fact]
        public void ModelMutexName_IsRevitServerToolsName_HashedOnlyAboveTheMutexNameLimit()
        {
            Assert.Equal("RevitServerTool:PC:1_Projects:A.rvt_rs", RsModelExporter.GetModelMutexName("RevitServerTool:PC:1", @"Projects\A.rvt", "rs"));

            const string user = "RevitServerNet.Tests";
            const string host = "rs";
            var longest = @"P\" + new string('m', RsModelExporter.MaxModelMutexNameLength - user.Length - host.Length - 4);
            var name = RsModelExporter.GetModelMutexName(user, longest, host);
            Assert.Equal(RsModelExporter.MaxModelMutexNameLength, name.Length);
            Assert.Equal(user + "_" + longest.Replace('\\', ':') + "_" + host, name);
            // The longest name used as is can be created on both runtimes.
            new Mutex(false, name).Dispose();

            var hashed = RsModelExporter.GetModelMutexName(user, longest + "m", host);
            Assert.StartsWith("RevitServerNet_", hashed);
            Assert.Equal("RevitServerNet_".Length + 64, hashed.Length);
        }

        [Fact]
        public void ModelMutex_SecondExportWaitsUntilTheFirstReleases()
        {
            var model = @"Tests\" + Guid.NewGuid().ToString("N") + ".rvt";
            var errors = new List<Exception>();
            using (var firstHolds = new ManualResetEventSlim())
            using (var releaseFirst = new ManualResetEventSlim())
            using (var secondHolds = new ManualResetEventSlim())
            {
                var first = Run(() =>
                {
                    var mutex = RsModelExporter.AcquireModelMutex("RevitServerNet.Tests", model, "host", "test model", CancellationToken.None);
                    firstHolds.Set();
                    releaseFirst.Wait();
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }, errors);
                Assert.True(firstHolds.Wait(TimeSpan.FromSeconds(10)));

                var second = Run(() =>
                {
                    var mutex = RsModelExporter.AcquireModelMutex("RevitServerNet.Tests", model, "host", "test model", CancellationToken.None);
                    secondHolds.Set();
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }, errors);

                Assert.False(secondHolds.Wait(TimeSpan.FromMilliseconds(300)));
                releaseFirst.Set();
                Assert.True(secondHolds.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(first.Join(TimeSpan.FromSeconds(10)));
                Assert.True(second.Join(TimeSpan.FromSeconds(10)));
                Assert.Empty(errors);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ModelMutex_AbandonedByItsOwner_IsOwnedByTheNextExport(bool cancellableToken)
        {
            var model = @"Tests\" + Guid.NewGuid().ToString("N") + ".rvt";
            var name = RsModelExporter.GetModelMutexName("RevitServerNet.Tests", model, "host");
            Mutex abandoned = null;
            // A thread that ends while it owns the mutex abandons it (a crashed exporter); the handle stays open here.
            var owner = new Thread(() =>
            {
                abandoned = new Mutex(false, name);
                abandoned.WaitOne();
            }) { IsBackground = true };
            owner.Start();
            Assert.True(owner.Join(TimeSpan.FromSeconds(10)));
            try
            {
                using (var cts = new CancellationTokenSource())
                {
                    var token = cancellableToken ? cts.Token : CancellationToken.None;
                    var errors = new List<Exception>();
                    var acquired = false;
                    // A mutex is released by the thread that owns it.
                    var next = Run(() =>
                    {
                        var mutex = RsModelExporter.AcquireModelMutex("RevitServerNet.Tests", model, "host", "test model", token);
                        acquired = true;
                        mutex.ReleaseMutex();
                        mutex.Dispose();
                    }, errors);
                    Assert.True(next.Join(TimeSpan.FromSeconds(10)));
                    Assert.Empty(errors);
                    Assert.True(acquired);
                }
            }
            finally
            {
                GC.KeepAlive(abandoned);
                abandoned?.Dispose();
            }
        }

        [Fact]
        public void ModelMutex_CancelledWait_ThrowsWithoutTakingTheMutex()
        {
            var model = @"Tests\" + Guid.NewGuid().ToString("N") + ".rvt";
            var name = RsModelExporter.GetModelMutexName("RevitServerNet.Tests", model, "host");
            var errors = new List<Exception>();
            using (var firstHolds = new ManualResetEventSlim())
            using (var releaseFirst = new ManualResetEventSlim())
            using (var cts = new CancellationTokenSource())
            {
                var first = Run(() =>
                {
                    var mutex = RsModelExporter.AcquireModelMutex("RevitServerNet.Tests", model, "host", "test model", CancellationToken.None);
                    firstHolds.Set();
                    releaseFirst.Wait();
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }, errors);
                Assert.True(firstHolds.Wait(TimeSpan.FromSeconds(10)));

                Exception secondError = null;
                var secondOwnsTheMutex = true;
                var second = Run(() =>
                {
                    try
                    {
                        RsModelExporter.AcquireModelMutex("RevitServerNet.Tests", model, "host", "test model", cts.Token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        secondError = ex;
                        // A mutex is re-entrant: a thread that owned it would get it again at once.
                        using (var probe = Mutex.OpenExisting(name))
                        {
                            secondOwnsTheMutex = probe.WaitOne(0);
                            if (secondOwnsTheMutex) probe.ReleaseMutex();
                        }
                    }
                }, errors);
                Thread.Sleep(200);
                cts.Cancel();

                Assert.True(second.Join(TimeSpan.FromSeconds(10)));
                releaseFirst.Set();
                Assert.True(first.Join(TimeSpan.FromSeconds(10)));
                Assert.Empty(errors);
                Assert.IsAssignableFrom<OperationCanceledException>(secondError);
                Assert.False(secondOwnsTheMutex);
            }
        }

        private static Thread Run(Action action, List<Exception> errors)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add(ex);
                }
            }) { IsBackground = true };
            thread.Start();
            return thread;
        }

        // ---- fakes ----

        /// <summary>
        /// Server session that records its calls; every step can be replaced.
        /// </summary>
        private sealed class FakeSession : IModelExportSession
        {
            public const long FileSize = 200000;

            private readonly List<string> _calls = new List<string>();
            private readonly List<bool> _unlockCancelledFlags = new List<bool>();

            public Func<Action, string> Lock = mark =>
            {
                mark();
                return "Locked";
            };
            public List<string> Files = new List<string> { "a.dat", "b.dat" };
            public string ListStatus = "Success";
            public Action<CancellationToken> Identify = ct => { };
            public Action MakeLocal = () => { };
            public Func<string, Stream> OpenStream = file => new MemoryStream(Enumerable.Repeat((byte)7, (int)FileSize).ToArray());
            public Action DisposeResponse = () => { };
            public Func<object> FormatVersion = () => "Latest";
            public Func<bool, string> Unlock = cancelled => "Unlocked";
            public Action<string, object, string> Generate = (dataDir, version, rvtPath) =>
                File.WriteAllText(rvtPath, "rvt from " + string.Join(",", Directory.GetFiles(dataDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)));

            public List<string> Calls
            {
                get { lock (_calls) return _calls.ToList(); }
            }

            public List<bool> UnlockCancelledFlags
            {
                get { lock (_calls) return _unlockCancelledFlags.ToList(); }
            }

            public int Count(string call) => Calls.Count(c => c == call);

            private void Record(string call)
            {
                lock (_calls) _calls.Add(call);
            }

            public void IdentifyModel(CancellationToken ct)
            {
                Record("IdentifyModel");
                Identify(ct);
            }

            public string LockData(Action lockMayBeHeld, CancellationToken ct)
            {
                Record("LockData");
                return Lock(lockMayBeHeld);
            }

            public string GetListOfModelDataFiles(CancellationToken ct, out List<string> files)
            {
                Record("GetListOfModelDataFiles");
                files = Files;
                return ListStatus;
            }

            public IDisposable DownloadFile(string file, CancellationToken ct, out Stream stream)
            {
                Record("DownloadFile " + file);
                stream = OpenStream(file);
                return new Response(this);
            }

            public object ModelDataFormatVersion(CancellationToken ct)
            {
                Record("ModelDataFormatVersion");
                return FormatVersion();
            }

            public string UnlockData(bool cancelled)
            {
                lock (_calls)
                {
                    _calls.Add("UnlockData");
                    _unlockCancelledFlags.Add(cancelled);
                }
                return Unlock(cancelled);
            }

            public void Abort() => Record("Abort");

            public void Close() => Record("Close");

            public void GenerateRvtFile(string dataDirectory, object dataFormatVersion, string rvtPath)
            {
                Record("GenerateRvtFile");
                Generate(dataDirectory, dataFormatVersion, rvtPath);
            }

            public void MakeCreatedLocal(string rvtPath, object dataFormatVersion)
            {
                Record("MakeCreatedLocal");
                MakeLocal();
            }

            private sealed class Response : IDisposable
            {
                private readonly FakeSession _owner;

                public Response(FakeSession owner)
                {
                    _owner = owner;
                }

                public void Dispose()
                {
                    _owner.Record("DisposeResponse");
                    _owner.DisposeResponse();
                }
            }
        }

        /// <summary>
        /// Readable stream whose Read throws.
        /// </summary>
        private sealed class FailingStream : Stream
        {
            private readonly Exception _error;

            public FailingStream(Exception error)
            {
                _error = error;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw _error;
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class CollectingTraceListener : TraceListener
        {
            private readonly List<string> _lines = new List<string>();

            public List<string> Lines
            {
                get { lock (_lines) return _lines.ToList(); }
            }

            public override void Write(string message)
            {
            }

            public override void WriteLine(string message)
            {
                lock (_lines) _lines.Add(message);
            }
        }
    }

    /// <summary>
    /// IProgress that reports synchronously on the reporting thread.
    /// </summary>
    internal sealed class TestProgress : IProgress<long>
    {
        private readonly Action<long> _report;

        public TestProgress(Action<long> report)
        {
            _report = report;
        }

        public void Report(long value) => _report(value);
    }
}
