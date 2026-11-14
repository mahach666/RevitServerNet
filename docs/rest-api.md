---
title: REST API
---

# REST API

`RevitServerApi` and its extension methods (namespace `RevitServerNet.Extensions`) talk to the Revit Server REST service.

## Usage examples

### Folder Operations

```csharp
// List sub-folders of a folder (List<FolderInfo>; FolderInfo.Path is filled as "|Projects|<name>")
var folders = await api.ListFoldersAsync("|Projects");

// Or everything in the folder: Folders, Models, Files, DriveSpace, DriveFreeSpace
FolderContents contents = await api.GetFolderContentsAsync("|Projects");

// Create a new folder: parent folder path + new folder name (HTTP errors are thrown as RevitServerApiException)
await api.CreateFolderAsync("|Projects", "NewProject");

// Delete a folder with all its contents (HTTP failures are returned in the result, not thrown)
var deleted = await api.DeleteFolderAsync("|Projects|OldProject");
if (!deleted.Success) Console.WriteLine(deleted.Message);

// Rename: newName must be a non-empty name (the server's rename with an empty name deletes, so the library refuses it)
var renamed = await api.RenameFolderAsync("|Projects|NewProject", "Project2");
```

### Project Operations

```csharp
// Get Project Information parameters of a model (pass the .rvt model path, not a folder)
var projectInfo = await api.GetProjectInfoAsync("|Projects|MyProject|file.rvt"); // List<ProjParameter>
foreach (var p in projectInfo)
    Console.WriteLine($"{p.Category}: {p.Name} = {p.Value}");

// Get model history (per model file: GET {modelPath}/history)
ModelHistory history = await api.GetModelHistoryAsync("|Projects|MyProject|file.rvt");
foreach (var h in history.Items)
    Console.WriteLine($"{h.Version} {h.User} {h.DateUtc} {h.Comment}"); // Date is the raw "/Date(ms)/" string, DateUtc the parsed value

// Client-side filtering/paging (the REST endpoint has no paging); newest versions first
var last10 = await api.GetModelHistoryAsync("|Projects|MyProject|file.rvt", take: 10);
```

### Locking Operations

```csharp
// Lock a model or folder (PUT {path}/lock); returns OperationResult
var locked = await api.LockItemAsync("|Projects|MyProject|file.rvt");

// Unlock a model (or folder)
OperationResult unlocked = await api.UnlockItemAsync("|Projects|MyProject|file.rvt");

// Lock state of a model or folder (GET {path}/directoryinfo); null when unlocked
LockInfo lockInfo = await api.GetModelLockAsync("|Projects|MyProject|file.rvt");
Console.WriteLine(lockInfo?.LockState);

bool isLocked = await api.IsModelLockedAsync("|Projects|MyProject|file.rvt");
// The REST service names the owner only for a lock in progress; otherwise null
string lockedBy = await api.GetModelLockUserAsync("|Projects|MyProject|file.rvt");
```

## API Reference

### RevitServerApi Class

Main class for interacting with Revit Server REST API.

#### Constructor

```csharp
public RevitServerApi(string host, string userName, bool useHttps = false, string serverVersion = "2019")
```

#### Properties

- `BaseUrl`: Gets the base URL for API requests
- `UserName`: Gets the user name used for API requests. A name with non-ASCII characters (for example a Cyrillic Windows account) is sent percent-encoded in the `User-Name` header, as are a non-ASCII machine name and `additionalHeaders` values: Revit Server 2022 and 2025 answer raw UTF-8 header values with HTTP 400.
- `Timeout` (`TimeSpan?`): time limit of one request, from sending it to reading the whole response; `Timeout.InfiniteTimeSpan` means no limit, at most `int.MaxValue` milliseconds. A request that exceeds it is aborted and throws `RevitServerApiException` ("API request timed out after ..."). `null` (the default) keeps the behaviour of earlier versions: on .NET 6/8 `HttpWebRequest` ends a request after 100 s, on .NET Framework the requests have no limit. Long server operations (a copy or move of a large model, a delete of a big folder) can take longer than 100 s.

#### Methods

- `GetAsync(string command, Dictionary<string, string> additionalHeaders = null)`: Performs GET request
- `PostAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)`: Performs POST request
- `PutAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)`: Performs PUT request
- `DeleteAsync(string command, Dictionary<string, string> additionalHeaders = null)`: Performs DELETE request
- Overloads of the four methods with a trailing `CancellationToken`: a cancelled token aborts the request and throws `OperationCanceledException`.
- `static string EncodePath(string path)`: Builds the path part of a command. It turns `\` and `/` into `|`, collapses `||`, adds a leading `|` and percent-encodes every folder/model name with `Uri.EscapeDataString` (the `|` separators stay as is). A null or empty path gives `|` (the root). Example: `|#Archive|Model.rvt` becomes `|%23Archive|Model.rvt`.

### Extension Methods

The library provides extension methods for common operations. The extension methods are in the `RevitServerNet.Extensions` namespace, and result types (`FolderContents`, `ModelHistory`, `OperationResult`, ...) are in `RevitServerNet.Models`:
```csharp
using RevitServerNet.Extensions;
using RevitServerNet.Models;
```

All extension methods return `Task<T>`; `-> T` below shows the awaited result type.

#### ServerExtensions
- `GetServerInfoAsync(this RevitServerApi api)` -> `ServerInfo`
- `PingServerAsync(this RevitServerApi api)` -> `bool` (returns `false` on any error)
- `IsServerRunningAsync(this RevitServerApi api)` -> `bool` (returns `false` on any error or empty server properties)
- `GetServerVersionAsync(this RevitServerApi api)` -> `string`: the server reports no version, so this is the year of the server version the client was created with (`"2012"` for the unversioned path), returned after a request that shows the server answers
- `GetServerRolesAsync(this RevitServerApi api)` -> `List<ServerRole>`
- `GetMaximumModelNameLengthAsync(this RevitServerApi api)` -> `int`
- `GetServerDriveInfoAsync(this RevitServerApi api)` -> `(long DriveSpace, long DriveFreeSpace)`

#### FolderExtensions
- `GetRootFolderContentsAsync(this RevitServerApi api)` / `GetFolderContentsAsync(this RevitServerApi api, string folderPath)` -> `FolderContents`
- `GetFolderInfoAsync(this RevitServerApi api, string folderPath)` -> `FolderInfo`; `GetFolderSizeAsync(this RevitServerApi api, string folderPath)` -> `long`
- `GetModelInfoAsync(this RevitServerApi api, string modelPath)` -> `ModelInfo`
- `FolderExistsAsync(this RevitServerApi api, string folderPath)` / `ModelExistsAsync(this RevitServerApi api, string modelPath)` -> `bool`: `false` only when the server says the item does not exist (404; `FolderExistsAsync` also for a model path); other errors are thrown
- `CreateFolderAsync(this RevitServerApi api, string parentFolderPath, string folderName)`, `DeleteFolderAsync(this RevitServerApi api, string folderPath)`, `RenameFolderAsync(this RevitServerApi api, string folderPath, string newName)` -> `OperationResult`. Delete removes the folder (or model) with all its contents; the root is never deleted or renamed. `RenameFolderAsync` throws `ArgumentException` for a null, empty or whitespace `newName` or one with `|`, `/` or `\` (the server's rename with an empty name is a delete), and `CreateFolderAsync` for an empty `folderName` or one with a separator, before any request. `CreateFolderAsync` is `Success` also when the folder already exists (the server answers 200). For some names (for example "Кир") the server answers 500 after it created, renamed or deleted the item, and after a timeout the outcome is open too; these methods then check the outcome and report success when the item is in the expected state.
- `ListFilesAsync(this RevitServerApi api, string folderPath = "|")` -> `List<RevitFileInfo>`; `ListFoldersAsync(this RevitServerApi api, string folderPath = "|")` -> `List<FolderInfo>`; `ListModelsAsync(this RevitServerApi api, string folderPath = "|")` -> `List<ModelInfo>`
- `GetAllModelsRecursiveAsync(this RevitServerApi api, string folderPath = "|")` -> `List<ModelInfo>` (throws `RevitServerApiException` when the `contents` response of any visited folder has an empty body); `GetAllFoldersRecursiveAsync(this RevitServerApi api, string folderPath = "|")` -> `List<FolderInfo>`
- `WalkAsync(this RevitServerApi api, string topPath = "|", bool includeFiles = true, bool includeModels = true, bool digModels = false)` -> `WalkResult` (a folder that cannot be read throws `RevitServerApiException`; with `digModels` the internal files and folders of each model are listed after the model)
- `CreateLocalModelWithoutToolAsync(this RevitServerApi api, string modelPipePath, string destinationFile, string revitVersion = null, string assembliesPath = null, bool overwrite = false)` -> `string` (path of the created file)
- `CreateLocalModelWithoutToolAsync(this RevitServerApi api, string modelPipePath, string destinationFile, bool createLocal, string revitVersion = null, string assembliesPath = null, bool overwrite = false, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)` -> `string`

#### ProjectExtensions
- `GetProjectInfoAsync(this RevitServerApi api, string modelPath)` -> `List<ProjParameter>`. The server localizes the parameter type: `DataType` maps the English names and the Russian "Текст" and "Многострочный текст", and is `Unknown` for others; `TypeOfParameter` has the text as the server sent it. A parameter without a display name gets the JSON property name as `Name`. An empty or non-array response throws `RevitServerApiException`.

#### HistoryExtensions
- `GetModelHistoryAsync(this RevitServerApi api, string modelPath)` -> `ModelHistory`
- `GetModelHistoryAsync(this RevitServerApi api, string modelPath, int? take = null, int? skip = null, string userFilter = null, int? minVersion = null, int? maxVersion = null, DateTime? fromDate = null, DateTime? toDate = null)` -> `ModelHistory` (filtered, ordered by version descending and paged on the client, because the REST history endpoint has no paging)
- `GetLatestVersionAsync(api, string modelPath)`, `GetVersionAsync(api, string modelPath, int version)` -> `HistoryItem`
- `GetLatestVersionsAsync(api, string modelPath, int count)`, `GetModelHistoryPageAsync(api, string modelPath, int skip, int take)`, `GetVersionsByUserAsync(api, string modelPath, string userName)`, `GetVersionsByUserAsync(api, string modelPath, string userName, int? take)` -> `List<HistoryItem>`
- `GetVersionCountAsync(api, string modelPath)` -> `int`, `GetTotalModelSizeAsync(api, string modelPath)` -> `long`, `GetLastVersionCommentAsync(api, string modelPath)` -> `string`
- History items: `Version` (`VersionNumber`), `Size` (`ModelSize`), `SupportSize`, `OverwrittenByHistoryNumber`, `User`, `Comment`, `Date` (raw `/Date(ms)/`) and `DateUtc`. The `fromDate`/`toDate` filters compare `DateUtc` with the bounds converted to UTC, both inclusive. `take: 0` gives an empty page, negative `skip`/`take` count as 0.
- Lock queries (the REST service has no lock endpoint for reading; these use `directoryinfo` and `|/descendent/locks`): `GetLocksAsync(api)` -> `LocksList` (locks below the root), `GetModelLockAsync(api, string modelPath)` -> `LockInfo` (null when unlocked; `LockState` is `LockedChild` for a folder with a locked item below it), `IsModelLockedAsync(api, string modelPath)` -> `bool`, `GetModelLockUserAsync(api, string modelPath)` -> `string` (the server names the owner only for a lock in progress, otherwise null), `GetActiveLocksCountAsync(api)` -> `int`. A missing model or folder throws `RevitServerApiException` with `StatusCode` 404, other errors are thrown too. `GetLocksByUserAsync(api, string userName)` throws `NotSupportedException`: the server does not report who owns a lock.

#### LockingExtensions
- `LockItemAsync(this RevitServerApi api, string itemPath)` / `UnlockItemAsync(this RevitServerApi api, string itemPath)` -> `OperationResult` (a `RevitServerApiException` from the HTTP call is propagated)
- `CancelLockAsync(this RevitServerApi api, string itemPath)` -> `OperationResult`
- `GetDescendentLocksAsync(this RevitServerApi api, string folderPath)` -> `string` (raw JSON response; on failure the exception text is returned instead of thrown)
- `DeleteDescendentLocksAsync(this RevitServerApi api, string folderPath)` -> `OperationResult`
- `CopyItemAsync` / `MoveItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)` -> `OperationResult`

The server answers these operations with 2xx and an empty body on success, so `Success = true` means the server accepted the operation. `CancelLockAsync`, `DeleteDescendentLocksAsync`, `CopyItemAsync` and `MoveItemAsync` turn a `RevitServerApiException` into `Success = false` with the error in `Message`; other exceptions propagate. `DeleteDescendentLocksAsync` is `Success = false` when the server lists `FailedItems`. A null, empty or whitespace path throws `ArgumentException` before any request (before, it meant the server root); `LockItemAsync`, `CopyItemAsync` and `MoveItemAsync` also refuse the root `|`. For some destination names (for example "Ка.rvt") the server answers 500 after it copied or moved the item: a move is then reported as done when the source is gone and the destination exists (also after a timeout), and a copy without `overwrite` when the destination exists (a copy onto an existing item without `overwrite` is answered 409, so the item is new). A copy with `overwrite` that gets a 500, and a copy that gets no answer (the server may still be copying), stay failures.

Lock information is in `HistoryExtensions`: `GetModelLockAsync(this RevitServerApi api, string modelPath)` -> `LockInfo`, plus `GetLocksAsync`, `IsModelLockedAsync`, `GetModelLockUserAsync`, `GetLocksByUserAsync` and `GetActiveLocksCountAsync`.

#### ExportExtensions
- `ExportModelAsync(this RevitServerApi api, string modelPath, string destinationFile, string assembliesPath = null, bool overwrite = false, IProgress<long> bytesProgress = null)` -> `string` (path of the created file)
- `ExportModelAsync(this RevitServerApi api, string modelPath, string destinationFile, bool createLocal, string assembliesPath = null, bool overwrite = false, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)` -> `string`
