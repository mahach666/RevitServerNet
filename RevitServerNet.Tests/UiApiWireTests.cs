using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Wire tests of the typed <see cref="RevitServerUiApi"/> methods. The response bodies are real answers captured from
    /// Revit Server 2022 and 2025 Admin UI APIs (the two servers answer identically).
    /// </summary>
    public class UiApiWireTests
    {
        private const string Vdir = "/RevitServerAdmin2022/api/";

        // Real error descriptors (HTTP 200 for an already locked / unlocked item).
        private const string AlreadyLocked =
            "{\"StatusCode\":200,\"ServerPath\":\"RSN_p\\\\copy.rvt\",\"ItemType\":\"rs-model\",\"TitleText\":\"Cannot Lock Model\",\"MessageText\":\"This operation cannot be completed because a model could not be locked.\",\"DetailText\":\"The model is already locked.\",\"SuggestionText\":\"Refresh the data and try again.\"}";
        private const string AlreadyUnlocked =
            "{\"StatusCode\":200,\"ServerPath\":\"RSN_p\\\\copy.rvt\",\"ItemType\":\"rs-model\",\"TitleText\":\"Cannot Unlock Model\",\"MessageText\":\"This operation cannot be completed because a model could not be unlocked.\",\"DetailText\":\"The model is already unlocked.\",\"SuggestionText\":\"Refresh the data and try again.\"}";
        private const string ModelNotFound =
            "{\"StatusCode\":404,\"ServerPath\":\"RSN_p\\\\copy.rvt\",\"ItemType\":\"rs-model\",\"TitleText\":\"Model Not Found\",\"MessageText\":\"You are trying to access a model that does not exist on the server.\",\"DetailText\":null,\"SuggestionText\":\"Refresh the data and try again.\"}";
        private const string ModelExists =
            "{\"StatusCode\":409,\"ServerPath\":\"RSN_p\\\\copy.rvt\",\"ItemType\":\"rs-model\",\"TitleText\":\"Model Exists\",\"MessageText\":\"This model already exists on the server.\",\"DetailText\":null,\"SuggestionText\":\"Refresh the data and try again using a different name.\"}";

        private const string Id = "localhost\\RSN_p\\copy.rvt";
        private const string IdQuery = "localhost%5CRSN_p%5Ccopy.rvt";
        private const string TrickyId = "h\\a&b #1+2%";
        private const string TrickyIdQuery = "h%5Ca%26b%20%231%2B2%25";

        private static RevitServerUiApi CreateApi(StubHttpServer server, string year = "2022")
            => new RevitServerUiApi(server.Host, false, year);

        private static StubHttpServer Respond(StubResponse response) => new StubHttpServer(_ => response);

        [Fact]
        public async Task GetModelDetails_MapsRealResponse_AndEscapesId()
        {
            const string json = "{\"LastUpdatedBy\":\"Mahach\",\"SupportedSize\":46084,\"ProductVersion\":\"2022\",\"DateModified\":\"2026-06-07T15:00:10Z\",\"DateCreated\":\"2026-11-13T22:12:06.3601179Z\",\"Size\":16747025,\"IsAlive\":true,\"Id\":\"localhost\\\\RSN_p\\\\copy.rvt\",\"Name\":\"copy.rvt\",\"ServerPath\":\"RSN_p\\\\copy.rvt\",\"Type\":\"rs-model\",\"LockStatus\":\"Locked\",\"LockContext\":null}";
            using (var server = Respond(StubResponse.Ok(json)))
            {
                var d = await CreateApi(server).GetModelDetailsAsync(TrickyId);

                Assert.Equal("GET " + Vdir + "model/details?id=" + TrickyIdQuery, server.SingleRequest());
                Assert.Equal(Id, d.Id);
                Assert.Equal("rs-model", d.Type);
                Assert.Equal("copy.rvt", d.Name);
                Assert.Equal("RSN_p\\copy.rvt", d.ServerPath);
                Assert.True(d.IsAlive);
                Assert.Equal("Locked", d.LockStatus);
                Assert.Null(d.LockContext);
                Assert.Equal(16747025L, d.Size);
                Assert.Equal(46084L, d.SupportedSize);
                Assert.Equal("2022", d.ProductVersion);
                Assert.Equal("Mahach", d.LastUpdatedBy);
                Assert.Equal("2026-06-07T15:00:10Z", d.DateModified);
                Assert.StartsWith("2026-11-13T22:12:06", d.DateCreated);
            }
        }

        [Fact]
        public async Task GetItemLockData_SendsPlaceholder_AndMapsLowercaseFields()
        {
            const string json = "{\"type\":\"Model\",\"path\":\"localhost\\\\RSN_p\\\\copy.rvt\",\"lockStatus\":\"HasLockedAncestor\",\"lockContext\":null}";
            using (var server = Respond(StubResponse.Ok(json)))
            {
                var d = await CreateApi(server).GetItemLockDataAsync(Id);

                Assert.Equal("GET " + Vdir + "folder/ItemLockData?id=" + IdQuery + "&placeholder=", server.SingleRequest());
                Assert.Equal("Model", d.Type);
                Assert.Equal(Id, d.Path);
                Assert.Equal("HasLockedAncestor", d.LockStatus);
                Assert.Null(d.LockContext);
            }
        }

        [Fact]
        public async Task GetModelHistories_MapsRealResponse()
        {
            const string json = "[{\"Comment\":\"\",\"Date\":\"2026-06-07T09:55:00Z\",\"ModelSize\":13998225,\"OverwrittenByHistoryNumber\":0,\"SupportSize\":61127,\"User\":\"Mahach\",\"VersionNumber\":1},{\"Comment\":\"c\",\"Date\":\"2026-06-07T09:55:06Z\",\"ModelSize\":13998539,\"OverwrittenByHistoryNumber\":3,\"SupportSize\":1117456,\"User\":\"Mahach\",\"VersionNumber\":2}]";
            using (var server = Respond(StubResponse.Ok(json)))
            {
                var list = await CreateApi(server).GetModelHistoriesAsync(Id);

                Assert.Equal("GET " + Vdir + "model/ModelHistories?type=rs-model&id=" + IdQuery, server.SingleRequest());
                Assert.Equal(2, list.Count);
                Assert.Equal(1, list[0].VersionNumber);
                Assert.Equal(13998225L, list[0].ModelSize);
                Assert.Equal(61127L, list[0].SupportSize);
                Assert.Equal("Mahach", list[0].User);
                Assert.Equal("2026-06-07T09:55:00Z", list[0].Date);
                Assert.Equal("c", list[1].Comment);
                Assert.Equal(3, list[1].OverwrittenByHistoryNumber);
            }
        }

        [Fact]
        public async Task GetModelHistories_EmptyBody_ReturnsEmptyList()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                Assert.Empty(await CreateApi(server).GetModelHistoriesAsync(Id));
            }
        }

        [Fact]
        public async Task GetSubItems_SendsDepth_AndMapsNestedChildren()
        {
            const string json = "{\"Id\":\"localhost\\\\RSN_p\",\"IsAlive\":true,\"Name\":\"RSN_p\",\"ServerPath\":\"RSN_p\",\"Type\":\"rs-folder\",\"LockStatus\":\"Unlocked\",\"LockContext\":null,\"Children\":[{\"Id\":\"localhost\\\\RSN_p\\\\sub\",\"IsAlive\":true,\"Name\":\"sub\",\"ServerPath\":\"RSN_p\\\\sub\",\"Type\":\"rs-folder\",\"LockStatus\":\"Unlocked\",\"LockContext\":null,\"Children\":[{\"Id\":\"localhost\\\\RSN_p\\\\sub\\\\moved.rvt\",\"IsAlive\":true,\"Name\":\"moved.rvt\",\"ServerPath\":\"RSN_p\\\\sub\\\\moved.rvt\",\"Type\":\"rs-model\",\"LockStatus\":\"Unlocked\",\"LockContext\":null,\"Children\":null}]}]}";
            using (var server = Respond(StubResponse.Ok(json)))
            {
                var tree = await CreateApi(server).GetSubItemsAsync("localhost\\RSN_p", 2);

                Assert.Equal("GET " + Vdir + "folder/SubItems?id=localhost%5CRSN_p&depth=2", server.SingleRequest());
                Assert.Equal("rs-folder", tree.Type);
                var sub = Assert.Single(tree.Children);
                Assert.Equal("sub", sub.Name);
                var model = Assert.Single(sub.Children);
                Assert.Equal("rs-model", model.Type);
                Assert.Equal("RSN_p\\sub\\moved.rvt", model.ServerPath);
                Assert.Null(model.Children);
            }
        }

        [Fact]
        public async Task GuardedArguments_ThrowWithoutSendingARequest()
        {
            using (var server = Respond(StubResponse.Ok("{}")))
            {
                var api = CreateApi(server);

                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetSubItemsAsync(Id, -1));
                await Assert.ThrowsAsync<ArgumentException>(() => api.GetSubItemsAsync(" "));
                await Assert.ThrowsAsync<ArgumentException>(() => api.GetModelDetailsAsync(null));
                await Assert.ThrowsAsync<ArgumentException>(() => api.GetItemLockDataAsync(""));
                await Assert.ThrowsAsync<ArgumentException>(() => api.GetModelHistoriesAsync(Id, " "));
                await Assert.ThrowsAsync<ArgumentException>(() => api.LockAsync(""));
                await Assert.ThrowsAsync<ArgumentException>(() => api.UnlockAsync(null));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CreateFolderAsync(" "));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyOrMoveAsync(Id, "", RevitServerUiApi.PasteAction.Copy));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyOrMoveAsync("", Id, RevitServerUiApi.PasteAction.Move));
                await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteOrRenameAsync(""));
                await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteItemAsync(null));
                await Assert.ThrowsAsync<ArgumentException>(() => api.RenameAsync("", "x"));

                Assert.Empty(server.Requests);
            }
        }

        [Fact]
        public async Task DeleteOrRename_WithoutNewName_SendsDelete()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).DeleteOrRenameAsync(Id);

                Assert.Equal("DELETE " + Vdir + "operation?operationtype=DeleteOrRename&id=" + IdQuery, server.SingleRequest());
            }
        }

        [Fact]
        public async Task DeleteItem_SendsDelete()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).DeleteItemAsync(Id);

                Assert.Equal("DELETE " + Vdir + "operation?operationtype=DeleteOrRename&id=" + IdQuery, server.SingleRequest());
            }
        }

        [Fact]
        public async Task DeleteOrRename_WithNewName_SendsRename()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).DeleteOrRenameAsync(Id, "r 1&.rvt");

                Assert.Equal("DELETE " + Vdir + "operation?operationtype=DeleteOrRename&id=" + IdQuery + "&newName=r%201%26.rvt", server.SingleRequest());
            }
        }

        [Fact]
        public async Task Rename_SendsNewName()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).RenameAsync(Id, "r.rvt");

                Assert.Equal("DELETE " + Vdir + "operation?operationtype=DeleteOrRename&id=" + IdQuery + "&newName=r.rvt", server.SingleRequest());
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("   ")]
        [InlineData("\t")]
        public async Task DeleteOrRename_BlankNewName_ThrowsAndNeverReachesTheServer(string newName)
        {
            // The server treats a blank newName as a delete (verified on Revit Server 2022 and 2025).
            using (var server = Respond(StubResponse.Ok("")))
            {
                var api = CreateApi(server);

                var ex = await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteOrRenameAsync(Id, newName));
                Assert.Equal("newName", ex.ParamName);
                await Assert.ThrowsAsync<ArgumentException>(() => api.RenameAsync(Id, newName));

                Assert.Empty(server.Requests);
            }
        }

        [Theory]
        [InlineData(RevitServerUiApi.PasteAction.Copy, false, "Copy", "false")]
        [InlineData(RevitServerUiApi.PasteAction.Copy, true, "Copy", "true")]
        [InlineData(RevitServerUiApi.PasteAction.Move, false, "Move", "false")]
        public async Task CopyOrMove_SendsPostWithActionAndReplaceFlag_AndReturnsTheUnquotedUrl(
            RevitServerUiApi.PasteAction action, bool replace, string actionText, string replaceText)
        {
            using (var server = Respond(new StubResponse(201, "Created", "\"http://localhost/localhost/RSN_p/sub/moved.rvt\"")))
            {
                var url = await CreateApi(server).CopyOrMoveAsync(Id, "localhost\\RSN_p\\sub\\moved.rvt", action, replace);

                Assert.Equal(
                    "POST " + Vdir + "folder/copyOrMove?id=" + IdQuery + "&destinationid=localhost%5CRSN_p%5Csub%5Cmoved.rvt&pasteAction="
                    + actionText + "&replaceExisting=" + replaceText,
                    server.SingleRequest());
                Assert.Equal("http://localhost/localhost/RSN_p/sub/moved.rvt", url);
            }
        }

        [Fact]
        public async Task CopyOrMove_Defaults_ReplaceExistingFalse()
        {
            using (var server = Respond(new StubResponse(201, "Created", "\"u\"")))
            {
                await CreateApi(server).CopyOrMoveAsync(Id, Id + "2", RevitServerUiApi.PasteAction.Copy);

                Assert.EndsWith("&replaceExisting=false", server.SingleRequest());
            }
        }

        [Fact]
        public async Task CopyOrMove_ModelExists_ThrowsWithConflictAndDescriptor()
        {
            using (var server = Respond(new StubResponse(409, "Conflict", ModelExists)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(
                    () => CreateApi(server).CopyOrMoveAsync(Id, Id, RevitServerUiApi.PasteAction.Copy));

                Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
                Assert.Equal(ModelExists, ex.ResponseContent);
            }
        }

        [Theory]
        [InlineData("\"http://h/p\"", "http://h/p")]
        [InlineData("http://h/p", "http://h/p")]
        [InlineData("  http://h/p\r\n", "http://h/p")]
        [InlineData("", "")]
        public async Task CreateFolder_ReturnsUrlFromJsonStringOrRawBody(string body, string expected)
        {
            using (var server = Respond(new StubResponse(201, "Created", body)))
            {
                var url = await CreateApi(server).CreateFolderAsync("localhost\\RSN_p\\New Folder");

                Assert.Equal("PUT " + Vdir + "folder/createfolder?id=localhost%5CRSN_p%5CNew%20Folder", server.SingleRequest());
                Assert.Equal(expected, url);
            }
        }

        [Fact]
        public async Task CreateFolder_FolderExists_ThrowsWithConflict()
        {
            const string body = "{\"StatusCode\":409,\"ServerPath\":\"RSN_p\",\"ItemType\":\"rs-folder\",\"TitleText\":\"Folder Exists\",\"MessageText\":\"This folder already exists on the server.\",\"DetailText\":null,\"SuggestionText\":\"Refresh the data and try again using a different name.\"}";
            using (var server = Respond(new StubResponse(409, "Conflict", body)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => CreateApi(server).CreateFolderAsync("localhost\\RSN_p"));

                Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
                Assert.Equal(body, ex.ResponseContent);
            }
        }

        [Fact]
        public async Task Lock_Success_SendsPutAndAcceptsEmptyBody()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).LockAsync(Id);

                Assert.Equal("PUT " + Vdir + "operation?operationType=Lock&id=" + IdQuery, server.SingleRequest());
            }
        }

        [Fact]
        public async Task Lock_Http200WithErrorDescriptor_Throws()
        {
            using (var server = Respond(StubResponse.Ok(AlreadyLocked)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => CreateApi(server).LockAsync(Id));

                Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
                Assert.Equal(AlreadyLocked, ex.ResponseContent);
                Assert.Contains("Cannot Lock Model", ex.Message);
                Assert.Contains("The model is already locked.", ex.Message);
                Assert.Equal("PUT " + Vdir + "operation?operationType=Lock&id=" + IdQuery, server.SingleRequest());
            }
        }

        [Fact]
        public async Task Lock_MissingItem_ThrowsWith404()
        {
            using (var server = Respond(new StubResponse(404, "Not Found", ModelNotFound)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => CreateApi(server).LockAsync(Id));

                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
                Assert.Equal(ModelNotFound, ex.ResponseContent);
            }
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public async Task Unlock_Success_SendsDeleteWithItemMustExist(bool itemMustExist, string text)
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).UnlockAsync(Id, itemMustExist);

                Assert.Equal("DELETE " + Vdir + "operation?operationtype=Unlock&id=" + IdQuery + "&itemMustExist=" + text, server.SingleRequest());
            }
        }

        [Fact]
        public async Task Unlock_DefaultsToItemMustExist()
        {
            using (var server = Respond(StubResponse.Ok("")))
            {
                await CreateApi(server).UnlockAsync(Id);

                Assert.EndsWith("&itemMustExist=true", server.SingleRequest());
            }
        }

        [Fact]
        public async Task Unlock_Http200WithErrorDescriptor_Throws()
        {
            using (var server = Respond(StubResponse.Ok(AlreadyUnlocked)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(() => CreateApi(server).UnlockAsync(Id, false));

                Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
                Assert.Equal(AlreadyUnlocked, ex.ResponseContent);
                Assert.Contains("Cannot Unlock Model", ex.Message);
                Assert.Contains("The model is already unlocked.", ex.Message);
            }
        }

        [Fact]
        public async Task RawHelpers_Http200WithErrorDescriptor_Throw_ButOtherJsonIsReturned()
        {
            const string data = "{\"StatusCode\":\"x\",\"Name\":\"no title, so it is data\"}";
            using (var server = new StubHttpServer(line => line.Contains("Lock")
                ? StubResponse.Ok(AlreadyLocked)
                : StubResponse.Ok(data)))
            {
                var api = CreateApi(server);

                await Assert.ThrowsAsync<RevitServerUiApiException>(
                    () => api.PutAsync("api/operation", new Dictionary<string, string> { { "operationType", "Lock" } }));
                Assert.Equal(data, await api.GetAsync("api/other"));
            }
        }

        [Theory]
        [InlineData("2012", "/RevitServerAdmin/api/model/details")]
        [InlineData("2013", "/RevitServerAdmin2013/api/model/details")]
        [InlineData("2022", "/RevitServerAdmin2022/api/model/details")]
        [InlineData("2025", "/RevitServerAdmin2025/api/model/details")]
        public async Task Constructor_MapsYearToVirtualDirectory(string year, string path)
        {
            using (var server = Respond(StubResponse.Ok("{}")))
            {
                await CreateApi(server, year).GetModelDetailsAsync("h\\m");

                Assert.StartsWith("GET " + path + "?", server.SingleRequest());
            }
        }

        [Theory]
        [InlineData("2011")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData(null)]
        public void Constructor_InvalidYear_Throws(string year)
        {
            Assert.Throws<ArgumentException>(() => new RevitServerUiApi("h", false, year));
        }

        [Fact]
        public void Constructor_EmptyHost_Throws()
        {
            Assert.Throws<ArgumentException>(() => new RevitServerUiApi(" "));
        }

        [Fact]
        public void BaseUrl_UsesProtocolHostAndVirtualDirectory()
        {
            Assert.Equal("https://h:8443/RevitServerAdmin2025", new RevitServerUiApi("h:8443", true, "2025").BaseUrl);
        }

        [Fact]
        public void Calls_DoNotCaptureTheSynchronizationContext()
        {
            // With a captured context the continuation is posted to a context that is never pumped here and the wait times out.
            using (var server = Respond(StubResponse.Ok("{\"Id\":\"x\"}")))
            {
                var api = CreateApi(server);
                var previous = SynchronizationContext.Current;
                Task<Models.UiModelDetails> task;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
                    task = api.GetModelDetailsAsync("h\\m");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }

#pragma warning disable xUnit1031 // blocking on the task is what this test checks
                Assert.True(task.Wait(TimeSpan.FromSeconds(15)), "the call did not complete without the caller's synchronization context");
                Assert.Equal("x", task.Result.Id);
#pragma warning restore xUnit1031
            }
        }

        private sealed class NeverPumpedContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
                // intentionally dropped
            }
        }
    }
}
