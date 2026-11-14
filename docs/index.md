---
title: RevitServerNet
---

# RevitServerNet

RevitServerNet is a .NET library for working with Revit Server: the REST API, the Admin UI API and a direct export of models to RVT files over the Revit Server ModelService (net.tcp), without `RevitServerTool.exe`.

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

## Where to go next

- [Getting started](getting-started.md): install the package and make a first call.
- [REST API](rest-api.md): `RevitServerApi` and the extension methods.
- [Admin UI API](admin-ui-api.md): `RevitServerUiApi`.
- [Direct export](direct-export.md): export a model to an RVT file over net.tcp, locks, `CreateLocal`, cancellation, bundled assemblies, timeouts.
- [RevitServerTool wrapper](revitservertool.md): export through `RevitServerTool.exe`.
- [Error handling](error-handling.md): which calls throw and which return a result.
- [Versions and compatibility](versions-and-compatibility.md): supported Revit Server years and requirements.
- [Testing](testing.md): unit, wire and live tests.
- [API reference](api/RevitServerNet.yml): every public type, generated from the XML documentation.
- [Source on GitHub](https://github.com/mahach666/RevitServerNet) and [DeepWiki](https://deepwiki.com/mahach666/RevitServerNet) for an automatically generated overview of the repository.

RevitServerNet is not affiliated with, endorsed by or sponsored by Autodesk. Autodesk, Revit and Revit Server are trademarks of Autodesk, Inc.
