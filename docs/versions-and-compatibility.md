---
title: Versions and compatibility
---

# Versions and compatibility

## Supported Revit Server versions

- REST API (`RevitServerApi`) and Admin UI API (`RevitServerUiApi`): `serverVersion` must be a numeric year >= 2012; no upper bound is enforced. 2012 maps to `/RevitServerAdminRESTService/AdminRESTService.svc` (REST) and `/RevitServerAdmin` (UI); 2013 and later map to `/RevitServerAdminRESTService{YEAR}/AdminRESTService.svc` and `/RevitServerAdmin{YEAR}`. A year below 2012 or a non-numeric value throws `ArgumentException`.
- Direct export: any Revit Server year whose server exposes `net.tcp://{host}/ModelService{year}/ModelService.svc/`. The year is only a part of that address; the same bundled client assemblies are used for every year. Verified live on Revit Server 2022, 2023, 2024 and 2025 (a docker test stand; net48 and net8.0). All four servers served the same test models saved in Revit 2022 format (copied unchanged onto the 2023-2025 servers), so models saved by Revit 2023 or later were not part of these checks. With `CreateLocal = true`, `BasicFileInfo` matched the output of the official RevitServerTool 2022 and 2024. Revit Server 2026 and 2027 are expected to work through the same endpoint pattern but have not been verified against a live server; Revit Server 2021 and earlier have not been verified live either. See [Direct export from Revit Server](direct-export.md#direct-export-from-revit-server-rs-enterprise-api).
- `api.ExportModelAsync(...)` reads the version from `BaseUrl`. On a client created with `serverVersion: "2012"` it cannot find a year and uses "2019". For such a server, call `ModelExporter.ExportAsync` with `RevitVersion` set explicitly.

## Requirements

- .NET Framework 4.8, .NET 6 or .NET 8 (package targets net48, net6.0, net8.0)
- Dependencies (installed automatically): Newtonsoft.Json 13.0.4, System.Security.Permissions 9.0.9; on net6.0/net8.0 also System.ServiceModel.Primitives and System.ServiceModel.NetTcp 4.9.0; on net48 the framework assembly System.ServiceModel. On net6.0, System.Security.Permissions 9.0.9 emits a 'not supported on net6.0' build warning.
- Direct export only: Windows, network access to the Revit Server's net.tcp port (808 by default), and the Autodesk client assemblies (bundled, copied to `<output>\RSAssemblies\` on build and to `<publish>\RSAssemblies\` on publish; with `PublishSingleFile` they stay files in `<publish>\RSAssemblies\` next to the executable). The `%TEMP%` volume must hold the downloaded model data and the assembled RVT file together, about twice the model size. A 64-bit process is not required for the bundled set: its assemblies are AnyCPU (a 32-bit .NET Framework 4.8 process was checked). Other sets can be x64-only, see [Bundled Autodesk client assemblies](direct-export.md#bundled-autodesk-client-assemblies).
