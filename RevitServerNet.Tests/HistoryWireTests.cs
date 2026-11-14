using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitServerNet.Extensions;
using RevitServerNet.Models;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// DTO mapping, history helpers and lock queries against responses captured from live Revit Server 2022 and 2025
    /// (the two servers returned the same field names; only the date values and GUIDs differ).
    /// In the JSON constants ' stands for " and \\ is a JSON-escaped backslash, as the server sends them.
    /// </summary>
    public class HistoryWireTests
    {
        private const string Service = "/RevitServerAdminRESTService2022/AdminRESTService.svc/";
        private const string TestModel = "%7CTest%7CYES_%D0%90%D0%A0.rvt";
        private const string ProbeFolder = "%7CRSN_dto_history_probe";
        private const string ProbeModel = ProbeFolder + "%7CYES_%D0%90%D0%A0.rvt";

        private static string J(string s) => s.Replace('\'', '"');

        // GET |Test|YES_АР.rvt/history, RS 2022 (identical on 2025)
        private static readonly string HistoryJson = J(@"{'Path':'Test\\YES_АР.rvt','Items':[{'Comment':'','Date':'\/Date(1780826100000)\/','ModelSize':13998225,'OverwrittenByHistoryNumber':0,'SupportSize':61127,'User':'Mahach','VersionNumber':1},{'Comment':'','Date':'\/Date(1780826106000)\/','ModelSize':13998539,'OverwrittenByHistoryNumber':0,'SupportSize':1117456,'User':'Mahach','VersionNumber':2},{'Comment':'','Date':'\/Date(1780826248000)\/','ModelSize':14000892,'OverwrittenByHistoryNumber':0,'SupportSize':1117770,'User':'Mahach','VersionNumber':3},{'Comment':'','Date':'\/Date(1780835016000)\/','ModelSize':14003353,'OverwrittenByHistoryNumber':0,'SupportSize':2744384,'User':'Mahach','VersionNumber':4},{'Comment':'','Date':'\/Date(1780837313000)\/','ModelSize':14001607,'OverwrittenByHistoryNumber':0,'SupportSize':2746845,'User':'Mahach','VersionNumber':5},{'Comment':'','Date':'\/Date(1780840916000)\/','ModelSize':14006035,'OverwrittenByHistoryNumber':0,'SupportSize':2744587,'User':'Mahach','VersionNumber':6},{'Comment':'','Date':'\/Date(1780841754000)\/','ModelSize':14005572,'OverwrittenByHistoryNumber':0,'SupportSize':2746691,'User':'Mahach','VersionNumber':7},{'Comment':'','Date':'\/Date(1780841793000)\/','ModelSize':16570306,'OverwrittenByHistoryNumber':0,'SupportSize':2746733,'User':'Mahach','VersionNumber':8},{'Comment':'','Date':'\/Date(1780842071000)\/','ModelSize':16648445,'OverwrittenByHistoryNumber':0,'SupportSize':2764033,'User':'Mahach','VersionNumber':9},{'Comment':'','Date':'\/Date(1780844410000)\/','ModelSize':16648721,'OverwrittenByHistoryNumber':0,'SupportSize':2766672,'User':'Mahach','VersionNumber':10}]}");

        // GET serverproperties, RS 2022
        private const string ServerPropertiesJson =
            "{\"AccessLevelTypes\":[],\"MachineName\":\"0163E0FFBE52\",\"MaximumFolderPathLength\":98,\"MaximumModelNameLength\":40,\"ServerRoles\":[0,1,2],\"Servers\":[\"localhost\"]}";

        // GET |Test|YES_АР.rvt/modelinfo, RS 2022
        private static readonly string ModelInfoJson = J(@"{'Path':'Test\\YES_АР.rvt','DateCreated':'\/Date(1780826093277)\/','DateModified':'\/Date(1780844410000)\/','LastModifiedBy':'Mahach','ModelGUID':'4c6bb8be-13c3-4c52-a4e9-32a4df44ec46','ModelSize':16648721,'SupportSize':1750275}");

        // GET |Test/directoryinfo, RS 2022
        private static readonly string FolderInfoJson = J(@"{'Path':'Test','DateCreated':'\/Date(1780788031754)\/','DateModified':'\/Date(1780826133270)\/','Exists':true,'FolderCount':0,'IsFolder':true,'LastModifiedBy':null,'LockContext':null,'LockState':0,'ModelCount':2,'ModelLocksInProgress':[],'ModelSize':0,'Size':37348930}");

        // GET |Test/contents, RS 2022
        private static readonly string ContentsJson = J(@"{'Path':'Test','DriveFreeSpace':135833812992,'DriveSpace':136229920768,'Files':[],'Folders':[],'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'Models':[{'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'ModelSize':16648721,'Name':'YES_АР.rvt','ProductVersion':12,'SupportSize':1750275},{'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'ModelSize':17429035,'Name':'Yes_ОВ.rvt','ProductVersion':12,'SupportSize':1520899}]}");

        // GET |RSN_dto_history_probe/contents while its only model was locked, RS 2025
        private static readonly string LockedContentsJson = J(@"{'Path':'RSN_dto_history_probe','DriveFreeSpace':135964209152,'DriveSpace':136229920768,'Files':[],'Folders':[],'LockContext':null,'LockState':3,'ModelLocksInProgress':null,'Models':[{'LockContext':null,'LockState':1,'ModelLocksInProgress':null,'ModelSize':16747025,'Name':'YES_АР.rvt','ProductVersion':12,'SupportSize':46084}]}");

        // GET |RSN_dto_history_probe|YES_АР.rvt/directoryinfo while locked, RS 2025
        private static readonly string LockedModelInfoJson = J(@"{'Path':'RSN_dto_history_probe\\YES_АР.rvt','DateCreated':'\/Date(1794607803769)\/','DateModified':'\/Date(1780844410000)\/','Exists':true,'FolderCount':0,'IsFolder':false,'LastModifiedBy':'Mahach','LockContext':null,'LockState':1,'ModelCount':1,'ModelLocksInProgress':null,'ModelSize':16747025,'Size':16793109}");

        // GET |Test|YES_АР.rvt/directoryinfo, RS 2022
        private static readonly string UnlockedModelInfoJson = J(@"{'Path':'Test\\YES_АР.rvt','DateCreated':'\/Date(1780826093277)\/','DateModified':'\/Date(1780844410000)\/','Exists':true,'FolderCount':0,'IsFolder':false,'LastModifiedBy':'Mahach','LockContext':null,'LockState':0,'ModelCount':1,'ModelLocksInProgress':null,'ModelSize':16648721,'Size':18398996}");

        // GET |/descendent/locks while a model was locked, RS 2025 (RS 2022 answers the same way)
        private static readonly string DescendentLocksJson = J(@"{'Path':'','DescendentHasLockContext':false,'Items':['RSN_dto_history_probe\\YES_АР.rvt']}");

        // GET |/descendent/locks without locks, RS 2022
        private static readonly string NoLocksJson = J(@"{'Path':'','DescendentHasLockContext':false,'Items':null}");

        // 404 of directoryinfo for a model that does not exist, RS 2022
        private static readonly string MissingDirectoryInfoJson = J(@"{'Path':'RSN_dto_history_probe\\nope.rvt','DateCreated':null,'DateModified':null,'Exists':false,'FolderCount':0,'IsFolder':false,'LastModifiedBy':null,'LockContext':null,'LockState':0,'ModelCount':0,'ModelLocksInProgress':null,'ModelSize':0,'Size':0}");

        // An element of ModelLocksInProgress while a model was exported (captured by the audit on RS 2022).
        private static readonly string InProgressLockJson = J(@"{'Age':'PT1.0150588S','ModelLockOptions':0,'ModelLockType':0,'ModelPath':'Test\\YES_АР.rvt','TimeStamp':'\/Date(1794606014000)\/','UserName':'RevitServerTool:MAHACH-LT:1'}");

        private static StubHttpServer Serve(string command, string json)
        {
            return StubHttpServer.WithJson(new Dictionary<string, string> { { "GET " + Service + command, json } });
        }

        private static RevitServerApi Api(StubHttpServer server) => new RevitServerApi(server.Host, "tester", false, "2022");

        private static DateTime UtcOf(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

        private static int[] Versions(ModelHistory h) => h.Items.Select(i => i.Version).ToArray();

        // ---- history ----

        [Fact]
        public async Task History_MapsVersionSizeOverwrittenByAndDate()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var history = await Api(server).GetModelHistoryAsync("|Test|YES_АР.rvt");

                Assert.Equal(10, history.Items.Count);
                Assert.Equal(Enumerable.Range(1, 10), history.Items.Select(i => i.Version));
                var first = history.Items[0];
                Assert.Equal(13998225, first.Size);
                Assert.Equal(61127, first.SupportSize);
                Assert.Equal(0, first.OverwrittenByHistoryNumber);
                Assert.Equal("Mahach", first.User);
                Assert.Equal("/Date(1780826100000)/", first.Date);
                Assert.Equal(UtcOf(1780826100000), first.DateUtc);
                Assert.Equal(DateTimeKind.Utc, first.DateUtc.Value.Kind);
                Assert.Equal(16648721, history.Items[9].Size);
            }
        }

        [Fact]
        public async Task LatestAndNumberedVersions_UseTheVersionNumber()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var api = Api(server);

                Assert.Equal(10, (await api.GetLatestVersionAsync("|Test|YES_АР.rvt")).Version);
                Assert.Equal(13998539, (await api.GetVersionAsync("|Test|YES_АР.rvt", 2)).Size);
                Assert.Null(await api.GetVersionAsync("|Test|YES_АР.rvt", 99));
                Assert.Equal(10, await api.GetVersionCountAsync("|Test|YES_АР.rvt"));
                Assert.Equal(string.Empty, await api.GetLastVersionCommentAsync("|Test|YES_АР.rvt"));
                Assert.Equal(new[] { 10, 9, 8 }, (await api.GetLatestVersionsAsync("|Test|YES_АР.rvt", 3)).Select(i => i.Version));
                Assert.Equal(new[] { 8, 7 }, (await api.GetModelHistoryPageAsync("|Test|YES_АР.rvt", 2, 2)).Select(i => i.Version));
            }
        }

        [Fact]
        public async Task TotalModelSize_SumsModelAndSupportSizes()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var total = await Api(server).GetTotalModelSizeAsync("|Test|YES_АР.rvt");

                Assert.Equal(13998225L + 13998539 + 14000892 + 14003353 + 14001607 + 14006035 + 14005572 + 16570306 + 16648445 + 16648721
                    + 61127 + 1117456 + 1117770 + 2744384 + 2746845 + 2744587 + 2746691 + 2746733 + 2764033 + 2766672, total);
            }
        }

        [Fact]
        public async Task HistoryFilter_ByVersionRange_ReturnsNewestFirst()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var history = await Api(server).GetModelHistoryAsync("|Test|YES_АР.rvt", minVersion: 3, maxVersion: 5);

                Assert.Equal(new[] { 5, 4, 3 }, Versions(history));
                Assert.Equal("Test\\YES_АР.rvt", history.Path);
            }
        }

        [Fact]
        public async Task HistoryFilter_ByDate_ParsesTheWcfDate_AndBoundsAreInclusive()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var api = Api(server);

                var from = await api.GetModelHistoryAsync("|Test|YES_АР.rvt", fromDate: UtcOf(1780841793000));
                var to = await api.GetModelHistoryAsync("|Test|YES_АР.rvt", toDate: UtcOf(1780826106000));
                var both = await api.GetModelHistoryAsync("|Test|YES_АР.rvt", fromDate: UtcOf(1780826248000), toDate: UtcOf(1780837313000));
                var all = await api.GetModelHistoryAsync("|Test|YES_АР.rvt", fromDate: new DateTime(2000, 1, 1));
                var none = await api.GetModelHistoryAsync("|Test|YES_АР.rvt", toDate: new DateTime(2000, 1, 1));

                Assert.Equal(new[] { 10, 9, 8 }, Versions(from));
                Assert.Equal(new[] { 2, 1 }, Versions(to));
                Assert.Equal(new[] { 5, 4, 3 }, Versions(both));
                Assert.Equal(10, all.Items.Count);
                Assert.Empty(none.Items);
            }
        }

        [Fact]
        public async Task HistoryFilter_DateBoundOfLocalKind_IsComparedAsThatInstant()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var local = UtcOf(1780841793000).ToLocalTime();

                var history = await Api(server).GetModelHistoryAsync("|Test|YES_АР.rvt", fromDate: local);

                Assert.Equal(new[] { 10, 9, 8 }, Versions(history));
            }
        }

        [Fact]
        public async Task HistoryFilter_ByUserIsCaseInsensitive_AndPagingClampsNegativeAndZero()
        {
            using (var server = Serve(TestModel + "/history", HistoryJson))
            {
                var api = Api(server);
                const string path = "|Test|YES_АР.rvt";

                Assert.Equal(10, (await api.GetModelHistoryAsync(path, userFilter: "mAHACH")).Items.Count);
                Assert.Empty((await api.GetModelHistoryAsync(path, userFilter: "someone")).Items);
                Assert.Equal(new[] { 10, 9 }, Versions(await api.GetModelHistoryAsync(path, take: 2)));
                Assert.Equal(new[] { 2, 1 }, Versions(await api.GetModelHistoryAsync(path, skip: 8, take: 5)));
                Assert.Equal(new[] { 9, 8 }, Versions(await api.GetModelHistoryAsync(path, skip: 1, take: 2)));
                Assert.Empty((await api.GetModelHistoryAsync(path, take: 0)).Items);
                Assert.Equal(10, (await api.GetModelHistoryAsync(path, skip: -3)).Items.Count);
                Assert.Empty((await api.GetModelHistoryAsync(path, take: -1)).Items);
                Assert.Equal(new[] { 10, 9 }, (await api.GetVersionsByUserAsync(path, "Mahach", 2)).Select(i => i.Version));
                Assert.Equal(10, (await api.GetVersionsByUserAsync(path, "Mahach")).Count);
            }
        }

        [Theory]
        [InlineData("/Date(1780826100000)/", 1780826100000)]
        [InlineData("/Date(1780826100000+0300)/", 1780826100000)]
        [InlineData("/Date(-1000)/", -1000)]
        public void DateUtc_ParsesTheWcfFormats(string wire, long ms)
        {
            Assert.Equal(UtcOf(ms), new HistoryItem { Date = wire }.DateUtc);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2026-06-07T10:15:00Z")]
        [InlineData("/Date(abc)/")]
        [InlineData("/Date(99999999999999999)/")]
        public void DateUtc_IsNullForAnythingElse(string wire)
        {
            Assert.Null(new HistoryItem { Date = wire }.DateUtc);
        }

        // ---- models ----

        [Fact]
        public async Task GetModelInfo_MapsSizeGuidDatesAndDerivesTheName()
        {
            using (var server = Serve(TestModel + "/modelinfo", ModelInfoJson))
            {
                var info = await Api(server).GetModelInfoAsync("|Test|YES_АР.rvt");

                Assert.Equal("Test\\YES_АР.rvt", info.Path);
                Assert.Equal("YES_АР.rvt", info.Name);
                Assert.Equal(16648721, info.Size);
                Assert.Equal(1750275, info.SupportSize);
                Assert.Equal("4c6bb8be-13c3-4c52-a4e9-32a4df44ec46", info.ModelGUID);
                Assert.Equal("Mahach", info.LastModifiedBy);
                Assert.Equal(UtcOf(1780826093277), info.DateCreatedUtc);
                Assert.Equal(UtcOf(1780844410000), info.DateModifiedUtc);
            }
        }

        [Fact]
        public async Task ListModels_MapsModelSizeNameAndProductVersion()
        {
            using (var server = Serve("%7CTest/contents", ContentsJson))
            {
                var models = await Api(server).ListModelsAsync("|Test");

                Assert.Equal(new[] { "YES_АР.rvt", "Yes_ОВ.rvt" }, models.Select(m => m.Name));
                Assert.Equal(new long[] { 16648721, 17429035 }, models.Select(m => m.Size));
                Assert.Equal(new long[] { 1750275, 1520899 }, models.Select(m => m.SupportSize));
                Assert.All(models, m => Assert.Equal(12, m.ProductVersion));
            }
        }

        [Fact]
        public async Task GetFolderInfo_MapsCountsDatesAndDerivesNameAndHasContents()
        {
            using (var server = Serve("%7CTest/directoryinfo", FolderInfoJson))
            {
                var info = await Api(server).GetFolderInfoAsync("|Test");

                Assert.Equal("Test", info.Name);
                Assert.True(info.HasContents);
                Assert.Equal(2, info.ModelCount);
                Assert.Equal(0, info.FolderCount);
                Assert.Equal(37348930, info.Size);
                Assert.True(info.Exists == true);
                Assert.True(info.IsFolder);
                Assert.Equal(LockState.Unlocked, info.LockState);
                Assert.Equal(UtcOf(1780788031754), info.DateCreatedUtc);
                Assert.Equal(UtcOf(1780826133270), info.DateModifiedUtc);
            }
        }

        [Fact]
        public void FolderInfo_KeepsHasContentsAndNameFromAParentsContents()
        {
            // an element of Folders in the root contents, RS 2022
            var folder = JsonConvert.DeserializeObject<FolderInfo>(J(@"{'HasContents':false,'LockContext':null,'LockState':0,'ModelLocksInProgress':null,'Name':'RSN_folders_p','Size':0}"));

            Assert.Equal("RSN_folders_p", folder.Name);
            Assert.False(folder.HasContents);
        }

        [Fact]
        public async Task GetFolderContents_ExposesTheLockStateOfTheFolderAndItsModels()
        {
            using (var server = Serve(ProbeFolder + "/contents", LockedContentsJson))
            {
                var contents = await Api(server).GetFolderContentsAsync("|RSN_dto_history_probe");

                Assert.Equal(LockState.LockedChild, contents.LockState);
                Assert.Null(contents.LockContext);
                Assert.Equal(LockState.Locked, contents.Models.Single().LockState);
                Assert.Equal(16747025, contents.Models.Single().Size);
            }
        }

        [Fact]
        public void LockInProgress_MapsOptionsTypeAgeAndTimeStamp()
        {
            var info = JsonConvert.DeserializeObject<IPLockInfo>(InProgressLockJson);

            Assert.Equal("RevitServerTool:MAHACH-LT:1", info.UserName);
            Assert.Equal("Test\\YES_АР.rvt", info.ModelPath);
            Assert.Equal(UtcOf(1794606014000), info.TimeStampUtc);
            Assert.Equal(TimeSpan.FromTicks(10150588), info.AgeSpan);
            Assert.Equal(LockOptions.NotSet, info.LockOptions);
            Assert.Equal(LockType.Data, info.LockType);
        }

        [Fact]
        public void LockInProgress_ReadsTheModelLockNames()
        {
            // values 128 and 1 are not from a server (only zeros were observed); they prove which JSON names are read
            var info = JsonConvert.DeserializeObject<IPLockInfo>(
                J(@"{'Age':'PT2M','ModelLockOptions':128,'ModelLockType':1,'ModelPath':'a','TimeStamp':'x','UserName':'u'}"));

            Assert.Equal(LockOptions.NonExclusiveReadWrite, info.LockOptions);
            Assert.Equal(LockType.Permissions, info.LockType);
            Assert.Equal(TimeSpan.FromMinutes(2), info.AgeSpan);
            Assert.Null(info.TimeStampUtc);
        }

        // ---- server info ----

        [Fact]
        public async Task ServerInfo_MapsRolesAndLimits_AndTheVersionIsTheClientsYear()
        {
            using (var server = Serve("serverproperties", ServerPropertiesJson))
            {
                var api = Api(server);

                var info = await api.GetServerInfoAsync();
                Assert.Equal(new[] { ServerRole.Host, ServerRole.Accelerator, ServerRole.Admin }, info.Roles);
                Assert.Equal("0163E0FFBE52", info.MachineName);
                Assert.Equal(98, info.MaxPathLength);
                Assert.Equal(40, info.MaxNameLength);
                Assert.Equal(new[] { "localhost" }, info.Servers);
                Assert.Equal("2022", info.Version);
                Assert.Equal(new[] { ServerRole.Host, ServerRole.Accelerator, ServerRole.Admin }, await api.GetServerRolesAsync());
                Assert.Equal("2022", await api.GetServerVersionAsync());
                Assert.Equal(40, await api.GetMaximumModelNameLengthAsync());
                Assert.True(await api.IsServerRunningAsync());
                Assert.True(await api.PingServerAsync());
            }
        }

        [Fact]
        public async Task ServerVersion_ComesFromTheYearOfTheServicePath_AlsoFor2012()
        {
            using (var server = new StubHttpServer(line => line == "GET /RevitServerAdminRESTService/AdminRESTService.svc/serverproperties"
                ? StubResponse.Ok(ServerPropertiesJson)
                : new StubResponse(404, "Not Found", line)))
            {
                var api = new RevitServerApi(server.Host, "tester", false, "2012");

                Assert.Equal("2012", await api.GetServerVersionAsync());
            }
        }

        [Fact]
        public async Task IsServerRunning_IsNotMadeTrueByTheClientsVersionAlone()
        {
            using (var server = Serve("serverproperties", "{}"))
            {
                Assert.False(await Api(server).IsServerRunningAsync());
            }
        }

        // ---- lock queries ----

        [Fact]
        public async Task GetLocks_ReadsTheDescendentLocksOfTheRoot()
        {
            using (var server = Serve("%7C/descendent/locks", DescendentLocksJson))
            {
                var api = Api(server);

                var locks = await api.GetLocksAsync();

                var item = Assert.Single(locks.Locks);
                Assert.Equal("RSN_dto_history_probe\\YES_АР.rvt", item.ModelPath);
                Assert.Equal(LockState.Locked, item.LockState);
                Assert.Null(item.UserName);
                Assert.Equal(1, await api.GetActiveLocksCountAsync());
                Assert.Equal("GET " + Service + "%7C/descendent/locks", server.Requests.First());
            }
        }

        [Fact]
        public async Task GetLocks_WithoutLocks_IsEmpty()
        {
            using (var server = Serve("%7C/descendent/locks", NoLocksJson))
            {
                var api = Api(server);

                Assert.Empty((await api.GetLocksAsync()).Locks);
                Assert.Equal(0, await api.GetActiveLocksCountAsync());
            }
        }

        [Fact]
        public async Task GetLocks_ThrowsOnAnEmptyResponse()
        {
            using (var server = Serve("%7C/descendent/locks", ""))
            {
                await Assert.ThrowsAsync<RevitServerApiException>(() => Api(server).GetLocksAsync());
            }
        }

        [Fact]
        public async Task IsModelLocked_IsTrueForALockedModel_AndAsksDirectoryInfoOfTheModel()
        {
            using (var server = Serve(ProbeModel + "/directoryinfo", LockedModelInfoJson))
            {
                var api = Api(server);

                Assert.True(await api.IsModelLockedAsync("|RSN_dto_history_probe|YES_АР.rvt"));
                var info = await api.GetModelLockAsync("|RSN_dto_history_probe|YES_АР.rvt");
                Assert.Equal("RSN_dto_history_probe\\YES_АР.rvt", info.ModelPath);
                Assert.Equal(LockState.Locked, info.LockState);
                Assert.Null(info.UserName);
                Assert.Null(await api.GetModelLockUserAsync("|RSN_dto_history_probe|YES_АР.rvt"));
                Assert.All(server.Requests, r => Assert.Equal("GET " + Service + ProbeModel + "/directoryinfo", r));
            }
        }

        [Fact]
        public async Task IsModelLocked_IsFalseForAnUnlockedModel()
        {
            using (var server = Serve(TestModel + "/directoryinfo", UnlockedModelInfoJson))
            {
                var api = Api(server);

                Assert.False(await api.IsModelLockedAsync("|Test|YES_АР.rvt"));
                Assert.Null(await api.GetModelLockAsync("|Test|YES_АР.rvt"));
            }
        }

        [Fact]
        public async Task IsModelLocked_Http200WithExistsFalse_ThrowsNotFound()
        {
            // RS 2022/2025 answer a missing item with 404 and this body; a 200 with Exists=false must not read as "unlocked" either
            const string missing = "{\"Path\":\"RSN_gone\",\"DateCreated\":null,\"DateModified\":null,\"Exists\":false,\"FolderCount\":0,\"IsFolder\":false,\"LastModifiedBy\":null,\"LockContext\":null,\"LockState\":0,\"ModelCount\":0,\"ModelLocksInProgress\":null,\"ModelSize\":0,\"Size\":0}";
            using (var server = Serve("%7CRSN_gone/directoryinfo", missing))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => Api(server).IsModelLockedAsync("|RSN_gone"));
                Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
            }
        }

        [Fact]
        public async Task GetModelLock_TakesTheOwnerOfALockInProgress()
        {
            // directoryinfo of a model with an element of ModelLocksInProgress (the element itself is a captured one)
            var json = LockedModelInfoJson.Replace("\"ModelLocksInProgress\":null", "\"ModelLocksInProgress\":[" + InProgressLockJson + "]");
            using (var server = Serve(ProbeModel + "/directoryinfo", json))
            {
                var api = Api(server);

                var info = await api.GetModelLockAsync("|RSN_dto_history_probe|YES_АР.rvt");

                Assert.Equal("RevitServerTool:MAHACH-LT:1", info.UserName);
                Assert.Equal("/Date(1794606014000)/", info.TimeStamp);
                Assert.Equal("RevitServerTool:MAHACH-LT:1", await api.GetModelLockUserAsync("|RSN_dto_history_probe|YES_АР.rvt"));
            }
        }

        [Theory]
        [InlineData(500, "Internal Server Error")]
        [InlineData(405, "Method Not Allowed")]
        public async Task IsModelLocked_DoesNotTurnAServerErrorIntoFalse(int status, string reason)
        {
            using (var server = new StubHttpServer(_ => new StubResponse(status, reason, "<html>Service</html>")))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => Api(server).IsModelLockedAsync("|Test|YES_АР.rvt"));

                Assert.Equal((System.Net.HttpStatusCode)status, ex.StatusCode);
            }
        }

        [Fact]
        public async Task IsModelLocked_ThrowsForAModelThatDoesNotExist()
        {
            using (var server = new StubHttpServer(_ => new StubResponse(404, "Not Found", MissingDirectoryInfoJson)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => Api(server).IsModelLockedAsync("|RSN_dto_history_probe|nope.rvt"));

                Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
            }
        }

        [Fact]
        public async Task GetLocksByUser_IsNotSupported_AndSendsNothing()
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok("{}")))
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => Api(server).GetLocksByUserAsync("Mahach"));

                Assert.Empty(server.Requests);
            }
        }
    }
}
