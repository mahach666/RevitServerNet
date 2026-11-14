# Security policy

## Supported versions

Security fixes go into the latest release published on [nuget.org](https://www.nuget.org/packages/RevitServerNet).

## Reporting a vulnerability

Please do not open a public issue for a vulnerability.

Use GitHub private vulnerability reporting: open the [Security tab](https://github.com/mahach666/RevitServerNet/security) of the repository and choose "Report a vulnerability". Describe what is affected, how to reproduce it and what an attacker gains. Do not include real server names, credentials or customer models.

You will get an answer as soon as the maintainer can read the report. A confirmed issue is fixed in a new release, and the report is published as a GitHub security advisory when the fix is out.

## Scope notes

- The direct export talks to Revit Server over net.tcp without transport security (the same as the Autodesk clients do), and the REST and Admin UI clients use plain HTTP unless `useHttps` is set. Use a trusted network or HTTPS where the server supports it.
- The package bundles unmodified Autodesk Revit Server client assemblies. Problems inside those assemblies should be reported to Autodesk.
