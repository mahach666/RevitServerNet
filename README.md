# RevitServerNet

A .NET library for working with Revit Server REST API. Provides easy access to Revit Server operations including folder management, project operations, locking, and history tracking.

## Features

- **Server Management**: Get server information and status
- **Folder Operations**: Create, delete, and manage folders on Revit Server
- **Project Information**: Read a model's project information parameters
- **Locking**: Lock and unlock models and folders, cancel in-progress locks, list or delete descendent locks, and query server-wide and per-model locks
- **History Tracking**: Access project history and version information
- **Copy / Move**: Copy and move items on the server (`CopyItemAsync`, `MoveItemAsync`)
- **Recursive traversal**: `WalkAsync`, `GetAllModelsRecursiveAsync`, `GetAllFoldersRecursiveAsync`
- **Admin UI API**: `RevitServerUiApi` for `RevitServerAdmin{YEAR}/api/...` (model details, lock data, histories, tree, rename/delete, copy/move, lock/unlock, create folder)
- **Direct Export**: Export Revit Server models to RVT files over the Revit Server ModelService (net.tcp), without RevitServerTool.exe (net48/net6/net8)
- **RevitServerTool wrapper**: `RevitServerToolClient.CreateLocalModelAsync` for exporting through RevitServerTool.exe
- **Multi-Version Support**: REST and Admin UI clients for Revit Server 2012 and later; direct export for any server year that exposes `/ModelService{year}/`, with one bundled set of Autodesk client assemblies (verified live against Revit Server 2022-2025)
- **Async Operations**: All API calls are asynchronous for better performance

## Installation

Via Package Manager Console:
```powershell
Install-Package RevitServerNet
```

Or via NuGet Package Manager:
```
RevitServerNet
```

Or via .NET CLI:
```bash
dotnet add package RevitServerNet
```

## Quick Start

```csharp
using RevitServerNet;
using RevitServerNet.Extensions; // GetServerInfoAsync, ListFoldersAsync, GetFolderContentsAsync are extension methods

// Initialize API client
// serverVersion defaults to "2019": set it to your Revit Server year
var api = new RevitServerApi("your-server-host", "your-username", serverVersion: "2024");

// Get server information
var serverInfo = await api.GetServerInfoAsync();

// List subfolders of a folder (List<FolderInfo>, Path is filled in)
var folders = await api.ListFoldersAsync("|path|to|folder");

// Or everything in the folder (Folders, Models, Files)
var contents = await api.GetFolderContentsAsync("|path|to|folder");
```

## Supported Revit Server Versions

- REST API (`RevitServerApi`) and Admin UI API (`RevitServerUiApi`): `serverVersion` must be a numeric year >= 2012; no upper bound is enforced. 2012 maps to `/RevitServerAdminRESTService/AdminRESTService.svc` (REST) and `/RevitServerAdmin` (UI); 2013 and later map to `/RevitServerAdminRESTService{YEAR}/AdminRESTService.svc` and `/RevitServerAdmin{YEAR}`. A year below 2012 or a non-numeric value throws `ArgumentException`.
- Direct export: any Revit Server year whose server exposes `net.tcp://{host}/ModelService{year}/ModelService.svc/`. The year is only a part of that address; the same bundled client assemblies are used for every year. Verified live on Revit Server 2022, 2023, 2024 and 2025 (a docker test stand; net48 and net8.0). All four servers served the same test models saved in Revit 2022 format (copied unchanged onto the 2023-2025 servers), so models saved by Revit 2023 or later were not part of these checks. With `CreateLocal = true`, `BasicFileInfo` matched the output of the official RevitServerTool 2022 and 2024. Revit Server 2026 and 2027 are expected to work through the same endpoint pattern but have not been verified against a live server; Revit Server 2021 and earlier have not been verified live either. See [Direct export from Revit Server](#direct-export-from-revit-server-rs-enterprise-api).
- `api.ExportModelAsync(...)` reads the version from `BaseUrl`. On a client created with `serverVersion: "2012"` it cannot find a year and uses "2019". For such a server, call `ModelExporter.ExportAsync` with `RevitVersion` set explicitly.

## Requirements

- .NET Framework 4.8, .NET 6 or .NET 8 (package targets net48, net6.0, net8.0)
- Dependencies (installed automatically): Newtonsoft.Json 13.0.3, System.Security.Permissions 9.0.9; on net6.0/net8.0 also System.ServiceModel.Primitives and System.ServiceModel.NetTcp 4.9.0; on net48 the framework assembly System.ServiceModel. On net6.0, System.Security.Permissions 9.0.9 emits a 'not supported on net6.0' build warning.
- Direct export only: Windows, network access to the Revit Server's net.tcp port (808 by default), and the Autodesk client assemblies (bundled, copied to `<output>\RSAssemblies\` on build and to `<publish>\RSAssemblies\` on publish; with `PublishSingleFile` they stay files in `<publish>\RSAssemblies\` next to the executable). The `%TEMP%` volume must hold the downloaded model data and the assembled RVT file together, about twice the model size. A 64-bit process is not required for the bundled set: its assemblies are AnyCPU (a 32-bit .NET Framework 4.8 process was checked). Other sets can be x64-only, see [Bundled Autodesk client assemblies](#bundled-autodesk-client-assemblies).

## Usage Examples

### Basic Setup

```csharp
using System;
using RevitServerNet;
using RevitServerNet.Extensions;   // all operations below are extension methods
using RevitServerNet.Models;       // ServerInfo, FolderInfo, OperationResult, ... (only needed when you name the types)

// Create API client
var api = new RevitServerApi(
    host: "your-revit-server.com",
    userName: "your-username",
    useHttps: true,        // Optional, default: false
    serverVersion: "2024"  // Optional, default: "2019"; must be a year >= 2012
);
```

### Server Operations

```csharp
// Get server information (GET serverproperties)
ServerInfo serverInfo = await api.GetServerInfoAsync();
// The server reports no name or version: Version is the year this client was created with, Name stays null
Console.WriteLine($"{serverInfo?.MachineName} {serverInfo?.Version}");

// Check that the server responds and returns non-empty properties.
// Never throws: any exception yields false.
bool isRunning = await api.IsServerRunningAsync();
```

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

## Direct export from Revit Server (RS Enterprise API)

RevitServerNet exports a model from Revit Server to a local RVT file without `RevitServerTool.exe`. It talks to the server's ModelService over net.tcp through its own WCF channel and uses the Autodesk Revit Server client assemblies for the data contracts and to assemble the RVT file. Works on .NET Framework 4.8, .NET 6 and .NET 8 on Windows.

### Minimal example (direct call)
```csharp
using System;
using RevitServerNet;

var options = new ModelExporterOptions
{
    ServerHost = "revit-server.local",                 // host or host:port of the net.tcp endpoint (default port 808)
    ModelPipePath = "|Projects|Demo|Model.rvt",          // pipe or Windows-style path
    DestinationFile = @"C:\Exports\Demo.rvt",
    RevitVersion = "2024",                               // four-digit year of the Revit Server
    Overwrite = true,
    CreateLocal = false                                  // true: BasicFileInfo of a local copy, as RevitServerTool writes it
};

var resultPath = await ModelExporter.ExportAsync(
    options,
    // value is per downloaded model-data file, it restarts from 0 for every file
    new Progress<long>(bytes => Console.WriteLine($"Current file: {bytes} bytes")),
    cancellationToken);                                  // optional CancellationToken
Console.WriteLine($"Exported to: {resultPath}");
```

### Using `RevitServerApi` extension (infers host/version)
```csharp
using RevitServerNet;
using RevitServerNet.Extensions;

var api = new RevitServerApi("revit-server.local", "user", serverVersion: "2024");
await api.ExportModelAsync(
    modelPath: "|Projects|Demo|Model.rvt",
    destinationFile: @"C:\Exports\Demo.rvt",
    overwrite: true
);

// Overload with CreateLocal and cancellation
await api.ExportModelAsync("|Projects|Demo|Model.rvt", @"C:\Exports\Demo.rvt", createLocal: true, overwrite: true, cancellationToken: cancellationToken);
```
Notes:
- The extension takes the host from `api.BaseUrl` without the HTTP port (`new Uri(BaseUrl).Host`; before 1.3.0 the HTTP port ended up in the net.tcp address), so the ModelService must listen on the default net.tcp port (808) of that host. For another port, call `ModelExporter.ExportAsync` with `ServerHost = "host:port"`. The version is the year in `RevitServerAdminRESTService{YEAR}`; if the URL has no year (a client created with `serverVersion: "2012"`), it falls back to `"2019"`.
- `FolderExtensions.CreateLocalModelWithoutToolAsync(this RevitServerApi api, string modelPipePath, string destinationFile, string revitVersion = null, string assembliesPath = null, bool overwrite = false)` runs the same export and returns `destinationFile`. Differences from `ExportModelAsync`: you can pass `revitVersion` explicitly; if it is null, the year is taken from `api.BaseUrl`, and `"2022"` is used when `BaseUrl` has no year. The host is `new Uri(api.BaseUrl).Host`. An overload adds `bool createLocal`, `IProgress<long> bytesProgress` and `CancellationToken`:

```csharp
await api.CreateLocalModelWithoutToolAsync(
    modelPipePath: "|Projects|Demo|Model.rvt",
    destinationFile: @"C:\Exports\Demo.rvt",
    createLocal: true,
    revitVersion: "2024",
    overwrite: true
);
```
- Use pipe-format paths (`|Projects|...|file.rvt`) or Windows-style relative paths (`Projects\...\file.rvt`). Leading, trailing and doubled separators are ignored and `/` is a separator too, as in the REST client (the ModelService itself rejects them with `ModelMissingFault`).

### How the export works
1. Endpoint: `net.tcp://{ServerHost}/ModelService{RevitVersion}/ModelService.svc/tcpstream`. `RevitVersion` must be a four-digit year (`ArgumentException` otherwise) and must be the year of the Revit Server: with another year the server has no such endpoint and the export fails with `InvalidOperationException` ("no ModelService endpoint responded").
2. All calls go over one streamed WCF channel (`NetTcpBinding`, no security), in the order RevitServerTool `createLocalRVT` uses: `IdentifyModel`, `LockData` (non-exclusive read lock), `GetListOfModelDataFilesWithoutLocking`, `DownloadFile` for every model data file, `ModelDataFormatVersion`, `UnlockData`. RevitServerTool asks for the data format version after `UnlockData`; here it is asked while the read lock is still held. `IdentifyModel` is called with `bUsedForAccess = true` (unchanged from 1.2.1). RevitServerTool calls it with `false` and calls again with `true` only when the server returns the pre-2013 identity (`FACE1111-2012-2012-2012-201220122012`). No difference was seen on Revit Server 2022-2025.
3. The files are downloaded to `%TEMP%\RevitServerNet_{guid}`, and the RVT file is assembled there with the Autodesk Helper (`ModelDataVersionManager` + `RvtFile.GenerateRvtFileFromModelFolder`), so the `%TEMP%` volume must hold the model data and the RVT file together (about twice the model size). The RVT file is then moved under a temporary name (`{file name}.{guid}.partial`) into the destination folder, which is where it is copied when `%TEMP%` is on another volume, and only then renamed to `DestinationFile`: the previous destination file is replaced only after the new file is complete in the destination folder, and a failure before that leaves it untouched. The temp folder is deleted at the end of the export; a folder left behind by a failed delete or a killed process is removed by a later export once it is more than 7 days old (as RevitServerTool does with its own temp folders; this also covers the `RevitServerNet_ModelData_{guid}` folders of earlier versions).
4. The output was compared stream by stream with RevitServerTool 2022 output and the server's model data files (Revit Server 2022, two models, net48 and net8), and with RevitServerTool 2024 output on Revit Server 2024 (same two models, net48 and net8): every stream is identical; `BasicFileInfo` differs only as described under `CreateLocal`.

### Locks
- `LockData` must return `Locked` or `WasLocked` (the read lock of this user name already exists, for example left by an interrupted earlier export). `Busy` means another operation holds the model (for example a synchronization with central): like RevitServerTool, `LockData` is then tried up to 5 times, 10 s apart (the wait is cancellable); the lock contention faults `DataLockContentionFault` and `PermissionLockContentionFault` are retried the same way. When all 5 attempts fail, or for any other status (`Missing`, `GaveUp`, `Unknown`), the export throws `InvalidOperationException` that names the status (and the number of attempts). A model locked by an administrator fails in `LockData` at once with the server fault `AdminLockBusyOnModelFault` (checked on Revit Server 2022).
- `UnlockData` is always called once the read lock may have been taken: after success, after an error and after cancellation (then on a new channel; the unlock itself is not cancellable). It is retried up to 5 times, 10 s apart, when it fails with a communication error (as RevitServerTool does); a timeout is not retried. If `LockData` was interrupted (cancelled or timed out) while the server was still processing it, the server can apply the lock after the unlock: when the first `UnlockData` then finds no lock, it is sent once more after 5 s. If the lock still appears later, it stays until it expires (12 h) or until the next export of this model from this machine, which gets `WasLocked` and releases it. Before 1.3.0 the lock was never released and stayed on the server until it expired (12 h).
- The read lock is held under the user name `RevitServerTool:{machine name}:1`. RevitServerTool uses `RevitServerTool:{machine name}:{n}`, where `n` is the number of its installation folder on that machine: each installation gets the lowest free number the first time it runs (listed in `RevitServerTool.ini`, in `C:\ProgramData\Autodesk\RevitServerTool\Data` on the machine tested). So only one RevitServerTool installation per machine uses the same name as this library; for example, with Revit 2022 and 2024 installed, RevitServerTool 2024 used `:2`. The server keeps one read lock per user name, so exports of the same model under the same name must not overlap. The export holds a named mutex with the same name as RevitServerTool's, `{user}_{model path}_{server}`, from `LockData` to `UnlockData` (RevitServerTool takes it earlier, before `IdentifyModel`, which takes no lock): exports of the same model by this library, and by a RevitServerTool that uses the same user name, wait for each other in the same Windows session (other sessions have their own mutex namespace). The mutex name is case-sensitive and uses `ServerHost` (trimmed, including any `:port`) and the model path after normalization: the path is split on `|`, `/` and `\`, empty segments (leading, trailing, doubled separators) are dropped, and the rest is joined with `\`, which then becomes `:` in the name. So `|A|B.rvt`, `A/B.rvt`, `A\B.rvt` and `||A||B.rvt|` share one mutex. The same model spelled differently in host (case, IP address instead of the name, `:port`) or in path case is not serialized. `ExportModelAsync` and `CreateLocalModelWithoutToolAsync` lowercase the host and drop the port, but the path case must still be the same. A name longer than 260 characters (the mutex name limit of .NET Framework) is replaced by a SHA-256 hash, so it does not coordinate with RevitServerTool. Without a `CancellationToken` the wait has no timeout. A RevitServerTool with another number holds its own read lock at the same time; each releases only its own (checked on Revit Server 2024).
- When exports that are not serialized overlap (another Windows session, or another spelling), the first one to finish releases the read lock of the other. The other export then gets `WasNotLocked` from `UnlockData`; it still succeeds (the file is usually consistent), and the lost lock is reported with `System.Diagnostics.Trace.TraceWarning` ("... was already released when UnlockData ran ..."), or in `Data["RevitServerNet.ReadLockLost"]` of the exception when the export failed.
- If `UnlockData` fails after an otherwise successful export, the file is still written, and the export throws `InvalidOperationException`: "The model was exported to '...', but releasing the server read lock failed (...). The read lock of user '...' remains on the server until it expires (12 h) or until the next export of this model from this machine releases it." Its `Data["RevitServerNet.ExportedFile"]` holds the path of the exported file: this key means that the RVT file is complete and only the lock release failed. If the export fails for another reason, an `UnlockData` failure is added to that exception's `Data["RevitServerNet.UnlockDataError"]` (that key alone does not mean a file was written).

### CreateLocal
- `false` (default, as in earlier versions): the file keeps the server's `BasicFileInfo` stream as is (central model: worksharing state "Central", the central path saved on the server).
- `true`: `BasicFileInfo` is rewritten the way RevitServerTool `createLocalRVT` writes it, through the Autodesk Helper (`ModelBasicFileInfoStream`): worksharing state "Created Local", central path `RSN://{ServerHost}/{model path with /}`, the server model's identity as the central identity, and a new random model identity. Checked against RevitServerTool 2022 (on Revit Server 2022) and RevitServerTool 2024 (on Revit Server 2024) output: byte-identical except for the random model identity.
- If the model's `BasicFileInfo` stream version is newer than the loaded Helper supports, the export fails with `NotSupportedException` instead of writing a stream that would lose fields (for example the 2019 client assemblies with a Revit 2022 model).

### Cancellation
`ModelExporter.ExportAsync`, the `ExportModelAsync` overload with `createLocal` and the `CreateLocalModelWithoutToolAsync` overload with `createLocal` accept a `CancellationToken`. It is checked before the export starts (nothing is created then), between the steps, and between 80 KB blocks of every download; a call that is waiting for the server is interrupted by aborting the channel. Waiting for another export of the same model (the mutex) and the wait between `LockData` retries are interrupted too. After cancellation the read lock is released, the temp folder is deleted, no destination file is written, and `OperationCanceledException` is thrown. The unlock after cancellation waits for the server's reply for up to 1 min per attempt (instead of the 10 min send timeout), and the model's export mutex is held during that time. The overloads without a token cannot be cancelled.

### Bundled Autodesk client assemblies
- The package contains one set: the Revit 2024 client assemblies, unmodified (AnyCPU): `RS.Enterprise.Common.ClientServer.DataContract.dll`, `RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll`, `RS.Enterprise.Common.ClientServer.Helper.dll`, `Autodesk.RevitServer.Social.dll`, `Castle.Core.dll`, `Castle.Windsor.dll`. These are the files the export needs (without the Castle assemblies it fails). `RS.Enterprise.Common.ClientServer.Proxy.dll` and `ServiceContract.Local.dll` are no longer used or shipped, nor are the per-year folders `RSAssemblies\2019` ... `RSAssemblies\2026`.
- `buildTransitive/RevitServerNet.props` copies them to `<output>\RSAssemblies\` on build and to `<publish>\RSAssemblies\` on `dotnet publish` (both checked for net8.0 and net48 consumers). With `PublishSingleFile` they are not bundled into the executable: they stay files in `<publish>\RSAssemblies\` next to it. With a project reference to `RevitServerNet.csproj`, the same happens through the project's items. To turn the copy off in a package consumer (for example a Revit add-in that does not use the direct export), declare an empty target with the name of the props target: `<Target Name="RevitServerNet_CopyRSAssembliesForConsumers" />`.
- Search order: `AssembliesPath` (when that directory exists; a path that does not exist is ignored), `<AppContext.BaseDirectory>\RSAssemblies`, `<folder of RevitServerNet.dll>\RSAssemblies`, `<folder of the process executable>\RSAssemblies` (.NET 6 and later; for a single-file app published with `IncludeAllContentForSelfExtract`, whose base directory is the extraction folder), `<Program Files>\Autodesk\Revit {year}\RevitServerToolCommand`. A directory is used only when it directly contains all six files; subfolders are not searched, and a directory that does not qualify (an `AssembliesPath` too) is skipped without an error. When a later directory is used, the skipped ones and the reasons are written with `System.Diagnostics.Trace.TraceWarning` and added to the loading error messages. If no directory qualifies, the export fails with `InvalidOperationException` that lists every searched path and why it was skipped. On .NET Framework an assembly cannot be loaded from a path longer than 259 characters (also in a long-path-aware process), so a directory whose files have longer paths is skipped with that reason and the next directory in the order is used (for example the set of an installed Revit). Keep `<app>\RSAssemblies` or `AssembliesPath` short enough: the longest file name, `RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll`, has 59 characters.
- The assemblies are loaded once per process. The first set found without an `AssembliesPath` (or with one that does not exist) is used for all later exports without one, whatever their `RevitVersion`. An export that resolves to another directory later in the same process fails with `InvalidOperationException` (two sets with the same assembly names cannot be used in one process), as does loading when an assembly with one of these names is already loaded from another location (for example inside Revit.exe, whose install folder ships `RS.Enterprise.Common.ClientServer.*.dll`).
- The bundled `RS.Enterprise.Common.ClientServer.Helper` and `Castle.Windsor` need `Castle.Core` 3.2.0.0. A process that binds `Castle.Core` to another version (a `Castle.Core` 4.x/5.x dependency on .NET, for example through Moq or NSubstitute, or a `bindingRedirect` on .NET Framework) cannot use the direct export: it fails with `InvalidOperationException` ("... the Windsor container could not be created in this process ...") before any server call. Run the export in a process without it.
- `AssembliesPath` can point to another year's set that contains the six files. Checked against Revit Server 2022: the 2019 and 2022 sets on net48 and net8.0, the 2025 and 2026 sets on net8.0. The 2025 and 2026 sets are built for .NET 8 and fail on net48 ("The WCF client for IModelService could not be created ..."). They (and an installed Revit 2025 or later `RevitServerToolCommand`) are also x64-only: they need a 64-bit process, and in a 32-bit process loading fails (`FileLoadException` on .NET 8, `BadImageFormatException` on .NET Framework).

### Timeouts and limits
- Open 1 min, close 1 min, send 10 min (one call until its reply starts; for `DownloadFile` until the file stream starts), receive 10 min. The total time to read a downloaded file is not limited by these timeouts (checked with a slowed-down reader against Revit Server 2022 on net48 and net8: a 10 s read with a 3 s send timeout and a 40 s read with a 2 s receive timeout both complete).
- Largest model data file: 5 GB. Reader quotas and buffer sizes are those of Autodesk's own streamed binding.

### Errors and diagnostics
- Server and connection errors are thrown as `InvalidOperationException` with the operation, the model and the endpoint in the message and the WCF exception as `InnerException` (for a server fault the fault type is named, for example `ModelMissingFault`). Argument errors are `ArgumentException`; an existing `DestinationFile` with `Overwrite = false` is an `IOException` thrown before the server is contacted.
- If the temp folder cannot be deleted, the failure is added to the exception's `Data["RevitServerNet.TempCleanupError"]`, or written with `System.Diagnostics.Trace.TraceWarning` after a successful export. A later export deletes it once it is more than 7 days old.
- If a download fails (for example the disk is full), the connection is aborted and the download's own exception is thrown; an error raised while closing the download is added to `Data["RevitServerNet.DownloadCloseError"]`. If the temporary `.partial` file in the destination folder cannot be deleted after a failure, that is added to `Data["RevitServerNet.PartialFileCleanupError"]`.
- The export no longer writes diagnostic files: the `%TEMP%\RevitServerNet_*.txt` files of earlier versions are not created any more (existing ones are not deleted).

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

### RevitServerUiApi Class

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

### ModelExporter (namespace `RevitServerNet`)

Direct export over the Revit Server ModelService (no `RevitServerTool.exe`), see [Direct export from Revit Server](#direct-export-from-revit-server-rs-enterprise-api):

- `static Task<string> ExportAsync(ModelExporterOptions options, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)`: returns the path of the created RVT file. `bytesProgress` reports the bytes downloaded so far of the current model-data file (after every 80 KB block; it restarts from 0 for every file). `bytesProgress.Report` is called on a thread-pool thread: use `System.Progress<T>` or marshal yourself for UI updates (the same applies to the `ExportModelAsync` and `CreateLocalModelWithoutToolAsync` overloads with `bytesProgress`). `cancellationToken` cancels the export (see Cancellation above). Overlapping exports of the same model are serialized only within one Windows session and only when `ServerHost` is spelled the same and `ModelPipePath` has the same case (separators are normalized; see Locks above). Throws `ArgumentNullException` if `options` is null and `ArgumentException` if `ServerHost`, `ModelPipePath`, `DestinationFile` or `RevitVersion` is empty, `RevitVersion` is not a four-digit year, or `ServerHost` is not a host name or IP address with an optional port. Creates the destination directory if needed; if `DestinationFile` already exists and `Overwrite` is `false`, throws `IOException` before contacting the server (the same applies to `ExportModelAsync` and `CreateLocalModelWithoutToolAsync`). The destination is checked before anything is downloaded: a `DestinationFile` that does not end with a valid file name throws `ArgumentException`; an existing folder in its place, a folder that cannot be written, or (with `Overwrite`) an existing file that is read-only or open in another process throws `IOException`/`UnauthorizedAccessException`, and no read lock is taken.
- `ModelExporterOptions`: `ServerHost` (host or host:port of the net.tcp endpoint), `ModelPipePath` (pipe format `|Projects|...|file.rvt` or Windows-style relative path), `DestinationFile`, `RevitVersion` (four-digit year, e.g. `"2024"`), `AssembliesPath` (optional; see Search order under [Bundled Autodesk client assemblies](#bundled-autodesk-client-assemblies)), `Overwrite` (default `false`), `CreateLocal` (default `false`).

### RevitServerToolClient (namespace `RevitServerNet.Tools`)

Static wrapper around `RevitServerTool.exe createLocalRvt`:

- `static string TryLocateToolPath(string versionHint = null)`: checks the `REVITSERVER_TOOL_PATH` environment variable first, then `Autodesk\Revit <versionHint>\RevitServerToolCommand\RevitServerTool.exe`, then any `Autodesk\Revit *` folder under the Program Files folders. Returns `null` if the tool is not found.
- `static Task<RevitServerToolResult> CreateLocalModelAsync(string toolPath, string serverHost, string modelRelativePath, string destinationFile, bool overwrite = false, int timeoutMs = 600000)`: runs the tool and returns its result. It does not throw on a non-zero exit code, so check `Success`. Throws `ArgumentException` if `serverHost`, `modelRelativePath` or `destinationFile` is empty, or if the full destination path has no file name (for example it ends with a separator); `ArgumentOutOfRangeException` if `timeoutMs` is less than -1; `RevitServerToolException` if `toolPath` does not exist; and `System.ComponentModel.Win32Exception` if the process cannot be started (for example `toolPath` is not an executable). Creating the destination directory can throw `IOException`, `UnauthorizedAccessException`, `PathTooLongException` or `NotSupportedException`. The task faults with `TimeoutException` after `timeoutMs`: the tool is killed and the wait for it to exit is limited to 5 seconds.
- `static string ConvertPipePathToRelativeWindowsPath(string pipePath)`: converts `|Projects|Demo|Model.rvt` to `Projects\Demo\Model.rvt`.
- `static string ParseVersionFromBaseUrl(string baseUrl)`: extracts the year from `RevitServerAdminRESTService<year>`, or returns `null`.
- `RevitServerToolResult`: `ExitCode`, `StandardOutput`, `StandardError`, `ErrorMessage` (the first output line that starts with `ERROR:`, or null), `Success` (`ExitCode == 0` and no `ERROR:` line: RevitServerTool exits with 0 also when it fails, for example for a model that does not exist).
- The server host, model path and destination are passed as separately quoted and escaped arguments; a destination without a directory is relative to the current directory.
- `RevitServerToolException`: thrown for tool-level failures.

## Error Handling

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

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.

## Support

If you encounter any issues or have questions, please open an issue at https://github.com/mahach666/RevitServerNet/issues.
