# .NET build foundation

V01-001 pins SDK `10.0.111`, C# 14 and `net10.0`. The two service projects are inert class-library markers only; this build foundation does not start a server, load a Provider, access PostgreSQL or touch asset files.

The SDK version is the M0-004-proven feature band for .NET runtime `10.0.11`. `global.json` disables roll-forward, while `NuGet.config` clears inherited sources and keeps the package cache in the ignored `.runtime/nuget` directory.

Run the merge-equivalent .NET gates from the repository root with the exact SDK selected by `global.json`:

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1 > .runtime/dotnet-vulnerabilities.json
python scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir .runtime/nuget --vulnerability-report .runtime/dotnet-vulnerabilities.json
python scripts/validate_dotnet_source.py
```

`Directory.Packages.props` is the only package-version source. Every project commits `packages.lock.json`; CI restores with `--locked-mode` on both Windows and Ubuntu. `eng/dotnet-dependency-policy.json` permits only the NuGet.org source and MIT package-license expressions. A package using a license file requires an exact package/version/path/SHA-256 exception and review before merge.

Roslyn analyzers run with warnings as errors. The code-metrics analyzer reads `eng/CodeMetricsConfig.txt`; the repository source gate independently rejects repeated C# token blocks and likely logging of secrets or credentials.
