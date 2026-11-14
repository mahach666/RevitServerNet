using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using RevitServerNet.Extensions;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Checks the exact request line RevitServerApi sends and how it handles HTTP errors.
    /// '|' is always sent as %7C: the .NET URI parser escapes it in the path and in the query.
    /// </summary>
    public class RevitServerApiWireTests
    {
        private const string Service = "/RevitServerAdminRESTService2022/AdminRESTService.svc/";

        private static RevitServerApi CreateApi(StubHttpServer server)
        {
            return new RevitServerApi(server.Host, "tester", false, "2022");
        }

        [Theory]
        [InlineData("|#Архив|Model.rvt",
            "%7C%23%D0%90%D1%80%D1%85%D0%B8%D0%B2%7CModel.rvt/history")]
        [InlineData("|Проект #1|M+1 (копия).rvt",
            "%7C%D0%9F%D1%80%D0%BE%D0%B5%D0%BA%D1%82%20%231%7CM%2B1%20%28%D0%BA%D0%BE%D0%BF%D0%B8%D1%8F%29.rvt/history")]
        [InlineData("|100%|a;b?c.rvt",
            "%7C100%25%7Ca%3Bb%3Fc.rvt/history")]
        public async Task GetModelHistoryAsync_SendsEveryNameEscaped(string modelPath, string expectedCommand)
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok("{\"Path\":\"x\",\"Items\":[]}")))
            {
                var history = await CreateApi(server).GetModelHistoryAsync(modelPath);

                Assert.NotNull(history);
                Assert.Equal("GET " + Service + expectedCommand, server.SingleRequest());
            }
        }

        [Fact]
        public async Task RenameFolderAsync_EscapesNewNameInQuery()
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok("{\"Success\":true}")))
            {
                var result = await CreateApi(server).RenameFolderAsync("|Проекты|Old", "#Новая & (2)=x");

                Assert.True(result.Success);
                Assert.Equal(
                    "DELETE " + Service + "%7C%D0%9F%D1%80%D0%BE%D0%B5%D0%BA%D1%82%D1%8B%7COld" +
                    "?newObjectName=%23%D0%9D%D0%BE%D0%B2%D0%B0%D1%8F%20%26%20%282%29%3Dx",
                    server.SingleRequest());
            }
        }

        [Fact]
        public async Task CopyItemAsync_EscapesDestinationPathOnce()
        {
            using (var server = new StubHttpServer(_ => StubResponse.Ok("{\"Success\":true}")))
            {
                var result = await CreateApi(server).CopyItemAsync("|A|m.rvt", "|B|#c&d=1 %.rvt");

                Assert.True(result.Success);
                Assert.Equal(
                    "POST " + Service + "%7CA%7Cm.rvt" +
                    "?destinationObjectPath=%7CB%7C%23c%26d%3D1%20%25.rvt&pasteAction=Copy&replaceExisting=false",
                    server.SingleRequest());
            }
        }

        [Fact]
        public async Task GetAllModelsRecursiveAsync_WalksFolderNamedWithHash()
        {
            var rootContents = "GET " + Service + "%7C/contents";
            var archiveContents = "GET " + Service + "%7C%23%D0%90%D1%80%D1%85%D0%B8%D0%B2/contents";
            var json = new Dictionary<string, string>
            {
                [rootContents] = "{\"Folders\":[{\"Name\":\"#Архив\"}],\"Models\":[]}",
                [archiveContents] = "{\"Folders\":[],\"Models\":[{\"Name\":\"Model.rvt\"}]}",
            };

            using (var server = StubHttpServer.WithJson(json))
            {
                var models = await CreateApi(server).GetAllModelsRecursiveAsync();

                Assert.Equal("|#Архив|Model.rvt", Assert.Single(models).Path);
                Assert.Empty(server.Errors);
                Assert.Equal(new[] { rootContents, archiveContents }, server.Requests);
            }
        }

        [Fact]
        public async Task GetAllModelsRecursiveAsync_EmptyFolderContentsResponse_Throws()
        {
            var rootContents = "GET " + Service + "%7C/contents";
            var subfolderContents = "GET " + Service + "%7CF/contents";
            var json = new Dictionary<string, string>
            {
                [rootContents] = "{\"Folders\":[{\"Name\":\"F\"}],\"Models\":[{\"Name\":\"Root.rvt\"}]}",
                [subfolderContents] = "",
            };

            using (var server = StubHttpServer.WithJson(json))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(
                    () => CreateApi(server).GetAllModelsRecursiveAsync());

                Assert.Equal("Empty response for folder contents: |F", ex.Message);
                Assert.Empty(server.Errors);
                Assert.Equal(new[] { rootContents, subfolderContents }, server.Requests);
            }
        }

        [Theory]
        [InlineData(404, "Not Found", HttpStatusCode.NotFound)]
        [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
        public async Task HttpErrorResponse_ThrowsWithStatusCodeAndResponseContent(int status, string reason, HttpStatusCode expected)
        {
            const string body = "{\"Message\":\"Model |#Архив|Model.rvt not found\"}";
            using (var server = new StubHttpServer(_ => new StubResponse(status, reason, body)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(
                    () => CreateApi(server).GetModelHistoryAsync("|#Архив|Model.rvt"));

                Assert.Equal(expected, ex.StatusCode);
                Assert.Equal(body, ex.ResponseContent);
                Assert.Equal($"API request failed with status {expected}: {body}", ex.Message);
                Assert.IsType<WebException>(ex.InnerException);
            }
        }

        [Fact]
        public async Task NoHttpResponse_ThrowsWithoutStatusCode()
        {
            using (var server = new StubHttpServer(_ => null))
            {
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(
                    () => CreateApi(server).GetModelHistoryAsync("|A|m.rvt"));

                Assert.Null(ex.StatusCode);
                Assert.Null(ex.ResponseContent);
                Assert.IsType<WebException>(ex.InnerException);
                Assert.Equal("API request failed: " + ex.InnerException.Message, ex.Message);
                Assert.Contains("GET " + Service + "%7CA%7Cm.rvt/history", server.Requests);
            }
        }

        [Fact]
        public void ExistingConstructors_LeaveHttpDetailsEmpty()
        {
            var inner = new WebException("x");

            var withMessage = new RevitServerApiException("m");
            var withInner = new RevitServerApiException("m", inner);

            Assert.Null(withMessage.StatusCode);
            Assert.Null(withMessage.ResponseContent);
            Assert.Null(withInner.StatusCode);
            Assert.Null(withInner.ResponseContent);
            Assert.Same(inner, withInner.InnerException);
        }
    }
}
