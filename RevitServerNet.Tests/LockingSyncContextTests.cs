using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RevitServerNet.Extensions;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// A caller on a UI thread that blocks on the task (.Result / .Wait()) must not deadlock:
    /// the library must not resume its awaits on the caller's SynchronizationContext.
    /// </summary>
    public class LockingSyncContextTests
    {
        // Like a WinForms/WPF/Revit UI thread: one thread, continuations are queued and only run when the thread is free.
        private sealed class SingleThreadContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object State)> _queue =
                new BlockingCollection<(SendOrPostCallback, object)>();
            private readonly Thread _thread;

            public SingleThreadContext()
            {
                _thread = new Thread(() =>
                {
                    SetSynchronizationContext(this);
                    foreach (var item in _queue.GetConsumingEnumerable())
                        item.Callback(item.State);
                }) { IsBackground = true };
                _thread.Start();
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                try { _queue.Add((d, state)); }
                catch (InvalidOperationException) { } // a late continuation of a deadlocked call after the test ended
            }

            public void Dispose() => _queue.CompleteAdding();
        }

        public static TheoryData<string> Calls => new TheoryData<string>
        {
            "Get", "Lock", "Unlock", "CancelLock", "DeleteDescendentLocks", "Copy", "Move", "Copy500",
            "FolderContents", "FolderInfo", "CreateFolder", "DeleteFolder", "RenameFolder", "FolderExists", "ModelExists",
            "AllModelsRecursive", "Walk", "History", "LatestVersion", "HistoryPage", "Locks", "IsModelLocked",
            "ProjectInfo", "ServerInfo", "ServerVersion", "IsServerRunning",
            "Post", "Put", "Delete", "DescendentLocks", "RootContents", "FolderSize", "ModelInfo", "ListFolders", "ListModels", "ListFiles",
            "AllFoldersRecursive", "Version", "VersionsByUser", "VersionCount", "TotalModelSize", "LastComment", "LatestVersions",
            "ModelLock", "ModelLockUser", "ActiveLocksCount", "Ping", "ServerRoles", "MaxNameLength", "DriveInfo",
        };

        private static Task Call(RevitServerApi api, string name)
        {
            switch (name)
            {
                case "Get": return api.GetAsync("serverproperties");
                case "Lock": return api.LockItemAsync("|A|m.rvt");
                case "Unlock": return api.UnlockItemAsync("|A|m.rvt");
                case "CancelLock": return api.CancelLockAsync("|A|m.rvt");
                case "DeleteDescendentLocks": return api.DeleteDescendentLocksAsync("|A");
                case "Copy": return api.CopyItemAsync("|A|m.rvt", "|B|m.rvt");
                case "Move": return api.MoveItemAsync("|A|m.rvt", "|B|m.rvt");
                case "Copy500": return api.CopyItemAsync("|A|m.rvt", "|Fail|m.rvt"); // 500, then the outcome check
                case "FolderContents": return api.GetFolderContentsAsync("|A");
                case "FolderInfo": return api.GetFolderInfoAsync("|A");
                case "CreateFolder": return api.CreateFolderAsync("|A", "n");
                case "DeleteFolder": return api.DeleteFolderAsync("|A|n");
                case "RenameFolder": return api.RenameFolderAsync("|A|n", "o");
                case "FolderExists": return api.FolderExistsAsync("|A");
                case "ModelExists": return api.ModelExistsAsync("|A|m.rvt");
                case "AllModelsRecursive": return api.GetAllModelsRecursiveAsync("|A");
                case "Walk": return api.WalkAsync("|A");
                case "History": return api.GetModelHistoryAsync("|A|m.rvt");
                case "LatestVersion": return api.GetLatestVersionAsync("|A|m.rvt");
                case "HistoryPage": return api.GetModelHistoryPageAsync("|A|m.rvt", 0, 1);
                case "Locks": return api.GetLocksAsync();
                case "IsModelLocked": return api.IsModelLockedAsync("|A|m.rvt");
                case "ProjectInfo": return api.GetProjectInfoAsync("|A|m.rvt");
                case "ServerInfo": return api.GetServerInfoAsync();
                case "ServerVersion": return api.GetServerVersionAsync();
                case "IsServerRunning": return api.IsServerRunningAsync();
                case "Post": return api.PostAsync("|A|m.rvt/x");
                case "Put": return api.PutAsync("|A|m.rvt/x");
                case "Delete": return api.DeleteAsync("|A|m.rvt/x");
                case "DescendentLocks": return api.GetDescendentLocksAsync("|A");
                case "RootContents": return api.GetRootFolderContentsAsync();
                case "FolderSize": return api.GetFolderSizeAsync("|A");
                case "ModelInfo": return api.GetModelInfoAsync("|A|m.rvt");
                case "ListFolders": return api.ListFoldersAsync("|A");
                case "ListModels": return api.ListModelsAsync("|A");
                case "ListFiles": return api.ListFilesAsync("|A");
                case "AllFoldersRecursive": return api.GetAllFoldersRecursiveAsync("|A");
                case "Version": return api.GetVersionAsync("|A|m.rvt", 1);
                case "VersionsByUser": return api.GetVersionsByUserAsync("|A|m.rvt", "Mahach");
                case "VersionCount": return api.GetVersionCountAsync("|A|m.rvt");
                case "TotalModelSize": return api.GetTotalModelSizeAsync("|A|m.rvt");
                case "LastComment": return api.GetLastVersionCommentAsync("|A|m.rvt");
                case "LatestVersions": return api.GetLatestVersionsAsync("|A|m.rvt", 2);
                case "ModelLock": return api.GetModelLockAsync("|A|m.rvt");
                case "ModelLockUser": return api.GetModelLockUserAsync("|A|m.rvt");
                case "ActiveLocksCount": return api.GetActiveLocksCountAsync();
                case "Ping": return api.PingServerAsync();
                case "ServerRoles": return api.GetServerRolesAsync();
                case "MaxNameLength": return api.GetMaximumModelNameLengthAsync();
                case "DriveInfo": return api.GetServerDriveInfoAsync();
                default: throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        // Real RS 2022 response shapes, so that methods with several awaits run to the end instead of failing at the first one
        private static StubResponse Respond(string line)
        {
            if (line.StartsWith("POST ") && line.Contains("%7CFail%7C")) return new StubResponse(500, "Internal Server Error", string.Empty);
            if (!line.StartsWith("GET ")) return new StubResponse(200, "OK", string.Empty);
            if (line.Contains("/contents"))
                return StubResponse.Ok("{\"Path\":\"A\",\"DriveFreeSpace\":1,\"DriveSpace\":2,\"Files\":[],\"Folders\":[],\"LockContext\":null,\"LockState\":0,\"ModelLocksInProgress\":null,\"Models\":[{\"LockContext\":null,\"LockState\":0,\"ModelLocksInProgress\":null,\"ModelSize\":16648721,\"Name\":\"m.rvt\",\"ProductVersion\":12,\"SupportSize\":1750275}]}");
            if (line.Contains("/directoryinfo") || line.Contains("/modelinfo"))
                return StubResponse.Ok("{\"Path\":\"A\\\\m.rvt\",\"DateCreated\":\"\\/Date(1794611306058)\\/\",\"DateModified\":\"\\/Date(1780844410000)\\/\",\"Exists\":true,\"FolderCount\":0,\"IsFolder\":true,\"LastModifiedBy\":\"Mahach\",\"LockContext\":null,\"LockState\":0,\"ModelCount\":1,\"ModelLocksInProgress\":null,\"ModelSize\":16747025,\"Size\":16793109}");
            if (line.Contains("/history"))
                return StubResponse.Ok("{\"Path\":\"A\\\\m.rvt\",\"Items\":[{\"Comment\":\"\",\"Date\":\"\\/Date(1780826100000)\\/\",\"ModelSize\":13998225,\"OverwrittenByHistoryNumber\":0,\"SupportSize\":61127,\"User\":\"Mahach\",\"VersionNumber\":1}]}");
            if (line.Contains("/descendent/locks"))
                return StubResponse.Ok("{\"Path\":\"\",\"DescendentHasLockContext\":false,\"Items\":null}");
            if (line.Contains("/projectinfo"))
                return StubResponse.Ok("[]");
            if (line.Contains("serverproperties"))
                return StubResponse.Ok("{\"AccessLevelTypes\":[],\"MachineName\":\"RS\",\"MaximumFolderPathLength\":98,\"MaximumModelNameLength\":40,\"ServerRoles\":[0,1,2],\"Servers\":[\"localhost\"]}");
            return new StubResponse(404, "Not Found", string.Empty);
        }

        [Theory]
        [MemberData(nameof(Calls))]
        public void BlockingOnTheResult_OnASingleThreadContext_DoesNotDeadlock(string name)
        {
            using (var server = new StubHttpServer(Respond))
            using (var context = new SingleThreadContext())
            {
                var api = new RevitServerApi(server.Host, "tester", false, "2022");
                var completed = false;
                Exception error = null;
                var done = new ManualResetEventSlim();

                context.Post(_ =>
                {
                    var task = Call(api, name);
                    // Blocks the context thread like .Result on a UI thread; a deadlock shows as the timeout
                    try { completed = task.Wait(TimeSpan.FromSeconds(10)); }
                    catch (AggregateException ex) { completed = true; error = ex.InnerException; } // failed, but did not deadlock
                    done.Set();
                }, null);

                Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "the context thread never came back");
                Assert.True(completed, $"{name}: the task did not complete in 10 s while the caller blocked its SynchronizationContext");
                Assert.Null(error); // the stub answers like the server, so every call must run to the end
                Assert.Empty(server.Errors);
            }
        }
    }
}
