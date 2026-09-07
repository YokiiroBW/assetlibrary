# .NET build foundation

V01-001 pins SDK `10.0.111`, C# 14 and `net10.0`. `services/core-server` now contains the scoped V0.1 modules; `Host/` is the common Windows/Linux/Docker entry point with health endpoints and optional PostgreSQL readiness. WorkerSupervisor remains an inert marker. Production authentication, business API and physical writes remain blocked; see `docs/releases/V0.1_ALPHA_READINESS.md`.

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

`Directory.Packages.props` is the only package-version source. Every project in `AssetLibrary.slnx` commits `packages.lock.json`; CI restores with `--locked-mode` on both Windows and Ubuntu. The excluded M0-004 packaging Spike reuses the same central pin and keeps generated restore state in its isolated build directory. `eng/dotnet-dependency-policy.json` permits only the NuGet.org source and MIT package-license expressions. A package using a license file requires an exact package/version/path/SHA-256 exception and review before merge.

Roslyn analyzers run with warnings as errors. The code-metrics analyzer reads `eng/CodeMetricsConfig.txt`; the repository source gate independently rejects repeated C# token blocks and likely logging of secrets or credentials.

Repository and architecture checks run with `python -I -B scripts/verify_repository.py`. The complete cross-stack commands, exact runtimes and platform requirements are recorded in `tests/architecture/ci-tiers.json` and `.github/workflows/handoff-quality.yml`; Web commands also live in `apps/web/package.json`, and SDK commands in `packages/sdk/assetlink/README.md`. Use the pinned SDK and package-manager versions, not the machine's default runtime. A missing tool or skipped platform test is missing evidence.
