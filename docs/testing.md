---
title: Testing
---

# Testing

The test project is `RevitServerNet.Tests` (xUnit, targets `net8.0` and `net48`). It has three kinds of tests.

- Unit tests: pure logic such as path encoding (`EncodePathTests`), export option validation (`DirectExportOptionsTests`), the assembly loader (`RsAssemblyLoaderTests`) and the RevitServerTool client (`ExportToolClientTests`).
- Wire tests: the client talks to a local stub HTTP server (`StubHttpServer`) that returns canned Revit Server answers, so requests and parsing are checked without a Revit Server (`RevitServerApiWireTests`, `FolderWireTests`, `HistoryWireTests`, `LockingWireTests`, `UiApiWireTests`, `RevitServerUiApiWireTests`).
- Live tests: run against a real Revit Server (`FolderLiveTests`, `LiveHistoryTests`, `LiveLockingTests`, `LiveUiApiTests`, `LiveExportTests`, `LiveServerExportTests`). They are skipped when the environment variables below are not set, so a plain `dotnet test` runs offline.

## Run the offline tests

```powershell
dotnet test RevitServerNet.sln -c Release
```

The `net48` run and the Windows-only direct export code need Windows. The live tests of both target frameworks use the same server model, so the frameworks run one after another, and all live test classes share one xUnit collection and do not run in parallel.

## Run the live tests

Live tests need a Revit Server and a model on it. A convenient stand is [RevitServerStack](https://github.com/mahach666/RevitServerStack): it runs Revit Server 2020 to 2027 in Docker on one Windows machine. Set the variables, then run the tests:

| Variable | Meaning |
| --- | --- |
| `RSN_TEST_RS_HOST` | Host or `host:port` of the net.tcp endpoint of the ModelService (required). |
| `RSN_TEST_RS_YEAR` | Year of the Revit Server, for example `2022` (required). |
| `RSN_TEST_RS_MODEL` | Pipe path of a model on that server, for example `\|Projects\|Demo\|Model.rvt`; it may be percent-encoded (required). |
| `RSN_TEST_RS_REST_HOST` | Host or `host:port` of the HTTP REST service (optional, default `RSN_TEST_RS_HOST`). |

A test that goes through `ExportModelAsync` reaches the ModelService on the default net.tcp port of the REST host, so it is skipped when `RSN_TEST_RS_HOST` is not the host of `RSN_TEST_RS_REST_HOST`.

Example against a stand where the admin service answers on `localhost:2022` and port 808 is published for that version:

```powershell
$env:RSN_TEST_RS_HOST = 'localhost'
$env:RSN_TEST_RS_YEAR = '2022'
$env:RSN_TEST_RS_MODEL = '|Projects|Demo|Model.rvt'
$env:RSN_TEST_RS_REST_HOST = 'localhost:2022'
dotnet test RevitServerNet.sln -c Release
```

The live tests create, rename, delete and lock folders and models on the server and export the model: use a disposable stand, not a production server.

## Package smoke test

`scripts/package-smoke.ps1` packs the library into a temporary feed, builds a throwaway console app that references the package (net8.0 and net48), and checks that the six bundled Autodesk assemblies are in `RSAssemblies\` of the build output, the publish output and a self-contained single-file publish, and that every output starts and fails with "no ModelService endpoint responded" rather than "Revit Server client assemblies not found". A second app that turns the copy off must get no `RSAssemblies` folder. It needs the .NET 8 SDK and Windows.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-smoke.ps1
```
