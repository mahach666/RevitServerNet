using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitServerNet.Extensions;
using RevitServerNet.Models;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// DTO mapping, history and lock queries against a live Revit Server (skipped unless <see cref="LiveServer"/> is configured).
    /// Reads the configured model only; the lock test works on a copy in its own folder and removes it.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class LiveHistoryTests
    {
        private static RevitServerApi Api() => new RevitServerApi(LiveServer.RestHost, "RSN_dto_history", false, LiveServer.Year);

        private static string ModelPath => "|" + LiveServer.Model.Trim('|');

        private static string ModelFileName => LiveServer.Model.Split('|', '\\').Last();

        private static string ParentPath => ModelPath.Substring(0, ModelPath.LastIndexOf('|'));

        [LiveRevitServerFact]
        public async Task ServerInfo_HasRolesAndTheClientsYear()
        {
            var api = Api();

            var info = await api.GetServerInfoAsync();

            Assert.NotEmpty(info.Roles);
            Assert.Contains(ServerRole.Host, info.Roles);
            Assert.True(info.MaxPathLength > 0);
            Assert.False(string.IsNullOrEmpty(info.MachineName));
            Assert.Equal(LiveServer.Year, info.Version);
            Assert.Equal(LiveServer.Year, await api.GetServerVersionAsync());
            Assert.Equal(info.Roles, await api.GetServerRolesAsync());
            Assert.True(await api.IsServerRunningAsync());
        }

        [LiveRevitServerFact]
        public async Task History_VersionsSizesAndDatesAreMapped_AndFiltersWork()
        {
            var api = Api();

            var history = await api.GetModelHistoryAsync(ModelPath);
            var raw = JObject.Parse(await api.GetAsync(RevitServerApi.EncodePath(ModelPath) + "/history"));
            var wireVersions = raw["Items"].Select(i => (int)i["VersionNumber"]).ToList();

            Assert.NotEmpty(history.Items);
            Assert.Equal(wireVersions, history.Items.Select(i => i.Version));
            Assert.Equal(history.Items.Count, history.Items.Select(i => i.Version).Distinct().Count());
            Assert.All(history.Items, i => Assert.True(i.Version > 0 && i.Size > 0 && i.DateUtc.HasValue));
            Assert.Equal(raw["Items"].Select(i => (long)i["ModelSize"]), history.Items.Select(i => i.Size));

            var latest = await api.GetLatestVersionAsync(ModelPath);
            Assert.Equal(wireVersions.Max(), latest.Version);
            Assert.Equal(latest.Version, (await api.GetModelHistoryPageAsync(ModelPath, 0, 1)).Single().Version);
            var oldest = wireVersions.Min();
            Assert.Equal(oldest, (await api.GetVersionAsync(ModelPath, oldest)).Version);
            Assert.Null(await api.GetVersionAsync(ModelPath, wireVersions.Max() + 1000));
            Assert.True(await api.GetTotalModelSizeAsync(ModelPath) > history.Items.Sum(i => i.SupportSize));

            Assert.Equal(history.Items.Count, (await api.GetModelHistoryAsync(ModelPath, fromDate: new DateTime(2000, 1, 1))).Items.Count);
            Assert.Empty((await api.GetModelHistoryAsync(ModelPath, toDate: new DateTime(2000, 1, 1))).Items);
            var from = history.Items.Select(i => i.DateUtc.Value).Max();
            Assert.All((await api.GetModelHistoryAsync(ModelPath, fromDate: from)).Items, i => Assert.True(i.DateUtc.Value >= from));
            Assert.NotEmpty((await api.GetModelHistoryAsync(ModelPath, fromDate: from)).Items);
            Assert.Equal(new[] { latest.Version }, (await api.GetModelHistoryAsync(ModelPath, minVersion: latest.Version)).Items.Select(i => i.Version));
        }

        [LiveRevitServerFact]
        public async Task ModelAndFolderInfo_MapSizeNameGuidDatesAndCounts()
        {
            var api = Api();

            var info = await api.GetModelInfoAsync(ModelPath);
            var listed = (await api.ListModelsAsync(ParentPath)).Single(m => m.Name == ModelFileName);
            var folder = await api.GetFolderInfoAsync(ParentPath);

            Assert.Equal(ModelFileName, info.Name);
            Assert.True(info.Size > 0);
            Assert.True(info.SupportSize > 0);
            Assert.Equal(info.Size, listed.Size);
            Assert.Equal(info.SupportSize, listed.SupportSize);
            Assert.True(listed.ProductVersion > 0);
            Assert.NotEqual(Guid.Empty, Guid.Parse(info.ModelGUID));
            Assert.True(info.DateCreatedUtc.HasValue && info.DateModifiedUtc.HasValue);
            Assert.False(string.IsNullOrEmpty(info.LastModifiedBy));
            Assert.True(folder.Exists == true);
            Assert.True(folder.IsFolder);
            Assert.True(folder.ModelCount >= 1);
            Assert.True(folder.HasContents);
            Assert.True(folder.Size >= info.Size);
            Assert.True(folder.DateCreatedUtc.HasValue);
            Assert.Equal(ParentPath.Split('|').Last(), folder.Name);
        }

        [LiveRevitServerFact]
        public async Task LockQueries_SeeALockOnACopyInAnOwnFolder()
        {
            var api = Api();
            var folder = "|RSN_dto_history_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var model = folder + "|" + ModelFileName;
            var created = false;
            var locked = false;
            try
            {
                Assert.True((await api.CreateFolderAsync("|", folder.TrimStart('|'))).Success);
                created = true;
                // The server answers a copy with HTTP 500 although the copy is made, so the result is not checked, the copy is.
                await api.CopyItemAsync(ModelPath, model);
                Assert.True(await api.ModelExistsAsync(model));

                Assert.False(await api.IsModelLockedAsync(model));
                Assert.Null(await api.GetModelLockAsync(model));
                Assert.DoesNotContain((await api.GetLocksAsync()).Locks, l => IsPath(l.ModelPath, model));

                var lockResult = await api.LockItemAsync(model);
                locked = lockResult.Success;
                Assert.True(lockResult.Success, lockResult.Message);

                Assert.True(await api.IsModelLockedAsync(model));
                var info = await api.GetModelLockAsync(model);
                Assert.NotNull(info);
                Assert.Equal(LockState.Locked, info.LockState);
                var locks = await api.GetLocksAsync();
                Assert.Contains(locks.Locks, l => IsPath(l.ModelPath, model));
                Assert.True(await api.GetActiveLocksCountAsync() >= 1);
                Assert.Equal(LockState.Locked, (await api.ListModelsAsync(folder)).Single().LockState);
                Assert.Equal(LockState.LockedChild, (await api.GetFolderContentsAsync(folder)).LockState);
                await Assert.ThrowsAsync<NotSupportedException>(() => api.GetLocksByUserAsync("RSN_dto_history"));

                var unlock = await api.UnlockItemAsync(model);
                Assert.True(unlock.Success, unlock.Message);
                locked = false;

                Assert.False(await api.IsModelLockedAsync(model));
                Assert.DoesNotContain((await api.GetLocksAsync()).Locks, l => IsPath(l.ModelPath, model));
            }
            finally
            {
                if (locked)
                    await api.UnlockItemAsync(model);
                if (created)
                    // raw server delete, independent of the code under test: DELETE <path>?newObjectName= with an empty name
                    await api.DeleteAsync(RevitServerApi.EncodePath(folder) + "?newObjectName=");
            }
            Assert.False(await api.FolderExistsAsync(folder));
        }

        [LiveRevitServerFact]
        public async Task IsModelLocked_ThrowsForAModelThatDoesNotExist()
        {
            var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => Api().IsModelLockedAsync(ParentPath + "|RSN_dto_history_missing.rvt"));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        // The server reports "Folder\Model.rvt" without the leading separator.
        private static bool IsPath(string reported, string pipePath) =>
            string.Equals(reported?.Replace('\\', '|').Trim('|'), pipePath.Trim('|'), StringComparison.OrdinalIgnoreCase);
    }
}
