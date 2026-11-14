using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace RevitServerNet.Tests
{
    public class RevitServerUiApiWireTests
    {
        private static RevitServerUiApi CreateApi(StubHttpServer server)
        {
            return new RevitServerUiApi(server.Host, false, "2022");
        }

        [Fact]
        public async Task HttpErrorResponse_ThrowsWithStatusCodeAndResponseContent()
        {
            const string body = "Model |#Архив|Model.rvt not found";
            using (var server = new StubHttpServer(_ => new StubResponse(404, "Not Found", body)))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(
                    () => CreateApi(server).GetModelDetailsAsync("|#Архив|Model.rvt"));

                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
                Assert.Equal(body, ex.ResponseContent);
                Assert.Equal($"UI API request failed with status NotFound: {body}", ex.Message);
                Assert.IsType<WebException>(ex.InnerException);
                Assert.Equal(
                    "GET /RevitServerAdmin2022/api/model/details?id=%7C%23%D0%90%D1%80%D1%85%D0%B8%D0%B2%7CModel.rvt",
                    server.SingleRequest());
            }
        }

        [Fact]
        public async Task NoHttpResponse_ThrowsWithoutStatusCode()
        {
            using (var server = new StubHttpServer(_ => null))
            {
                var ex = await Assert.ThrowsAsync<RevitServerUiApiException>(
                    () => CreateApi(server).GetModelDetailsAsync("|A|m.rvt"));

                Assert.Null(ex.StatusCode);
                Assert.Null(ex.ResponseContent);
                Assert.IsType<WebException>(ex.InnerException);
                Assert.Equal("UI API request failed: " + ex.InnerException.Message, ex.Message);
            }
        }

        [Fact]
        public void ExistingConstructors_LeaveHttpDetailsEmpty()
        {
            var inner = new WebException("x");

            var withMessage = new RevitServerUiApiException("m");
            var withInner = new RevitServerUiApiException("m", inner);

            Assert.Null(withMessage.StatusCode);
            Assert.Null(withMessage.ResponseContent);
            Assert.Null(withInner.StatusCode);
            Assert.Null(withInner.ResponseContent);
            Assert.Same(inner, withInner.InnerException);
        }
    }
}
