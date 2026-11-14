---
title: Admin UI API
---

# Admin UI API

Client for the Revit Server Admin UI API (the endpoints used by the RevitServerAdmin web UI). Requests go to `http(s)://{host}/RevitServerAdmin{YEAR}/api/...` (`RevitServerAdmin` for 2012) and authenticate with the current Windows credentials. The class takes no user name.

```csharp
public RevitServerUiApi(string host, bool useHttps = false, string serverVersion = "2022")
```

`serverVersion` must be a year >= 2012. `BaseUrl` is `http(s)://{host}/RevitServerAdmin{YEAR}`, with no `/api` suffix.

Items are addressed by `id`: a backslash path that starts with the server host, for example `myserver\Projects\Model.rvt`. The id `myserver` alone is the server root. The id is sent to the server as is (it is not converted from pipe format).

- Raw (return the response body as `string`; `apiPath` is relative to `BaseUrl`, e.g. `"api/model/details"`): `GetAsync/DeleteAsync(string apiPath, Dictionary<string,string> query = null)`, `PostAsync/PutAsync(string apiPath, Dictionary<string,string> query = null, string data = null)`
- `GetModelDetailsAsync(string id)` -> `UiModelDetails`; `GetItemLockDataAsync(string id, string placeholder = "")` -> `UiItemLockData`; `GetModelHistoriesAsync(string id, string type = "rs-model")` -> `List<UiModelHistoryItem>`; `GetSubItemsAsync(string id, int depth = 1)` -> `UiTreeItem` (models in `RevitServerNet.Models`)
- `DeleteOrRenameAsync(string id, string newName = null)` (deletes when `newName` is null, renames otherwise; an empty or whitespace `newName` throws `ArgumentException`, because the server deletes on it), `DeleteItemAsync(string id)`, `RenameAsync(string id, string newName)`, `CopyOrMoveAsync(string id, string destinationId, RevitServerUiApi.PasteAction pasteAction, bool replaceExisting = false)` -> `string` (URL), `LockAsync(string id)`, `UnlockAsync(string id, bool itemMustExist = true)`, `CreateFolderAsync(string id)` -> `string` (URL)

HTTP errors and failed requests are thrown as `RevitServerUiApiException`, which has the same `StatusCode`/`ResponseContent` properties as `RevitServerApiException`. The server also reports some failures with HTTP 200 and an error descriptor (`StatusCode`, `TitleText`, `MessageText`, `DetailText`), for example a lock of an item that is already locked ("Cannot Lock Model"); every call, the raw ones too, throws `RevitServerUiApiException` with that text for such a body. Invalid arguments (for example an empty `id`) throw `ArgumentException`/`ArgumentOutOfRangeException`, and the typed methods raise a Newtonsoft.Json exception when the response body is not valid JSON.
