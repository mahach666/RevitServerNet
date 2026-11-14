---
title: Error handling
---

# Error handling

`RevitServerApi.GetAsync`/`PostAsync`/`PutAsync`/`DeleteAsync` throw `RevitServerApiException` when the server returns an HTTP error response or no response at all. Exception: on .NET Framework 4.8, `PostAsync`/`PutAsync` called with a request body (`data`) throw a plain `System.Net.WebException` if the connection fails while the body is being sent (the same applies to `RevitServerUiApi.PostAsync`/`PutAsync`). The extension methods never send a body and are not affected. Extension methods pass it through unless they are listed below. That includes the data getters (for example `GetFolderContentsAsync`, `GetModelHistoryAsync`, `GetProjectInfoAsync`) and `CreateFolderAsync`, `LockItemAsync` and `UnlockItemAsync`. `GetAllModelsRecursiveAsync` also throws `RevitServerApiException` when a folder's contents response has an empty body.

These extension methods do not throw for a server error:
- `DeleteFolderAsync`, `RenameFolderAsync`, `CancelLockAsync`, `DeleteDescendentLocksAsync`, `CopyItemAsync` and `MoveItemAsync` turn a `RevitServerApiException` into an `OperationResult` with `Success = false` and the error in `Message`. Argument errors (`ArgumentException`) and cancellation propagate.
- `FolderExistsAsync` and `ModelExistsAsync` return `false` for a 404 and throw other errors.
- `PingServerAsync` and `IsServerRunningAsync` return `false` on any error.
- `GetDescendentLocksAsync` returns the exception text (`ex.ToString()`) as its result.

`IsModelLockedAsync`, the other lock queries and `WalkAsync` throw (before 1.4.0 they returned `false` or skipped unreadable folders). A request that gets no HTTP response throws `RevitServerApiException` with the cause in the message ("API request failed: ...") and as `InnerException`.

In the methods that do not catch, a response body that is not valid JSON raises a Newtonsoft.Json exception, not `RevitServerApiException`.

```csharp
using System;
using System.Net;
using RevitServerNet;
using RevitServerNet.Extensions;

try
{
    var contents = await api.GetFolderContentsAsync("|Projects");
}
catch (RevitServerApiException ex)
{
    Console.WriteLine($"API Error: {ex.Message}");
    // HTTP status and body of the error response; null when the exception was not caused by an HTTP error response (network failure, or an error raised by the library itself)
    if (ex.StatusCode == HttpStatusCode.NotFound)
        Console.WriteLine($"Not found: {ex.ResponseContent}");
}
```

`RevitServerUiApiException` exposes the same `StatusCode` and `ResponseContent` properties.

Extension methods take raw folder and model names (for example `|#Archive|Model.rvt`) and encode every name with `RevitServerApi.EncodePath`. Do not escape names yourself, or they will be escaped twice (`%23` becomes `%2523`). `GetAsync`/`PostAsync`/`PutAsync`/`DeleteAsync` send `command` as is, so when you call them directly, build the path with `RevitServerApi.EncodePath`, e.g. `await api.GetAsync(RevitServerApi.EncodePath("|#Archive|Model.rvt") + "/modelinfo");`. Escape query-string values with `Uri.EscapeDataString`.
