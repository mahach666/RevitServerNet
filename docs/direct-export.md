---
title: Direct export
---

# Direct export

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

## API reference

### ModelExporter (namespace `RevitServerNet`)

Direct export over the Revit Server ModelService (no `RevitServerTool.exe`), see [Direct export from Revit Server](#direct-export-from-revit-server-rs-enterprise-api):

- `static Task<string> ExportAsync(ModelExporterOptions options, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)`: returns the path of the created RVT file. `bytesProgress` reports the bytes downloaded so far of the current model-data file (after every 80 KB block; it restarts from 0 for every file). `bytesProgress.Report` is called on a thread-pool thread: use `System.Progress<T>` or marshal yourself for UI updates (the same applies to the `ExportModelAsync` and `CreateLocalModelWithoutToolAsync` overloads with `bytesProgress`). `cancellationToken` cancels the export (see Cancellation above). Overlapping exports of the same model are serialized only within one Windows session and only when `ServerHost` is spelled the same and `ModelPipePath` has the same case (separators are normalized; see Locks above). Throws `ArgumentNullException` if `options` is null and `ArgumentException` if `ServerHost`, `ModelPipePath`, `DestinationFile` or `RevitVersion` is empty, `RevitVersion` is not a four-digit year, or `ServerHost` is not a host name or IP address with an optional port. Creates the destination directory if needed; if `DestinationFile` already exists and `Overwrite` is `false`, throws `IOException` before contacting the server (the same applies to `ExportModelAsync` and `CreateLocalModelWithoutToolAsync`). The destination is checked before anything is downloaded: a `DestinationFile` that does not end with a valid file name throws `ArgumentException`; an existing folder in its place, a folder that cannot be written, or (with `Overwrite`) an existing file that is read-only or open in another process throws `IOException`/`UnauthorizedAccessException`, and no read lock is taken.
- `ModelExporterOptions`: `ServerHost` (host or host:port of the net.tcp endpoint), `ModelPipePath` (pipe format `|Projects|...|file.rvt` or Windows-style relative path), `DestinationFile`, `RevitVersion` (four-digit year, e.g. `"2024"`), `AssembliesPath` (optional; see Search order under [Bundled Autodesk client assemblies](#bundled-autodesk-client-assemblies)), `Overwrite` (default `false`), `CreateLocal` (default `false`).
