using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitServerNet.Extensions;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Lock/copy/move semantics against a live Revit Server (skipped unless <see cref="LiveServer"/> is configured).
    /// Every test works in its own |RSN_locking_core_* folder with a copy of the model and removes it afterwards;
    /// the original model is only read.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class LiveLockingTests
    {
        private static RevitServerApi CreateApi(string userName = "rsn-locking-tests") =>
            new RevitServerApi(LiveServer.RestHost, userName, false, LiveServer.Year);

        private static async Task<JObject> GetJsonAsync(RevitServerApi api, string path, string command) =>
            JObject.Parse(await api.GetAsync(RevitServerApi.EncodePath(path) + "/" + command));

        private static async Task<int> ModelLockStateAsync(RevitServerApi api, string folder, string modelName)
        {
            var contents = await GetJsonAsync(api, folder, "contents");
            foreach (var model in (JArray)contents["Models"])
                if ((string)model["Name"] == modelName)
                    return (int)model["LockState"];
            throw new InvalidOperationException("Model not found: " + modelName);
        }

        // Creates the folder, runs the test body and always removes the locks and the folder.
        private static async Task WithFolderAsync(RevitServerApi api, Func<string, Task> body)
        {
            var folder = "|RSN_locking_core_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await api.PutAsync(RevitServerApi.EncodePath(folder));
            try
            {
                await body(folder);
            }
            finally
            {
                await api.DeleteDescendentLocksAsync(folder);
                await api.DeleteAsync(RevitServerApi.EncodePath(folder) + "?newObjectName=");
            }
        }

        [LiveRevitServerFact]
        public async Task CopyLockUnlockCancelDescendentMove_ReportTheRealOutcome()
        {
            var api = CreateApi();
            await WithFolderAsync(api, async folder =>
            {
                var model = folder + "|m.rvt";

                var copy = await api.CopyItemAsync(LiveServer.Model, model);
                Assert.True(copy.Success, copy.Message);
                await api.GetAsync(RevitServerApi.EncodePath(model) + "/modelinfo"); // the copy exists

                var copyAgain = await api.CopyItemAsync(LiveServer.Model, model);
                Assert.False(copyAgain.Success);
                Assert.Contains("Conflict", copyAgain.Message);

                var lockResult = await api.LockItemAsync(model);
                Assert.True(lockResult.Success, lockResult.Message);
                Assert.Equal((int)Models.LockState.Locked, await ModelLockStateAsync(api, folder, "m.rvt"));

                var cancel = await api.CancelLockAsync(model);
                Assert.True(cancel.Success, cancel.Message);

                var removed = await api.DeleteDescendentLocksAsync(folder);
                Assert.True(removed.Success, removed.Message);
                Assert.Equal((int)Models.LockState.Unlocked, await ModelLockStateAsync(api, folder, "m.rvt"));
                Assert.Equal(JTokenType.Null, (await GetJsonAsync(api, folder, "descendent/locks"))["Items"].Type);

                Assert.True((await api.LockItemAsync(model)).Success);
                var unlock = await api.UnlockItemAsync(model);
                Assert.True(unlock.Success, unlock.Message);
                Assert.Equal((int)Models.LockState.Unlocked, await ModelLockStateAsync(api, folder, "m.rvt"));

                var moved = await api.MoveItemAsync(model, folder + "|m2.rvt");
                Assert.True(moved.Success, moved.Message);
                await api.GetAsync(RevitServerApi.EncodePath(folder + "|m2.rvt") + "/modelinfo");
                var old = await Assert.ThrowsAsync<RevitServerApiException>(
                    () => api.GetAsync(RevitServerApi.EncodePath(model) + "/modelinfo"));
                Assert.Equal(System.Net.HttpStatusCode.NotFound, old.StatusCode);

                var moveMissing = await api.MoveItemAsync(folder + "|nope.rvt", folder + "|x.rvt");
                Assert.False(moveMissing.Success);
                Assert.Contains("NotFound", moveMissing.Message);

                var lockMissing = await Assert.ThrowsAsync<RevitServerApiException>(() => api.LockItemAsync(folder + "|nope.rvt"));
                Assert.Equal(System.Net.HttpStatusCode.NotFound, lockMissing.StatusCode);
            });
        }

        [LiveRevitServerFact]
        public async Task LockItemAsync_NullOrEmptyPath_DoesNotLockTheServerRoot()
        {
            var api = CreateApi();
            var before = (int)(await GetJsonAsync(api, "|", "directoryinfo"))["LockState"];

            await Assert.ThrowsAsync<ArgumentException>(() => api.LockItemAsync(null));
            await Assert.ThrowsAsync<ArgumentException>(() => api.LockItemAsync(""));
            await Assert.ThrowsAsync<ArgumentException>(() => api.LockItemAsync("|"));

            Assert.Equal(before, (int)(await GetJsonAsync(api, "|", "directoryinfo"))["LockState"]);
        }

        [LiveRevitServerFact]
        public async Task CopyAndMove_ToANameTheServerAnswers500For_AreReportedAsDone()
        {
            var api = CreateApi();
            await WithFolderAsync(api, async folder =>
            {
                // RS 2022/2025 copy to "Ка.rvt" and then answer 500
                var copy = await api.CopyItemAsync(LiveServer.Model, folder + "|Ка.rvt");
                Assert.True(copy.Success, copy.Message);
                Assert.True(await api.ModelExistsAsync(folder + "|Ка.rvt"));

                // Over the existing model with overwrite the server answers 500 too: the outcome cannot be told, so it stays a failure
                var overwrite = await api.CopyItemAsync(LiveServer.Model, folder + "|Ка.rvt", overwrite: true);
                Assert.False(overwrite.Success);
                Assert.Contains("InternalServerError", overwrite.Message);

                var move = await api.MoveItemAsync(folder + "|Ка.rvt", folder + "|Кир.rvt");
                Assert.True(move.Success, move.Message);
                Assert.False(await api.ModelExistsAsync(folder + "|Ка.rvt"));
                Assert.True(await api.ModelExistsAsync(folder + "|Кир.rvt"));
            });
        }

        [LiveRevitServerFact]
        public async Task CyrillicUserName_CanCopyLockAndUnlock()
        {
            // Raw UTF-8 in User-Name is answered with 400 by the servers (2022 and 2025); the library sends it percent-encoded.
            var api = CreateApi("Иван Петров-Ю");
            await WithFolderAsync(api, async folder =>
            {
                var model = folder + "|м.rvt";

                Assert.True((await api.CopyItemAsync(LiveServer.Model, model)).Success);
                Assert.True((await api.LockItemAsync(model)).Success);
                Assert.Equal((int)Models.LockState.Locked, await ModelLockStateAsync(api, folder, "м.rvt"));
                Assert.True((await api.UnlockItemAsync(model)).Success);
                Assert.Equal((int)Models.LockState.Unlocked, await ModelLockStateAsync(api, folder, "м.rvt"));
            });
        }
    }
}
