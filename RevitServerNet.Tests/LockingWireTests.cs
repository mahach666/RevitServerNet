using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitServerNet.Extensions;
using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// Lock/copy/move result semantics and RevitServerApi request details. The response codes and bodies are the ones
    /// Revit Server 2022 and 2025 return (both identical): PUT lock, DELETE lock/inProgressLock and POST copy/move answer
    /// 200/201 with an EMPTY body, DELETE descendent/locks answers {"Path":..,"FailedItems":null}, failures are 404/409 with an empty body.
    /// </summary>
    public class LockingWireTests
    {
        private const string Service = "/RevitServerAdminRESTService2022/AdminRESTService.svc/";

        private static RevitServerApi CreateApi(StubHttpServer server) => new RevitServerApi(server.Host, "tester", false, "2022");

        private static StubHttpServer Answer(int status, string reason, string body = "") =>
            new StubHttpServer(_ => new StubResponse(status, reason, body));

        // ---- Lock / unlock ----

        [Fact]
        public async Task LockItemAsync_Http200EmptyBody_IsSuccess()
        {
            using (var server = Answer(200, "OK"))
            {
                var result = await CreateApi(server).LockItemAsync("|A|#m.rvt");

                Assert.True(result.Success);
                Assert.Equal("Item locked successfully", result.Message);
                Assert.Equal("PUT " + Service + "%7CA%7C%23m.rvt/lock", server.SingleRequest());
            }
        }

        [Fact]
        public async Task UnlockItemAsync_Http200EmptyBody_IsSuccess()
        {
            using (var server = Answer(200, "OK"))
            {
                var result = await CreateApi(server).UnlockItemAsync("|A|m.rvt");

                Assert.True(result.Success);
                Assert.Equal("DELETE " + Service + "%7CA%7Cm.rvt/lock?objectMustExist=true", server.SingleRequest());
            }
        }

        [Fact]
        public async Task UnlockItemAsync_ExplicitRoot_IsSent()
        {
            // The root can be locked by mistake (by an older version of the library): it must stay possible to unlock it.
            using (var server = Answer(200, "OK"))
            {
                var result = await CreateApi(server).UnlockItemAsync("|");

                Assert.True(result.Success);
                Assert.Equal("DELETE " + Service + "%7C/lock?objectMustExist=true", server.SingleRequest());
            }
        }

        [Fact]
        public async Task LockAndUnlock_Http404_Throw()
        {
            using (var server = Answer(404, "Not Found"))
            {
                var api = CreateApi(server);

                var lockEx = await Assert.ThrowsAsync<RevitServerApiException>(() => api.LockItemAsync("|A|nope.rvt"));
                var unlockEx = await Assert.ThrowsAsync<RevitServerApiException>(() => api.UnlockItemAsync("|A|nope.rvt"));

                Assert.Equal(HttpStatusCode.NotFound, lockEx.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, unlockEx.StatusCode);
            }
        }

        public static TheoryData<string> MissingPaths => new TheoryData<string> { null, "", " ", "\t" };

        [Theory]
        [MemberData(nameof(MissingPaths))]
        public async Task EveryItemMethod_MissingPath_ThrowsAndSendsNothing(string path)
        {
            using (var server = Answer(200, "OK"))
            {
                var api = CreateApi(server);

                await Assert.ThrowsAsync<ArgumentException>(() => api.LockItemAsync(path));
                await Assert.ThrowsAsync<ArgumentException>(() => api.UnlockItemAsync(path));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CancelLockAsync(path));
                await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteDescendentLocksAsync(path));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyItemAsync(path, "|B|m.rvt"));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyItemAsync("|A|m.rvt", path));
                await Assert.ThrowsAsync<ArgumentException>(() => api.MoveItemAsync(path, "|B|m.rvt"));
                await Assert.ThrowsAsync<ArgumentException>(() => api.MoveItemAsync("|A|m.rvt", path));

                Assert.Empty(server.Errors);
                Assert.Empty(server.Requests);
            }
        }

        [Theory]
        [InlineData("|")]
        [InlineData("/")]
        [InlineData("\\")]
        public async Task LockCopyMove_RootPath_ThrowsAndSendsNothing(string root)
        {
            using (var server = Answer(200, "OK"))
            {
                var api = CreateApi(server);

                await Assert.ThrowsAsync<ArgumentException>(() => api.LockItemAsync(root));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyItemAsync(root, "|B|m.rvt"));
                await Assert.ThrowsAsync<ArgumentException>(() => api.CopyItemAsync("|A|m.rvt", root));
                await Assert.ThrowsAsync<ArgumentException>(() => api.MoveItemAsync(root, "|B|m.rvt"));
                await Assert.ThrowsAsync<ArgumentException>(() => api.MoveItemAsync("|A|m.rvt", root));

                Assert.Empty(server.Requests);
            }
        }

        // ---- Cancel lock ----

        [Fact]
        public async Task CancelLockAsync_Http200EmptyBody_IsSuccess()
        {
            using (var server = Answer(200, "OK"))
            {
                var result = await CreateApi(server).CancelLockAsync("|A|m.rvt");

                Assert.True(result.Success);
                Assert.Equal("Lock cancelled", result.Message);
                Assert.Equal("DELETE " + Service + "%7CA%7Cm.rvt/inProgressLock", server.SingleRequest());
            }
        }

        [Fact]
        public async Task CancelLockAsync_Http404_IsFailureWithTheStatus()
        {
            using (var server = Answer(404, "Not Found"))
            {
                var result = await CreateApi(server).CancelLockAsync("|A|nope.rvt");

                Assert.False(result.Success);
                Assert.Contains("NotFound", result.Message);
            }
        }

        // ---- Descendent locks ----

        [Fact]
        public async Task DeleteDescendentLocksAsync_RealBodyWithNullFailedItems_IsSuccess()
        {
            using (var server = Answer(200, "OK", "{\"Path\":\"RSN_locking_core_a\",\"FailedItems\":null}"))
            {
                var result = await CreateApi(server).DeleteDescendentLocksAsync("|RSN_locking_core_a");

                Assert.True(result.Success);
                Assert.Equal("Descendent locks deleted", result.Message);
                Assert.Equal("DELETE " + Service + "%7CRSN_locking_core_a/descendent/locks", server.SingleRequest());
            }
        }

        [Theory]
        [InlineData("{\"Path\":\"A\",\"FailedItems\":[]}")]
        [InlineData("")]
        public async Task DeleteDescendentLocksAsync_NothingFailed_IsSuccess(string body)
        {
            using (var server = Answer(200, "OK", body))
                Assert.True((await CreateApi(server).DeleteDescendentLocksAsync("|A")).Success);
        }

        [Fact]
        public async Task DeleteDescendentLocksAsync_FailedItems_IsFailureNamingTheItems()
        {
            // The servers never returned a non-null FailedItems in the probes; the array of paths is the shape the field name implies.
            using (var server = Answer(200, "OK", "{\"Path\":\"A\",\"FailedItems\":[\"A\\\\m.rvt\",\"A\\\\n.rvt\"]}"))
            {
                var result = await CreateApi(server).DeleteDescendentLocksAsync("|A");

                Assert.False(result.Success);
                Assert.Contains("A\\m.rvt", result.Message);
                Assert.Contains("A\\n.rvt", result.Message);
            }
        }

        [Fact]
        public async Task DeleteDescendentLocksAsync_Http404_IsFailureWithTheStatus()
        {
            using (var server = Answer(404, "Not Found"))
            {
                var result = await CreateApi(server).DeleteDescendentLocksAsync("|A|nope");

                Assert.False(result.Success);
                Assert.Contains("NotFound", result.Message);
            }
        }

        // ---- Copy / move ----

        [Fact]
        public async Task CopyItemAsync_Http201EmptyBody_IsSuccess()
        {
            using (var server = Answer(201, "Created"))
            {
                var result = await CreateApi(server).CopyItemAsync("Test|YES_АР.rvt", "|RSN|#h|m.rvt");

                Assert.True(result.Success);
                Assert.Equal("Item copied successfully", result.Message);
                Assert.Equal(
                    "POST " + Service + "%7CTest%7CYES_%D0%90%D0%A0.rvt?destinationObjectPath=%7CRSN%7C%23h%7Cm.rvt&pasteAction=Copy&replaceExisting=false",
                    server.SingleRequest());
            }
        }

        [Fact]
        public async Task MoveItemAsync_Http201EmptyBody_IsSuccess_AndOverwriteIsSent()
        {
            using (var server = Answer(201, "Created"))
            {
                var result = await CreateApi(server).MoveItemAsync("|A|m.rvt", "|B|m2.rvt", overwrite: true);

                Assert.True(result.Success);
                Assert.Equal("Item moved successfully", result.Message);
                Assert.Equal(
                    "POST " + Service + "%7CA%7Cm.rvt?destinationObjectPath=%7CB%7Cm2.rvt&pasteAction=Move&replaceExisting=true",
                    server.SingleRequest());
            }
        }

        [Theory]
        [InlineData(409, "Conflict", "Conflict")]
        [InlineData(404, "Not Found", "NotFound")]
        public async Task CopyAndMove_HttpError_IsFailureWithTheStatus(int status, string reason, string expected)
        {
            using (var server = Answer(status, reason))
            {
                var api = CreateApi(server);

                var copy = await api.CopyItemAsync("|A|m.rvt", "|B|m.rvt");
                var move = await api.MoveItemAsync("|A|m.rvt", "|B|m.rvt");

                Assert.False(copy.Success);
                Assert.Contains("Failed to copy item", copy.Message);
                Assert.Contains(expected, copy.Message);
                Assert.False(move.Success);
                Assert.Contains("Failed to move item", move.Message);
                Assert.Contains(expected, move.Message);
            }
        }

        // Real directoryinfo answers of Revit Server 2022/2025 for an existing and a missing model
        private const string ModelInfoExists = "{\"Path\":\"B\\\\m.rvt\",\"DateCreated\":\"\\/Date(1794611306058)\\/\",\"DateModified\":\"\\/Date(1780844410000)\\/\",\"Exists\":true,\"FolderCount\":0,\"IsFolder\":false,\"LastModifiedBy\":\"Mahach\",\"LockContext\":null,\"LockState\":0,\"ModelCount\":1,\"ModelLocksInProgress\":null,\"ModelSize\":16747025,\"Size\":16793109}";
        private const string ModelInfoMissing = "{\"Path\":\"A\\\\m.rvt\",\"DateCreated\":null,\"DateModified\":null,\"Exists\":false,\"FolderCount\":0,\"IsFolder\":false,\"LastModifiedBy\":null,\"LockContext\":null,\"LockState\":0,\"ModelCount\":0,\"ModelLocksInProgress\":null,\"ModelSize\":0,\"Size\":0}";

        // POST answers 500 (what the server does for destination names like "Ка.rvt" after copying), directoryinfo answers per path
        private static StubHttpServer PasteAnswers500(bool sourceExists, bool destinationExists) =>
            new StubHttpServer(line =>
            {
                if (line.StartsWith("POST ")) return new StubResponse(500, "Internal Server Error", "");
                var exists = line.Contains("%7CB%7Cm.rvt/directoryinfo") ? destinationExists : sourceExists;
                return exists ? StubResponse.Ok(ModelInfoExists) : new StubResponse(404, "Not Found", ModelInfoMissing);
            });

        [Fact]
        public async Task CopyItemAsync_Http500ButDestinationCreated_IsSuccess()
        {
            using (var server = PasteAnswers500(sourceExists: true, destinationExists: true))
            {
                var result = await CreateApi(server).CopyItemAsync("|A|m.rvt", "|B|m.rvt");

                Assert.True(result.Success, result.Message);
                Assert.Contains("500", result.Message);
                Assert.Equal(new[]
                {
                    "POST " + Service + "%7CA%7Cm.rvt?destinationObjectPath=%7CB%7Cm.rvt&pasteAction=Copy&replaceExisting=false",
                    "GET " + Service + "%7CB%7Cm.rvt/directoryinfo",
                }, server.Requests);
            }
        }

        [Fact]
        public async Task CopyItemAsync_Http500AndNoDestination_IsFailure()
        {
            using (var server = PasteAnswers500(sourceExists: true, destinationExists: false))
            {
                var result = await CreateApi(server).CopyItemAsync("|A|m.rvt", "|B|m.rvt");

                Assert.False(result.Success);
                Assert.Contains("Failed to copy item", result.Message);
            }
        }

        [Fact]
        public async Task CopyItemAsync_WithOverwrite_Http500_IsFailure_WithoutGuessingFromTheDestination()
        {
            // With overwrite the destination may have existed before: its presence proves nothing
            using (var server = PasteAnswers500(sourceExists: true, destinationExists: true))
            {
                var result = await CreateApi(server).CopyItemAsync("|A|m.rvt", "|B|m.rvt", overwrite: true);

                Assert.False(result.Success);
                Assert.Single(server.Requests);
            }
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public async Task MoveItemAsync_Http500_IsSuccessOnlyWhenSourceGoneAndDestinationThere(bool sourceExists, bool destinationExists, bool success)
        {
            foreach (var overwrite in new[] { false, true })
            using (var server = PasteAnswers500(sourceExists, destinationExists))
            {
                var result = await CreateApi(server).MoveItemAsync("|A|m.rvt", "|B|m.rvt", overwrite);

                Assert.Equal(success, result.Success);
            }
        }

        // The operation request gets no HTTP answer (connection closed, as after a timeout); directoryinfo answers like the server
        private static StubHttpServer PasteWithoutAnswer(bool sourceExists, bool destinationExists) =>
            new StubHttpServer(line =>
            {
                if (line.StartsWith("POST ")) return null;
                var exists = line.Contains("%7CB%7Cm.rvt/directoryinfo") ? destinationExists : sourceExists;
                return exists ? StubResponse.Ok(ModelInfoExists) : new StubResponse(404, "Not Found", ModelInfoMissing);
            });

        [Fact]
        public async Task MoveItemAsync_NoAnswer_ButSourceGoneAndDestinationThere_IsSuccess()
        {
            using (var server = PasteWithoutAnswer(sourceExists: false, destinationExists: true))
            {
                var result = await CreateApi(server).MoveItemAsync("|A|m.rvt", "|B|m.rvt");

                Assert.True(result.Success, result.Message);
                Assert.Contains("no answer came from the server", result.Message);
            }
        }

        [Fact]
        public async Task CopyItemAsync_NoAnswer_IsFailure_WithoutGuessing()
        {
            // The server may still be copying into a destination that already exists: its presence proves nothing
            using (var server = PasteWithoutAnswer(sourceExists: true, destinationExists: true))
            {
                var result = await CreateApi(server).CopyItemAsync("|A|m.rvt", "|B|m.rvt");

                Assert.False(result.Success);
                Assert.Contains("Failed to copy item", result.Message);
                Assert.DoesNotContain(server.Requests, r => r.Contains("directoryinfo"));
            }
        }

        // ---- Request headers and body (RevitServerApi) ----

        // Records the whole request (head as Latin-1 so every byte stays visible, and the body) and answers 200 with an empty body.
        private sealed class CaptureServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly TaskCompletionSource<bool> _received = new TaskCompletionSource<bool>();

            public CaptureServer()
            {
                _listener.Start();
                Host = "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
                Task.Run(ServeAsync);
            }

            public string Host { get; }
            public string Head { get; private set; }
            public byte[] Body { get; private set; }

            public async Task WaitAsync()
            {
                if (await Task.WhenAny(_received.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false) != _received.Task)
                    throw new TimeoutException("No request received");
                await _received.Task.ConfigureAwait(false);
            }

            private async Task ServeAsync()
            {
                try
                {
                    using (var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false))
                    {
                        var stream = client.GetStream();
                        var latin1 = Encoding.GetEncoding(28591);
                        var received = new MemoryStream();
                        var buffer = new byte[4096];
                        var headEnd = -1;
                        var contentLength = 0;
                        while (true)
                        {
                            if (headEnd >= 0 && received.Length >= headEnd + 4 + contentLength)
                                break;
                            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                            if (read == 0)
                                throw new IOException("Connection closed before the request was received");
                            received.Write(buffer, 0, read);
                            if (headEnd < 0)
                            {
                                var text = latin1.GetString(received.ToArray());
                                headEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                if (headEnd >= 0)
                                {
                                    Head = text.Substring(0, headEnd);
                                    foreach (var line in Head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                            contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                                }
                            }
                        }
                        var all = received.ToArray();
                        Body = all.Skip(headEnd + 4).Take(contentLength).ToArray();

                        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
                        client.Client.Shutdown(SocketShutdown.Send);
                    }
                    _received.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _received.TrySetException(ex);
                }
            }

            public string Header(string name) =>
                Head.Split(new[] { "\r\n" }, StringSplitOptions.None)
                    .Where(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Substring(name.Length + 1).Trim())
                    .SingleOrDefault();

            public void Dispose() => _listener.Stop();
        }

        [Fact]
        public async Task EveryRequest_SendsUserNameMachineNameAndOperationGuid()
        {
            using (var server = new CaptureServer())
            {
                await new RevitServerApi(server.Host, "tester", false, "2022").GetAsync("serverproperties");
                await server.WaitAsync();

                Assert.Equal("tester", server.Header("User-Name"));
                Assert.Equal(Environment.MachineName, server.Header("User-Machine-Name"));
                Assert.True(Guid.TryParse(server.Header("Operation-GUID"), out _));
            }
        }

        [Theory]
        [InlineData("Иван Петров", "%D0%98%D0%B2%D0%B0%D0%BD%20%D0%9F%D0%B5%D1%82%D1%80%D0%BE%D0%B2")]
        [InlineData("José", "Jos%C3%A9")]
        public async Task NonAsciiUserName_IsSentPercentEncoded_InsteadOfThrowing(string userName, string expectedHeader)
        {
            // Live servers (2022 and 2025): raw UTF-8 in User-Name gets 400 "Missing or invalid User-Name", the percent-encoded form is accepted.
            using (var server = new CaptureServer())
            {
                await new RevitServerApi(server.Host, userName, false, "2022").GetAsync("serverproperties");
                await server.WaitAsync();

                Assert.Equal(expectedHeader, server.Header("User-Name"));
            }
        }

        [Fact]
        public async Task AdditionalHeaders_AreForwarded_AndNonAsciiValuesEncoded()
        {
            using (var server = new CaptureServer())
            {
                var headers = new Dictionary<string, string> { ["X-Plain"] = "v1", ["X-Cyr"] = "Ёж" };
                await new RevitServerApi(server.Host, "tester", false, "2022").GetAsync("serverproperties", headers);
                await server.WaitAsync();

                Assert.Equal("v1", server.Header("X-Plain"));
                Assert.Equal("%D0%81%D0%B6", server.Header("X-Cyr"));
            }
        }

        [Fact]
        public async Task PutWithData_SendsJsonContentTypeAndExactUtf8Bytes()
        {
            const string data = "{\"name\":\"Архив\"}";
            using (var server = new CaptureServer())
            {
                await new RevitServerApi(server.Host, "tester", false, "2022").PutAsync("cmd", data);
                await server.WaitAsync();

                Assert.StartsWith("PUT " + Service + "cmd ", server.Head);
                Assert.Equal("application/json", server.Header("Content-Type"));
                Assert.Equal(Encoding.UTF8.GetByteCount(data).ToString(), server.Header("Content-Length"));
                Assert.Equal(Encoding.UTF8.GetBytes(data), server.Body);
            }
        }

        [Theory]
        [InlineData("PUT")]
        [InlineData("POST")]
        public async Task PutAndPostWithoutData_SendContentLengthZero(string method)
        {
            using (var server = new CaptureServer())
            {
                var api = new RevitServerApi(server.Host, "tester", false, "2022");
                await (method == "PUT" ? api.PutAsync("cmd") : api.PostAsync("cmd"));
                await server.WaitAsync();

                Assert.Equal("0", server.Header("Content-Length"));
                Assert.Null(server.Header("Content-Type"));
                Assert.Empty(server.Body);
            }
        }

        // ---- Timeout and cancellation ----

        [Fact]
        public async Task Timeout_AbortsAStalledRequest_WithAClearException()
        {
            using (var release = new ManualResetEventSlim())
            using (var server = new StubHttpServer(_ => { release.Wait(TimeSpan.FromSeconds(30)); return null; }))
            {
                var api = CreateApi(server);
                api.Timeout = TimeSpan.FromMilliseconds(500);

                var started = DateTime.UtcNow;
                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => api.GetAsync("serverproperties"));
                release.Set();

                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
                Assert.Contains("timed out", ex.Message);
                Assert.Null(ex.StatusCode);
            }
        }

        [Fact]
        public async Task CancellationToken_AbortsAStalledRequest()
        {
            using (var release = new ManualResetEventSlim())
            using (var server = new StubHttpServer(_ => { release.Wait(TimeSpan.FromSeconds(30)); return null; }))
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                var api = CreateApi(server);

                var started = DateTime.UtcNow;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetAsync("serverproperties", null, cts.Token));
                release.Set();

                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public async Task CancellationToken_AlreadyCancelled_SendsNothingUseful()
        {
            using (var server = Answer(200, "OK"))
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateApi(server).PutAsync("cmd", null, null, cts.Token));
                Assert.Empty(server.Requests);
            }
        }

        [Fact]
        public void Timeout_DefaultsToThePlatformBehaviour_AndRejectsNonPositiveValues()
        {
            var api = new RevitServerApi("localhost", "tester", false, "2022");

            // null: no limit set by the library (as before the property existed); a set value is enforced on every target
            Assert.Null(api.Timeout);
            Assert.Throws<ArgumentOutOfRangeException>(() => api.Timeout = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => api.Timeout = TimeSpan.FromSeconds(-5));
            Assert.Throws<ArgumentOutOfRangeException>(() => api.Timeout = TimeSpan.FromDays(30)); // above CancelAfter's limit
            api.Timeout = TimeSpan.FromMilliseconds(int.MaxValue);
            api.Timeout = Timeout.InfiniteTimeSpan;
            Assert.Equal(Timeout.InfiniteTimeSpan, api.Timeout);
            api.Timeout = null;
            Assert.Null(api.Timeout);
        }

        [Fact]
        public async Task Timeout_DoesNotHideAnHttpErrorResponse()
        {
            using (var server = Answer(404, "Not Found", "x"))
            {
                var api = CreateApi(server);
                api.Timeout = TimeSpan.FromSeconds(30);

                var ex = await Assert.ThrowsAsync<RevitServerApiException>(() => api.GetAsync("serverproperties"));

                Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            }
        }

        // ---- Constructor ----

        [Theory]
        [InlineData(null, "tester", "2022")]
        [InlineData("", "tester", "2022")]
        [InlineData("localhost", null, "2022")]
        [InlineData("localhost", "", "2022")]
        [InlineData("localhost", "tester", null)]
        [InlineData("localhost", "tester", " ")]
        [InlineData("localhost", "tester", "abc")]
        [InlineData("localhost", "tester", "2011")]
        public void Constructor_InvalidArguments_Throw(string host, string user, string version)
        {
            Assert.Throws<ArgumentException>(() => new RevitServerApi(host, user, false, version));
        }

        [Theory]
        [InlineData(false, "2012", "http://h/RevitServerAdminRESTService/AdminRESTService.svc")]
        [InlineData(false, "2013", "http://h/RevitServerAdminRESTService2013/AdminRESTService.svc")]
        [InlineData(true, "2025", "https://h/RevitServerAdminRESTService2025/AdminRESTService.svc")]
        public void Constructor_BuildsTheBaseUrl(bool https, string version, string expected)
        {
            Assert.Equal(expected, new RevitServerApi("h", "tester", https, version).BaseUrl);
        }
    }
}
