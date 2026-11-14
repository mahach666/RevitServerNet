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
    /// Wire tests of FolderExtensions and ProjectExtensions. Status codes, reason phrases and JSON shapes are the ones captured
    /// from live Revit Server 2022 and 2025 (identical on both): rename answers 201 with an empty body, delete 200 with an empty body,
    /// 404/403/409/400 come without a body, and some Cyrillic names make create/rename answer 500 after the server did the work.
    /// '|' is always sent as %7C.
    /// </summary>
    public class FolderWireTests
    {
        private const string Service = "/RevitServerAdminRESTService2022/AdminRESTService.svc/";
        private const string InternalError = "There is an internal server error, please check the service log for details.";

        private static RevitServerApi CreateApi(StubHttpServer server) => new RevitServerApi(server.Host, "tester", false, "2022");

        private static StubResponse Empty(int code, string reason) => new StubResponse(code, reason, string.Empty);
        private static StubResponse Created => Empty(201, "Created");
        private static StubResponse OkEmpty => Empty(200, "OK");
        private static StubResponse NotFound => Empty(404, "Not Found");
        private static StubResponse ServerError => Empty(500, InternalError);

        // Answers the routes by "METHOD target" (relative to the service path), anything else 404 like the real server
        private static StubHttpServer Stub(Dictionary<string, StubResponse> routes)
        {
            return new StubHttpServer(line =>
            {
                var key = line.Replace(" " + Service, " ");
                return routes.TryGetValue(key, out var response) ? response : new StubResponse(404, "Not Found", "Unexpected request: " + line);
            });
        }

        private static List<string> Requests(StubHttpServer server) => server.Requests.Select(r => r.Replace(" " + Service, " ")).ToList();

        private static string J(string json) => json.Replace('\'', '"');

        private const string FolderInfoJson = @"{'Path':'RSN_folders_p','DateCreated':'\/Date(1794607703058)\/','DateModified':'\/Date(1794607705547)\/','Exists':true,'FolderCount':1,'IsFolder':true,'LastModifiedBy':null,'LockContext':null,'LockState':0,'ModelCount':0,'ModelLocksInProgress':[],'ModelSize':0,'Size':0}";
        private const string ModelDirectoryInfoJson = @"{'Path':'Test\\YES_АР.rvt','DateCreated':'\/Date(1780826093277)\/','DateModified':'\/Date(1780844410000)\/','Exists':true,'FolderCount':0,'IsFolder':false,'LastModifiedBy':'Mahach','LockContext':null,'LockState':0,'ModelCount':1,'ModelLocksInProgress':null,'ModelSize':16648721,'Size':18398996}";
        private const string MissingDirectoryInfoJson = @"{'Path':'RSN_folders_p\\nope','DateCreated':null,'DateModified':null,'Exists':false,'FolderCount':0,'IsFolder':false,'LastModifiedBy':null,'LockContext':null,'LockState':0,'ModelCount':0,'ModelLocksInProgress':null,'ModelSize':0,'Size':0}";
        private const string ModelInfoJson = @"{'Path':'Test\\YES_АР.rvt','DateCreated':'\/Date(1780826093277)\/','DateModified':'\/Date(1780844410000)\/','LastModifiedBy':'Mahach','ModelGUID':'4c6bb8be-13c3-4c52-a4e9-32a4df44ec46','ModelSize':16648721,'SupportSize':1750275}";

        // Folder contents in the shape of the real /contents answer
        private static StubResponse Contents(string[] folders = null, string[] models = null, string[] files = null)
        {
            var f = string.Join(",", (folders ?? new string[0]).Select(n => "{'HasContents':false,'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'Name':'" + n + "','Size':0}"));
            var m = string.Join(",", (models ?? new string[0]).Select(n => "{'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'ModelSize':16648721,'Name':'" + n + "','ProductVersion':12,'SupportSize':1750275}"));
            var x = string.Join(",", (files ?? new string[0]).Select(n => "{'IsText':true,'Name':'" + n + "','Size':347}"));
            return StubResponse.Ok(J("{'Path':'','DriveFreeSpace':135822270464,'DriveSpace':136229920768,'Files':[" + x + "],'Folders':[" + f + "],'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'Models':[" + m + "]}"));
        }

        // ---- RenameFolderAsync ----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("a|b")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public async Task RenameFolderAsync_InvalidNewName_ThrowsAndSendsNothing(string newName)
        {
            // Old code sent DELETE <path>?newObjectName= which the server executes as a recursive DELETE of the folder
            using (var server = Stub(new Dictionary<string, StubResponse>()))
            {
                await Assert.ThrowsAsync<ArgumentException>(() => CreateApi(server).RenameFolderAsync("|Проекты|Old", newName));

                Assert.Empty(server.Requests);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("|")]
        [InlineData("||")]
        [InlineData("/")]
        [InlineData("\\")]
        public async Task RenameAndDeleteFolderAsync_RootPaths_SendNothing(string path)
        {
            using (var server = Stub(new Dictionary<string, StubResponse>()))
            {
                var api = CreateApi(server);

                var rename = await api.RenameFolderAsync(path, "New");
                var delete = await api.DeleteFolderAsync(path);

                Assert.False(rename.Success);
                Assert.False(delete.Success);
                Assert.Empty(server.Requests);
            }
        }

        [Fact]
        public async Task RenameFolderAsync_Created201WithEmptyBody_IsSuccessWithASingleRequest()
        {
            // Real rename answer: 201 Created, Content-Length 0. Old code then checked directoryinfo of both paths (404 here) and reported failure.
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CP%7Cold?newObjectName=new"] = Created,
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("|P|old", "new");

                Assert.True(result.Success, result.Message);
                Assert.Equal(new[] { "DELETE %7CP%7Cold?newObjectName=new" }, Requests(server));
            }
        }

        [Fact]
        public async Task RenameFolderAsync_PathWithoutLeadingPipe_IsRenamed()
        {
            // Old code: "Top/Sub".Substring(0, LastIndexOf('|') == -1) threw and the rename was reported as failed
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CTop%7CSub?newObjectName=New"] = Created,
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("Top/Sub", "New");

                Assert.True(result.Success, result.Message);
            }
        }

        [Fact]
        public async Task RenameFolderAsync_CaseOnlyRename_IsSuccess()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CP%7Cdel2?newObjectName=DEL2"] = Created,
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("|P|del2", "DEL2");

                Assert.True(result.Success, result.Message);
            }
        }

        [Fact]
        public async Task RenameFolderAsync_500AfterTheRename_IsSuccessWhenTheParentListsTheNewName()
        {
            // Real quirk: renaming to "Кира" answers 500 although the folder was renamed
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CP%7Cq1?newObjectName=%D0%9A%D0%B8%D1%80%D0%B0"] = ServerError,
                ["GET %7CP/contents"] = Contents(folders: new[] { "Кира" }),
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("|P|q1", "Кира");

                Assert.True(result.Success, result.Message);
                Assert.Contains("500", result.Message);
            }
        }

        [Fact]
        public async Task RenameFolderAsync_500WithoutTheRename_IsAFailureWithTheStatus()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CP%7Cq1?newObjectName=%D0%9A%D0%B8%D1%80%D0%B0"] = ServerError,
                ["GET %7CP/contents"] = Contents(folders: new[] { "q1" }),
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("|P|q1", "Кира");

                Assert.False(result.Success);
                Assert.Contains("InternalServerError", result.Message);
            }
        }

        [Fact]
        public async Task RenameFolderAsync_TopLevelFolder_VerifiesInTheRoot()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7Cq1?newObjectName=%D0%9A%D0%B8%D1%80%D0%B0"] = ServerError,
                ["GET %7C/contents"] = Contents(folders: new[] { "Кира", "Test" }),
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("q1", "Кира");

                Assert.True(result.Success, result.Message);
            }
        }

        [Theory]
        [InlineData(409, "Conflict", "Conflict")]
        [InlineData(403, "Forbidden", "Forbidden")]
        [InlineData(404, "Not Found", "NotFound")]
        [InlineData(400, "Bad Request", "BadRequest")]
        public async Task RenameFolderAsync_ClientErrors_AreReportedWithTheStatus(int code, string reason, string expected)
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CP%7Cq1?newObjectName=b"] = Empty(code, reason),
            }))
            {
                var result = await CreateApi(server).RenameFolderAsync("|P|q1", "b");

                Assert.False(result.Success);
                Assert.Contains(expected, result.Message);
                Assert.Single(server.Requests);
            }
        }

        // ---- DeleteFolderAsync ----

        [Fact]
        public async Task DeleteFolderAsync_SendsDeleteWithAnEmptyNewObjectName()
        {
            // The real server answers a bare DELETE with 405 'Parameter "NEWOBJECTNAME" in the query string is missing'
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7C%23B?newObjectName="] = OkEmpty,
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("|A|#B");

                Assert.True(result.Success, result.Message);
                Assert.Equal(new[] { "DELETE %7CA%7C%23B?newObjectName=" }, Requests(server));
            }
        }

        [Fact]
        public async Task DeleteFolderAsync_NormalizesThePath()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7CB?newObjectName="] = OkEmpty,
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("A\\B|");

                Assert.True(result.Success, result.Message);
            }
        }

        [Fact]
        public async Task DeleteFolderAsync_NotFound_IsAFailure()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7Cnope?newObjectName="] = NotFound,
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("|A|nope");

                Assert.False(result.Success);
                Assert.Contains("NotFound", result.Message);
            }
        }

        [Fact]
        public async Task DeleteFolderAsync_Locked403_IsAFailureNotAPermissionsGuess()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7Cb?newObjectName="] = Empty(403, "Forbidden"),
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("|A|b");

                Assert.False(result.Success);
                Assert.Contains("Forbidden", result.Message);
                Assert.DoesNotContain("special permissions", result.Message);
            }
        }

        [Fact]
        public async Task DeleteFolderAsync_500ButTheFolderIsGone_IsSuccess()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7Cb?newObjectName="] = ServerError,
                ["GET %7CA%7Cb/directoryinfo"] = new StubResponse(404, "Not Found", J(MissingDirectoryInfoJson)),
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("|A|b");

                Assert.True(result.Success, result.Message);
            }
        }

        [Fact]
        public async Task DeleteFolderAsync_500AndTheFolderRemains_IsAFailure()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["DELETE %7CA%7Cb?newObjectName="] = ServerError,
                ["GET %7CA%7Cb/directoryinfo"] = StubResponse.Ok(J(FolderInfoJson)),
            }))
            {
                var result = await CreateApi(server).DeleteFolderAsync("|A|b");

                Assert.False(result.Success);
                Assert.Contains("InternalServerError", result.Message);
            }
        }

        // ---- CreateFolderAsync ----

        [Theory]
        [InlineData(201, "Created")]
        [InlineData(200, "OK")] // the folder exists already: the real server answers 200 with an empty body
        public async Task CreateFolderAsync_EmptyBody_IsSuccessWithASingleRequest(int code, string reason)
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["PUT %7CP%7C%D0%A4%D1%84"] = Empty(code, reason),
            }))
            {
                var result = await CreateApi(server).CreateFolderAsync("|P", "Фф");

                Assert.True(result.Success, result.Message);
                Assert.Single(server.Requests);
            }
        }

        [Fact]
        public async Task CreateFolderAsync_500AfterTheFolderWasCreated_IsSuccess()
        {
            // Real quirk: PUT |P|Кир answers 500 and the folder exists afterwards (RS 2022 and 2025)
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["PUT %7CP%7C%D0%9A%D0%B8%D1%80"] = ServerError,
                ["GET %7CP%7C%D0%9A%D0%B8%D1%80/directoryinfo"] = StubResponse.Ok(J(FolderInfoJson)),
            }))
            {
                var result = await CreateApi(server).CreateFolderAsync("|P", "Кир");

                Assert.True(result.Success, result.Message);
            }
        }

        [Fact]
        public async Task CreateFolderAsync_500AndNoFolder_ThrowsTheServerError()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["PUT %7CP%7Cx"] = ServerError,
                ["GET %7CP%7Cx/directoryinfo"] = new StubResponse(404, "Not Found", J(MissingDirectoryInfoJson)),
            }))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).CreateFolderAsync("|P", "x"));

                Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
        }

        [Fact]
        public async Task CreateFolderAsync_Forbidden_IsThrownWithoutVerification()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["PUT %7CP%7Cx"] = Empty(403, "Forbidden"),
            }))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).CreateFolderAsync("|P", "x"));

                Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
                Assert.Single(server.Requests);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("a|b")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public async Task CreateFolderAsync_EmptyName_ThrowsAndSendsNothing(string name)
        {
            using (var server = Stub(new Dictionary<string, StubResponse>()))
            {
                await Assert.ThrowsAsync<ArgumentException>(() => CreateApi(server).CreateFolderAsync("|P", name));

                Assert.Empty(server.Requests);
            }
        }

        // ---- Exists checks ----

        [Fact]
        public async Task FolderExistsAsync_Folder_True_Missing404_False_ModelPath_False()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7CP/directoryinfo"] = StubResponse.Ok(J(FolderInfoJson)),
                ["GET %7CP%7Cnope/directoryinfo"] = new StubResponse(404, "Not Found", J(MissingDirectoryInfoJson)),
                ["GET %7CTest%7CYES_%D0%90%D0%A0.rvt/directoryinfo"] = StubResponse.Ok(J(ModelDirectoryInfoJson)),
            }))
            {
                var api = CreateApi(server);

                Assert.True(await api.FolderExistsAsync("|P"));
                Assert.False(await api.FolderExistsAsync("|P|nope"));
                Assert.False(await api.FolderExistsAsync("|Test|YES_АР.rvt")); // directoryinfo answers 200 with IsFolder=false
            }
        }

        [Theory]
        [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
        [InlineData(403, "Forbidden", HttpStatusCode.Forbidden)]
        [InlineData(401, "Unauthorized", HttpStatusCode.Unauthorized)]
        public async Task ExistsChecks_OtherFailures_AreThrownNotReportedAsMissing(int code, string reason, HttpStatusCode expected)
        {
            using (var server = new StubHttpServer(_ => Empty(code, reason)))
            {
                var api = CreateApi(server);

                var folder = await Assert.ThrowsAsync<RevitServerApiException>(() => api.FolderExistsAsync("|P"));
                var model = await Assert.ThrowsAsync<RevitServerApiException>(() => api.ModelExistsAsync("|P|m.rvt"));

                Assert.Equal(expected, folder.StatusCode);
                Assert.Equal(expected, model.StatusCode);
            }
        }

        [Fact]
        public async Task ModelExistsAsync_Model_True_FolderPath404_False()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7CTest%7CYES_%D0%90%D0%A0.rvt/modelinfo"] = StubResponse.Ok(J(ModelInfoJson)),
                ["GET %7CTest/modelinfo"] = new StubResponse(404, "Not Found", J("{'Path':'Test','ModelGUID':'00000000-0000-0000-0000-000000000000','ModelSize':0,'SupportSize':0}")),
            }))
            {
                var api = CreateApi(server);

                Assert.True(await api.ModelExistsAsync("|Test|YES_АР.rvt"));
                Assert.False(await api.ModelExistsAsync("|Test"));
            }
        }

        // ---- Walk and recursive helpers ----

        private static Dictionary<string, StubResponse> Tree() => new Dictionary<string, StubResponse>
        {
            ["GET %7C/contents"] = Contents(folders: new[] { "#A", "B" }, files: new[] { "super.lock" }),
            ["GET %7C%23A/contents"] = Contents(models: new[] { "m.rvt" }, files: new[] { "a.txt" }),
            ["GET %7CB/contents"] = Contents(),
            ["GET %7C%23A%7Cm.rvt/contents"] = Contents(folders: new[] { "Data" }, files: new[] { "Model.rvt" }),
            ["GET %7C%23A%7Cm.rvt%7CData/contents"] = Contents(files: new[] { "0_1.rws" }),
        };

        [Fact]
        public async Task WalkAsync_WalksTheTree_WithEscapedNames()
        {
            using (var server = Stub(Tree()))
            {
                var result = await CreateApi(server).WalkAsync("|");

                Assert.Equal(new[] { "|", "|super.lock", "|#A", "|#A|a.txt", "|#A|m.rvt", "|B" }, result.AllPaths);
                Assert.Equal(new[] { "|", "|#A", "|B" }, result.FolderPaths);
                Assert.Equal(new[] { "|super.lock", "|#A|a.txt" }, result.FilePaths);
                Assert.Equal(new[] { "|#A|m.rvt" }, result.ModelPaths);
            }
        }

        [Fact]
        public async Task WalkAsync_FlagsOff_ListsOnlyFolders()
        {
            using (var server = Stub(Tree()))
            {
                var result = await CreateApi(server).WalkAsync("|", false, false);

                Assert.Equal(new[] { "|", "|#A", "|B" }, result.AllPaths);
                Assert.Empty(result.FilePaths);
                Assert.Empty(result.ModelPaths);
            }
        }

        [Fact]
        public async Task WalkAsync_DigModels_ListsTheModelOnce_AndItsInternals()
        {
            // Old code added the model path a second time (as a folder) before its internals
            using (var server = Stub(Tree()))
            {
                var result = await CreateApi(server).WalkAsync("|", true, true, true);

                Assert.Equal(1, result.AllPaths.Count(p => p == "|#A|m.rvt"));
                Assert.Single(result.ModelPaths);
                Assert.DoesNotContain("|#A|m.rvt", result.FolderPaths);
                Assert.Equal(result.AllPaths.Count, result.AllPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.Contains("|#A|m.rvt|Model.rvt", result.FilePaths);
                Assert.Contains("|#A|m.rvt|Data|0_1.rws", result.FilePaths);
            }
        }

        [Fact]
        public async Task WalkAsync_SubfolderFailure_IsThrown_NotADroppedSubtree()
        {
            // Old code: catch { } dropped the failed subtree and returned a partial result
            var routes = Tree();
            routes["GET %7C%23A/contents"] = ServerError;
            using (var server = Stub(routes))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).WalkAsync("|"));

                Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
        }

        [Fact]
        public async Task WalkAsync_MissingTopFolder_Throws404()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>()))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).WalkAsync("|NoSuchFolderXYZ"));

                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            }
        }

        [Fact]
        public async Task WalkAsync_EmptyBody_Throws()
        {
            using (var server = new StubHttpServer(_ => OkEmpty))
            {
                await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).WalkAsync("|"));
            }
        }

        [Theory]
        [InlineData("|Test|")]
        [InlineData("Test")]
        [InlineData("Test/")]
        [InlineData("\\Test\\")]
        [InlineData("||Test")]
        public async Task RecursiveHelpers_NormalizeTheStartPath(string start)
        {
            var routes = new Dictionary<string, StubResponse>
            {
                ["GET %7CTest/contents"] = Contents(folders: new[] { "Sub" }, models: new[] { "YES_АР.rvt" }),
                ["GET %7CTest%7CSub/contents"] = Contents(models: new[] { "Yes_ОВ.rvt" }),
            };
            using (var server = Stub(routes))
            {
                var api = CreateApi(server);

                var models = await api.GetAllModelsRecursiveAsync(start);
                var folders = await api.GetAllFoldersRecursiveAsync(start);
                var walk = await api.WalkAsync(start);
                var listed = await api.ListModelsAsync(start);

                Assert.Equal(new[] { "|Test|YES_АР.rvt", "|Test|Sub|Yes_ОВ.rvt" }, models.Select(m => m.Path));
                Assert.Equal(new[] { "|Test|Sub" }, folders.Select(f => f.Path));
                Assert.Equal(new[] { "|Test", "|Test|YES_АР.rvt", "|Test|Sub", "|Test|Sub|Yes_ОВ.rvt" }, walk.AllPaths);
                Assert.Equal(new[] { "|Test|YES_АР.rvt" }, listed.Select(m => m.Path));
            }
        }

        [Fact]
        public async Task GetAllModelsRecursiveAsync_EmptyStartPath_IsTheRoot()
        {
            // Old code returned an empty list for "" although EncodePath("") and ListModelsAsync("") mean the root
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7C/contents"] = Contents(models: new[] { "m.rvt" }),
            }))
            {
                var models = await CreateApi(server).GetAllModelsRecursiveAsync("");

                Assert.Equal(new[] { "|m.rvt" }, models.Select(m => m.Path));
            }
        }

        [Fact]
        public async Task GetAllFoldersRecursiveAsync_FailureAndEmptyBody_AreThrown()
        {
            using (var failing = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7C/contents"] = Contents(folders: new[] { "A" }),
                ["GET %7CA/contents"] = ServerError,
            }))
            using (var empty = new StubHttpServer(_ => OkEmpty))
            {
                await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(failing).GetAllFoldersRecursiveAsync());
                await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(empty).GetAllFoldersRecursiveAsync());
            }
        }

        [Fact]
        public async Task ListFoldersAndListFiles_BuildPathsAndReturnEntries()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7CP/contents"] = Contents(folders: new[] { "#x", "y" }, files: new[] { "f.txt" }),
            }))
            {
                var api = CreateApi(server);

                var folders = await api.ListFoldersAsync("P/");
                var files = await api.ListFilesAsync("|P");

                Assert.Equal(new[] { "|P|#x", "|P|y" }, folders.Select(f => f.Path));
                Assert.Equal(new[] { "f.txt" }, files.Select(f => f.Name));
            }
        }

        // ---- GetProjectInfoAsync (real projectinfo of Test|YES_АР.rvt, identical on RS 2022 and 2025) ----

        private const string ProjectInfoJson = "﻿" + @"[{'A:title':'Идентификация','Рабочий_набор':{'#text':'Сведения о проекте','@displayName':'Рабочий набор','@type':'system'}},{'A:title':'Анализ трассировки'},{'_Раздел_проекта':{'#text':'АР','@displayName':'…Раздел проекта','@id':'7f0c8590-53a2-4ddf-aaed-c70eb9b687bc','@type':'shared','@typeOfParameter':'Текст'},'A:title':'Прочее','Адрес_проекта':{'#text':'Укажите адрес','@displayName':'Адрес проекта','@type':'system','@typeOfParameter':'Многострочный текст'},'Дата_утверждения_проекта':{'#text':'Дата выпуска','@displayName':'Дата утверждения проекта','@type':'system','@typeOfParameter':'Текст'},'Заказчик':{'#text':'Владелец','@type':'system','@typeOfParameter':'Текст'},'Наименование_проекта':{'#text':'Наименование проекта','@displayName':'Наименование проекта','@type':'system','@typeOfParameter':'Текст'},'Номер_проекта':{'#text':'Номер проекта','@displayName':'Номер проекта','@type':'system','@typeOfParameter':'Текст'},'Статус_проекта':{'#text':'Статус проекта','@displayName':'Статус проекта','@type':'system','@typeOfParameter':'Текст'}}]";

        [Fact]
        public async Task GetProjectInfoAsync_RealLocalizedPayload_MapsNamesTypesAndCategories()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>
            {
                ["GET %7CTest%7CYES_%D0%90%D0%A0.rvt/projectinfo"] = StubResponse.Ok(J(ProjectInfoJson)),
            }))
            {
                var parameters = await CreateApi(server).GetProjectInfoAsync("|Test|YES_АР.rvt");

                Assert.Equal(8, parameters.Count);
                Assert.All(parameters, p => Assert.False(string.IsNullOrEmpty(p.Name)));

                var workset = parameters[0];
                Assert.Equal("Идентификация", workset.Category);
                Assert.Equal("Рабочий набор", workset.Name);
                Assert.Equal("Сведения о проекте", workset.Value);
                Assert.Equal(ParamType.System, workset.Type);
                Assert.Equal(ParamDataType.Unknown, workset.DataType); // the server sends no @typeOfParameter for it
                Assert.Null(workset.Id);

                var shared = parameters.Single(p => p.Name == "…Раздел проекта");
                Assert.Equal("Прочее", shared.Category);
                Assert.Equal("АР", shared.Value);
                Assert.Equal("7f0c8590-53a2-4ddf-aaed-c70eb9b687bc", shared.Id);
                Assert.Equal(ParamType.Shared, shared.Type);
                Assert.Equal(ParamDataType.Text, shared.DataType);
                Assert.Equal("Текст", shared.TypeOfParameter);
                Assert.Null(parameters.Single(p => p.Name == "Рабочий набор").TypeOfParameter);

                // No @displayName: the JSON property name is the name
                var customer = parameters.Single(p => p.Value == "Владелец");
                Assert.Equal("Заказчик", customer.Name);
                Assert.Equal(ParamDataType.Text, customer.DataType);

                Assert.Equal(ParamDataType.MultilineText, parameters.Single(p => p.Name == "Адрес проекта").DataType);
                Assert.Equal(ParamDataType.Text, parameters.Single(p => p.Name == "Номер проекта").DataType);
            }
        }

        [Fact]
        public async Task GetProjectInfoAsync_NameWithoutDisplayName_UsesThePropertyNameWithSpaces()
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok(J("[{'A:title':'T','Номер_проекта':{'#text':'1','@type':'custom','@typeOfParameter':'Number'}}]"))))
            {
                var parameters = await CreateApi(server).GetProjectInfoAsync("|M.rvt");

                var p = Assert.Single(parameters);
                Assert.Equal("Номер проекта", p.Name);
                Assert.Equal(ParamType.Custom, p.Type);
                Assert.Equal(ParamDataType.Number, p.DataType);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("{'A:title':'x'}")]
        [InlineData("null")]
        public async Task GetProjectInfoAsync_EmptyOrNonArrayBody_ThrowsRevitServerApiException(string body)
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok(J(body))))
            {
                await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).GetProjectInfoAsync("|M.rvt"));
            }
        }

        [Fact]
        public async Task GetProjectInfoAsync_NonObjectPropertyValues_AreSkipped()
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok(J("[{'A:title':'T','A:extra':'text','n':5,'P':{'#text':'v','@displayName':'P1'}},'stray',[]]"))))
            {
                var parameters = await CreateApi(server).GetProjectInfoAsync("|M.rvt");

                var p = Assert.Single(parameters);
                Assert.Equal("P1", p.Name);
                Assert.Equal("T", p.Category);
            }
        }

        [Fact]
        public async Task GetProjectInfoAsync_NotFound_IsThrown()
        {
            using (var server = Stub(new Dictionary<string, StubResponse>()))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => CreateApi(server).GetProjectInfoAsync("|M.rvt"));

                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            }
        }
    }
}
