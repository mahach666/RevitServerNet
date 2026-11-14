# Contributing

Thank you for helping with RevitServerNet. Bug reports, fixes and documentation improvements are welcome.

## Before you start

- Open an issue first for a larger change, so the approach can be agreed before you write code.
- Search the existing issues and [CHANGES.md](CHANGES.md) to see whether the behaviour is known or was changed on purpose.
- Do not report a vulnerability in a public issue, see [SECURITY.md](SECURITY.md).

## Build and test

Building needs Windows and the .NET 8 SDK or later (the library targets net48, net6.0 and net8.0).

```powershell
dotnet build RevitServerNet.sln -c Release
dotnet test RevitServerNet.sln -c Release
```

The unit and wire tests run offline. The live tests need a Revit Server and are skipped unless `RSN_TEST_RS_HOST`, `RSN_TEST_RS_YEAR` and `RSN_TEST_RS_MODEL` are set; a test stand can be made with [RevitServerStack](https://github.com/mahach666/RevitServerStack). See [docs/testing.md](docs/testing.md).

When a change touches the packaging (the bundled `RSAssemblies`, `buildTransitive`), also run `scripts/package-smoke.ps1`.

## Pull requests

- Keep a pull request to one topic and keep the diff small. No unrelated refactoring or reformatting.
- Add or update tests for a changed behaviour: the main case, errors and edge cases. A claim about a real Revit Server should be checked against one.
- Keep the public API backward compatible. A behaviour change that callers can notice goes into [CHANGES.md](CHANGES.md).
- Update the pages in `docs/` when the behaviour they describe changes.
- Do not commit secrets, server names or customer models.

## Documentation

The site is built with docfx from `docs/` and the XML documentation of the public API:

```powershell
dotnet tool restore
dotnet docfx docfx.json --warningsAsErrors
```

The build fails on a warning, for example a broken link or a malformed XML doc comment. The generated `_site/` and `docs/api/` folders are not committed.

## Releases

Maintainers cut a release like this:

1. Change `<Version>` in `RevitServerNet.csproj` and add a `## <version>` section (for example `## 1.5.0`) to the top of `CHANGES.md`. The release workflow publishes that section as the release notes.
2. Merge to `master`.
3. Tag the merge commit `v<version>` and push the tag. The `Release` workflow checks that the tag equals the project version, builds and tests, pushes the packages to nuget.org through trusted publishing, and creates the GitHub release with the packages, a source archive and `SHA256SUMS.txt`. A version with a hyphen (`1.5.0-beta.1`) is published as a prerelease.

## License

By contributing you agree that your contribution is licensed under the MIT License of this repository.
