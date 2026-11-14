using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// <see cref="RevitServerUiApi"/> against a live Revit Server (skipped unless configured, see <see cref="LiveServer"/>).
    /// Every test works only in its own probe folder "RSN_uiapi_{guid}" (a copy of the configured model is put there) and removes it.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class LiveUiApiTests
    {
        private static readonly string ServerName = new Uri("http://" + (LiveServer.RestHost ?? "localhost")).Host;

        private static RevitServerUiApi CreateApi() => new RevitServerUiApi(LiveServer.RestHost, false, LiveServer.Year);

        // "Test|YES_AR.rvt" -> "{server}\Test\YES_AR.rvt"
        private static string SourceModelId() => ServerName + "\\" + LiveServer.Model.Trim('|').Replace('|', '\\');

        private sealed class Probe
        {
            private readonly RevitServerUiApi _api;

            private Probe(RevitServerUiApi api, string folderId)
            {
                _api = api;
                FolderId = folderId;
            }

            public string FolderId { get; }

            public static async Task<Probe> CreateAsync(RevitServerUiApi api)
            {
                var id = ServerName + "\\RSN_uiapi_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                // Other tests of the server may hold a lock on a parent folder for a moment (403): retry.
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        await api.CreateFolderAsync(id);
                        return new Probe(api, id);
                    }
                    catch (RevitServerUiApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden && attempt < 30)
                    {
                        await Task.Delay(1000);
                    }
                }
            }

            public string Child(string name) => FolderId + "\\" + name;

            public async Task<string> CopySourceAsync(string name)
            {
                var id = Child(name);
                await _api.CopyOrMoveAsync(SourceModelId(), id, RevitServerUiApi.PasteAction.Copy);
                return id;
            }

            // Removes the probe folder with everything inside; releases the locks the test may have left.
            public async Task CleanupAsync()
            {
                var tree = await _api.GetSubItemsAsync(FolderId, 4);
                await DeleteTreeAsync(tree);
            }

            private async Task DeleteTreeAsync(Models.UiTreeItem item)
            {
                foreach (var child in item.Children ?? new System.Collections.Generic.List<Models.UiTreeItem>())
                    await DeleteTreeAsync(child);

                if (!string.Equals(item.LockStatus, "Unlocked", StringComparison.OrdinalIgnoreCase))
                    await TryUnlockAsync(item.Id);
                await _api.DeleteItemAsync(item.Id);
            }

            private async Task TryUnlockAsync(string id)
            {
                var data = await _api.GetItemLockDataAsync(id);
                if (string.Equals(data.LockStatus, "Locked", StringComparison.OrdinalIgnoreCase))
                    await _api.UnlockAsync(id);
            }
        }

        [LiveRevitServerFact]
        public async Task LockAndUnlock_Model_SecondCallsThrowInsteadOfSilentlySucceeding()
        {
            var api = CreateApi();
            var probe = await Probe.CreateAsync(api);
            try
            {
                var model = await probe.CopySourceAsync("lock.rvt");

                await api.LockAsync(model);
                Assert.Equal("Locked", (await api.GetItemLockDataAsync(model)).LockStatus);
                Assert.Equal("Locked", (await api.GetModelDetailsAsync(model)).LockStatus);

                // HTTP 200 + error descriptor on the real servers (2022 and 2025)
                var again = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.LockAsync(model));
                Assert.Contains("already locked", again.Message);
                Assert.Contains("Cannot Lock Model", again.ResponseContent);

                // a locked model can be neither deleted nor renamed (real 403)
                var denied = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.RenameAsync(model, "x.rvt"));
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                denied = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.DeleteItemAsync(model));
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

                await api.UnlockAsync(model);
                Assert.Equal("Unlocked", (await api.GetItemLockDataAsync(model)).LockStatus);

                var unlockedAgain = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.UnlockAsync(model));
                Assert.Contains("already unlocked", unlockedAgain.Message);
                await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.UnlockAsync(model, false));
            }
            finally
            {
                await probe.CleanupAsync();
            }
        }

        [LiveRevitServerFact]
        public async Task LockAndUnlock_Folder_And_MissingItems()
        {
            var api = CreateApi();
            var probe = await Probe.CreateAsync(api);
            try
            {
                await api.LockAsync(probe.FolderId);
                Assert.Equal("Locked", (await api.GetItemLockDataAsync(probe.FolderId)).LockStatus);
                var again = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.LockAsync(probe.FolderId));
                Assert.Contains("already locked", again.Message);

                await api.UnlockAsync(probe.FolderId);
                var unlockedAgain = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.UnlockAsync(probe.FolderId));
                Assert.Contains("already unlocked", unlockedAgain.Message);

                var missing = probe.Child("nope.rvt");
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.LockAsync(missing));
                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
                ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.UnlockAsync(missing));
                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
                // itemMustExist=false turns the 404 into HTTP 200 + descriptor
                await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.UnlockAsync(missing, false));
            }
            finally
            {
                await probe.CleanupAsync();
            }
        }

        [LiveRevitServerFact]
        public async Task BlankNewName_NeverDeletes_AndRenameDeleteWork()
        {
            var api = CreateApi();
            var probe = await Probe.CreateAsync(api);
            try
            {
                var model = await probe.CopySourceAsync("a.rvt");

                foreach (var blank in new[] { "", " ", "   " })
                    await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteOrRenameAsync(model, blank));
                await Assert.ThrowsAsync<ArgumentException>(() => api.RenameAsync(model, " "));

                // still there: nothing reached the server
                Assert.Equal("a.rvt", (await api.GetModelDetailsAsync(model)).Name);

                var renamed = probe.Child("b.rvt");
                await api.RenameAsync(model, "b.rvt");
                Assert.Equal("b.rvt", (await api.GetModelDetailsAsync(renamed)).Name);
                var gone = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.GetModelDetailsAsync(model));
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

                await api.DeleteOrRenameAsync(renamed, "c.rvt");
                var renamed2 = probe.Child("c.rvt");
                Assert.Equal("c.rvt", (await api.GetModelDetailsAsync(renamed2)).Name);

                await api.DeleteItemAsync(renamed2);
                gone = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.GetModelDetailsAsync(renamed2));
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            }
            finally
            {
                await probe.CleanupAsync();
            }
        }

        [LiveRevitServerFact]
        public async Task CopyMove_CreateFolder_Details_Histories_SubItems()
        {
            var api = CreateApi();
            var probe = await Probe.CreateAsync(api);
            try
            {
                // non-ASCII destination name
                var copyId = probe.Child("копия_АР.rvt");
                var url = await api.CopyOrMoveAsync(SourceModelId(), copyId, RevitServerUiApi.PasteAction.Copy);
                Assert.EndsWith("копия_АР.rvt", Uri.UnescapeDataString(url));

                var exists = await Assert.ThrowsAsync<RevitServerUiApiException>(
                    () => api.CopyOrMoveAsync(SourceModelId(), copyId, RevitServerUiApi.PasteAction.Copy, false));
                Assert.Equal(HttpStatusCode.Conflict, exists.StatusCode);
                await api.CopyOrMoveAsync(SourceModelId(), copyId, RevitServerUiApi.PasteAction.Copy, true);

                var details = await api.GetModelDetailsAsync(copyId);
                Assert.Equal("rs-model", details.Type);
                Assert.Equal("копия_АР.rvt", details.Name);
                Assert.Equal(copyId, details.Id);
                Assert.True(details.Size > 0);
                Assert.True(details.SupportedSize > 0);
                Assert.Equal("Unlocked", details.LockStatus);
                Assert.False(string.IsNullOrEmpty(details.ProductVersion));

                var histories = await api.GetModelHistoriesAsync(copyId);
                Assert.NotEmpty(histories);
                Assert.Equal(1, histories.First().VersionNumber);
                Assert.True(histories.All(h => h.ModelSize > 0));

                var subFolder = probe.Child("sub");
                var folderUrl = await api.CreateFolderAsync(subFolder);
                Assert.EndsWith("/sub", folderUrl);
                var conflict = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.CreateFolderAsync(subFolder));
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

                var moved = subFolder + "\\moved.rvt";
                await api.CopyOrMoveAsync(copyId, moved, RevitServerUiApi.PasteAction.Move);
                var gone = await Assert.ThrowsAsync<RevitServerUiApiException>(() => api.GetModelDetailsAsync(copyId));
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

                var tree = await api.GetSubItemsAsync(probe.FolderId, 2);
                Assert.Equal("rs-folder", tree.Type);
                var sub = Assert.Single(tree.Children);
                Assert.Equal("sub", sub.Name);
                Assert.Equal(moved, Assert.Single(sub.Children).Id);

                var shallow = await api.GetSubItemsAsync(probe.FolderId, 1);
                Assert.Null(Assert.Single(shallow.Children).Children);

                // rename of a folder
                await api.RenameAsync(subFolder, "sub2");
                Assert.Equal("sub2", (await api.GetSubItemsAsync(probe.Child("sub2"), 1)).Name);
            }
            finally
            {
                await probe.CleanupAsync();
            }
        }
    }
}
