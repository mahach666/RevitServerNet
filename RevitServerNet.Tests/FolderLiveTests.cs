using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using RevitServerNet.Extensions;
using RevitServerNet.Models;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Folder REST operations against a live Revit Server (skipped unless <see cref="LiveServer"/> is configured).
    /// Writable tests work only in their own folder "|RSN_folders_live_*" that is removed in finally; the models of the server are only read.
    /// The server answers 403 while another client holds a lock on the parent: such calls are retried.
    /// </summary>
    [Collection(LiveRevitServerCollection.Name)]
    public class FolderLiveTests
    {
        private static RevitServerApi CreateApi() => new RevitServerApi(LiveServer.RestHost, "rsn-folders-live", false, LiveServer.Year);

        private static string[] ModelSegments => LiveServer.Model.Split('|', '\\').Where(s => s.Length > 0).ToArray();
        private static string ModelParent => "|" + string.Join("|", ModelSegments.Take(ModelSegments.Length - 1));
        private static string ModelPipePath => "|" + string.Join("|", ModelSegments);

        private static async Task<T> RetryWhenLockedAsync<T>(Func<Task<T>> action, Func<T, bool> isLocked)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await action();
                    if (attempt >= 20 || !isLocked(result)) return result;
                }
                catch (RevitServerApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden && attempt < 20)
                {
                }
                await Task.Delay(3000);
            }
        }

        private static Task<OperationResult> RetryAsync(Func<Task<OperationResult>> action) =>
            RetryWhenLockedAsync(action, r => !r.Success && r.Message != null && r.Message.Contains("Forbidden"));

        private static Task<OperationResult> CreateAsync(RevitServerApi api, string parent, string name) =>
            RetryWhenLockedAsync(() => api.CreateFolderAsync(parent, name), r => false);

        private static async Task RemoveAsync(RevitServerApi api, string path)
        {
            // Raw server delete (independent of the code under test); 404 means it is gone already
            await RetryWhenLockedAsync(async () =>
            {
                try { await api.DeleteAsync(RevitServerApi.EncodePath(path) + "?newObjectName="); }
                catch (RevitServerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
                return true;
            }, _ => false);
        }

        private static async Task WithRootAsync(Func<RevitServerApi, string, Task> test)
        {
            var api = CreateApi();
            var root = "|RSN_folders_live_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await CreateAsync(api, "|", root.Substring(1));
            try
            {
                await test(api, root);
            }
            finally
            {
                await RemoveAsync(api, root);
            }
        }

        private static async Task<List<string>> FolderNamesAsync(RevitServerApi api, string folder) =>
            (await api.ListFoldersAsync(folder)).Select(f => f.Name).ToList();

        // Every name goes through EncodePath: the server must get exactly this name (not cut at '#', not decoding '%41', '+' not a space)
        [LiveRevitServerFact]
        public Task ReservedCharactersInNames_AreCreatedListedRenamedAndDeletedAsGiven() => WithRootAsync(async (api, root) =>
        {
            // The server itself refuses the characters Windows forbids in file names (* : " ? with 400, < with 500)
            var names = new[] { "a#b", "x%41y", "50%", "p+q", "a&b", "s;t", "e=f", "(par)", "it's!", "sp ace", "Фф ё", "@[x]$,~" };
            foreach (var name in names)
            {
                var created = await CreateAsync(api, root, name);
                Assert.True(created.Success, name + ": " + created.Message);
                Assert.True(await api.FolderExistsAsync(root + "|" + name), name);
            }
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), (await FolderNamesAsync(api, root)).OrderBy(n => n, StringComparer.Ordinal));

            var renamed = await RetryAsync(() => api.RenameFolderAsync(root + "|a#b", "c%23d"));
            Assert.True(renamed.Success, renamed.Message);
            Assert.Contains("c%23d", await FolderNamesAsync(api, root));
            Assert.False(await api.FolderExistsAsync(root + "|a#b"));

            var deleted = await RetryAsync(() => api.DeleteFolderAsync(root + "|x%41y"));
            Assert.True(deleted.Success, deleted.Message);
            var left = await FolderNamesAsync(api, root);
            Assert.DoesNotContain("x%41y", left);
            Assert.Contains("50%", left); // nothing else was hit
        });

        [LiveRevitServerFact]
        public Task RenameFolderAsync_EmptyOrNullName_ThrowsAndTheFolderSurvives() => WithRootAsync(async (api, root) =>
        {
            await CreateAsync(api, root, "keep");
            await CreateAsync(api, root + "|keep", "inner");

            await Assert.ThrowsAsync<ArgumentException>(() => api.RenameFolderAsync(root + "|keep", ""));
            await Assert.ThrowsAsync<ArgumentException>(() => api.RenameFolderAsync(root + "|keep", null));
            await Assert.ThrowsAsync<ArgumentException>(() => api.RenameFolderAsync(root + "|keep", "  "));

            Assert.True(await api.FolderExistsAsync(root + "|keep"));
            Assert.True(await api.FolderExistsAsync(root + "|keep|inner"));
        });

        [LiveRevitServerFact]
        public Task RenameFolderAsync_RenamesWithEveryPathForm_CaseOnly_AndAfterTheCyrillic500() => WithRootAsync(async (api, root) =>
        {
            await CreateAsync(api, root, "r1");

            var plain = await RetryAsync(() => api.RenameFolderAsync(root + "|r1", "r2"));
            Assert.True(plain.Success, plain.Message);
            Assert.Equal(new[] { "r2" }, await FolderNamesAsync(api, root));

            // A path without the leading pipe and with '/' separators
            var slashes = await RetryAsync(() => api.RenameFolderAsync(root.TrimStart('|') + "/r2", "r3"));
            Assert.True(slashes.Success, slashes.Message);
            Assert.Equal(new[] { "r3" }, await FolderNamesAsync(api, root));

            var caseOnly = await RetryAsync(() => api.RenameFolderAsync(root + "|r3", "R3"));
            Assert.True(caseOnly.Success, caseOnly.Message);
            Assert.Equal(new[] { "R3" }, await FolderNamesAsync(api, root));

            // The server renames to this name but answers 500
            var quirk = await RetryAsync(() => api.RenameFolderAsync(root + "|R3", "Кира"));
            Assert.True(quirk.Success, quirk.Message);
            Assert.Equal(new[] { "Кира" }, await FolderNamesAsync(api, root));

            await CreateAsync(api, root, "other");
            var taken = await RetryAsync(() => api.RenameFolderAsync(root + "|Кира", "other"));
            Assert.False(taken.Success);
            Assert.Contains("Conflict", taken.Message);
            Assert.Equal(new[] { "other", "Кира" }, (await FolderNamesAsync(api, root)).OrderBy(n => n, StringComparer.Ordinal).ToArray());

            var missing = await RetryAsync(() => api.RenameFolderAsync(root + "|nope", "x"));
            Assert.False(missing.Success);
            Assert.Contains("NotFound", missing.Message);
        });

        [LiveRevitServerFact]
        public Task DeleteFolderAsync_DeletesTheFolderWithItsContents_AndReportsAMissingOne() => WithRootAsync(async (api, root) =>
        {
            await CreateAsync(api, root, "d1");
            await CreateAsync(api, root + "|d1", "inner");
            await CreateAsync(api, root, "keep");

            var result = await RetryAsync(() => api.DeleteFolderAsync(root + "|d1"));

            Assert.True(result.Success, result.Message);
            Assert.False(await api.FolderExistsAsync(root + "|d1"));
            Assert.False(await api.FolderExistsAsync(root + "|d1|inner"));
            Assert.True(await api.FolderExistsAsync(root + "|keep"));

            var again = await RetryAsync(() => api.DeleteFolderAsync(root + "|d1"));
            Assert.False(again.Success);
            Assert.Contains("NotFound", again.Message);
        });

        [LiveRevitServerFact]
        public async Task DeleteFolderAsync_Root_IsRefusedWithoutARequest()
        {
            var api = CreateApi();

            foreach (var path in new[] { "|", "", "/", "\\", "||", " " })
            {
                var result = await api.DeleteFolderAsync(path);
                Assert.False(result.Success);
            }

            Assert.True(await api.FolderExistsAsync(ModelParent));
        }

        [LiveRevitServerFact]
        public Task CreateFolderAsync_NamesThatMakeTheServerAnswer500_AreReportedAsCreated() => WithRootAsync(async (api, root) =>
        {
            var quirk = await CreateAsync(api, root, "Кир");
            Assert.True(quirk.Success, quirk.Message);
            Assert.True(await api.FolderExistsAsync(root + "|Кир"));

            var plain = await CreateAsync(api, root, "Фф");
            Assert.True(plain.Success, plain.Message);

            var again = await CreateAsync(api, root, "Фф"); // exists: the server answers 200
            Assert.True(again.Success, again.Message);
            Assert.Equal(new[] { "Кир", "Фф" }, (await FolderNamesAsync(api, root)).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        });

        [LiveRevitServerFact]
        public async Task ExistsChecks_DistinguishFoldersModelsAndMissingPaths()
        {
            var api = CreateApi();

            Assert.True(await api.FolderExistsAsync(ModelParent));
            Assert.False(await api.FolderExistsAsync(ModelPipePath)); // directoryinfo answers 200 for a model, IsFolder=false
            Assert.False(await api.FolderExistsAsync("|RSN_folders_live_missing"));
            Assert.True(await api.ModelExistsAsync(ModelPipePath));
            Assert.False(await api.ModelExistsAsync(ModelParent));
            Assert.False(await api.ModelExistsAsync("|RSN_folders_live_missing|m.rvt"));
        }

        [LiveRevitServerFact]
        public async Task WalkAsync_MissingTopFolder_Throws404_LikeTheOtherRecursiveHelpers()
        {
            var api = CreateApi();

            foreach (var ex in new[]
            {
                await Assert.ThrowsAsync<RevitServerApiException>(() => api.WalkAsync("|RSN_folders_live_missing")),
                await Assert.ThrowsAsync<RevitServerApiException>(() => api.GetAllModelsRecursiveAsync("|RSN_folders_live_missing")),
                await Assert.ThrowsAsync<RevitServerApiException>(() => api.GetAllFoldersRecursiveAsync("|RSN_folders_live_missing")),
            })
            {
                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            }
        }

        [LiveRevitServerFact]
        public async Task WalkAsync_DigModels_ListsEveryPathOnce_AndNormalizesTheStartPath()
        {
            var api = CreateApi();

            var plain = await api.WalkAsync(ModelParent);
            var dug = await api.WalkAsync(ModelParent + "|", true, true, true);

            Assert.Contains(ModelPipePath, plain.ModelPaths);
            Assert.Equal(1, dug.AllPaths.Count(p => string.Equals(p, ModelPipePath, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(ModelPipePath, dug.FolderPaths);
            Assert.Equal(dug.AllPaths.Count, dug.AllPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(dug.AllPaths, p => Assert.DoesNotContain("||", p));
            Assert.Contains(ModelPipePath + "|Model.rvt", dug.FilePaths); // internals of the model follow it
            Assert.True(dug.TotalCount > plain.TotalCount);
        }

        [LiveRevitServerFact]
        public async Task GetAllModelsRecursiveAsync_StartPathForms_GiveTheSameNormalizedPaths()
        {
            var api = CreateApi();

            var withPipes = await api.GetAllModelsRecursiveAsync(ModelParent + "|");
            var withoutPipe = await api.GetAllModelsRecursiveAsync(ModelParent.TrimStart('|'));

            Assert.Contains(ModelPipePath, withPipes.Select(m => m.Path));
            Assert.Equal(withPipes.Select(m => m.Path), withoutPipe.Select(m => m.Path));
            Assert.All(withPipes, m => Assert.DoesNotContain("||", m.Path));
        }

        [LiveRevitServerFact]
        public async Task GetProjectInfoAsync_ReturnsNamesAndLocalizedDataTypes()
        {
            var parameters = await CreateApi().GetProjectInfoAsync(ModelPipePath);

            Assert.NotEmpty(parameters);
            Assert.All(parameters, p => Assert.False(string.IsNullOrEmpty(p.Name), "Name of " + p.Value));
            Assert.Contains(parameters, p => p.DataType == ParamDataType.Text);
            Assert.Contains(parameters, p => p.Type == ParamType.System);
        }
    }
}
