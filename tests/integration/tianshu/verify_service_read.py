"""TS062: inherited HTTP input audit and real Application/AssetLink port tests.

No server, database, browser credentials, or production connection is accepted.
The existing query test double is explicitly retained; this is not service HTTP.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
BASE = "555f337271c3b8690d96a4a5a72d796d1c0a7536"
HTTP_BASE = "99e79f2f5a6d3b84ff2faaa9640752569796eb69"
INPUTS = ["services", "packages", "database", "global.json", "Directory.Build.props",
          "Directory.Build.targets", "Directory.Packages.props", "contracts",
          "tests/dotnet", "tests/integration/native-clients"]
CLASSES = ["LibrariesListProtocolTests", "EntriesBrowseProtocolTests",
           "AssetSearchProtocolTests", "AssetLinkFailureProtocolTests",
           "BrowseResultValidationTests", "PageResultValidationTests", "ReadDeadlineTests"]


def run(*args):
    return subprocess.run(args, cwd=ROOT, check=True, capture_output=True,
                          text=True, encoding="utf-8", errors="replace").stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True, type=Path)
    args = parser.parse_args()
    dotnet = str(args.dotnet.resolve(strict=True))
    if run(dotnet, "--version").strip() != "10.0.111":
        raise RuntimeError("Requires the repository's exact SDK 10.0.111")
    if run("git", "diff", HTTP_BASE, "--", *INPUTS).strip():
        raise RuntimeError("Inherited Core HTTP inputs changed; reassess evidence")
    historical = ["tests/integration/tianshu/probe_core.py",
                  "docs/integrations/tianshu/evidence.json"]
    if run("git", "diff", BASE, "--", *historical).strip():
        raise RuntimeError("TS060 evidence/probe changed")
    output = ROOT / ".codex/handoffs/TS-062"
    output.mkdir(parents=True, exist_ok=True)
    runtime = ROOT / ".runtime/ts062-port-results"
    runtime.mkdir(parents=True, exist_ok=True)
    trx = runtime / "ports.trx"
    if trx.exists():
        raise RuntimeError("Existing result: inspect before a deliberate new run")
    os.environ["DOTNET_CLI_HOME"] = str(ROOT / ".runtime/ts062-dotnet")
    os.environ["NUGET_PACKAGES"] = str(ROOT / ".runtime/ts062-nuget")
    result = run(dotnet, "test",
                 "tests/dotnet/AssetLibrary.WebGateway.Tests/AssetLibrary.WebGateway.Tests.csproj",
                 "--configuration", "Release", "--no-build", "--no-restore",
                 "--filter", "|".join("FullyQualifiedName~." + c + "." for c in CLASSES),
                 "--logger", "trx;LogFileName=ports.trx", "--results-directory", str(runtime))
    counters = ET.parse(trx).find(".//{*}Counters").attrib
    if int(counters["total"]) == 0 or counters["total"] != counters["passed"]:
        raise RuntimeError("Port tests failed, skipped or empty: " + str(counters))
    report = {"task": "TS-062", "baseline": BASE, "sdk": "10.0.111",
              "inherited_http_inputs": "unchanged from " + HTTP_BASE,
              "http_evidence": "docs/integrations/tianshu/evidence.json (TS060; not rerun)",
              "port_test_classes": CLASSES, "counters": counters,
              "trx_sha256": hashlib.sha256(trx.read_bytes()).hexdigest(),
              "real_components": ["ReadOnlyBrowseService", "ReadOnlyAssetLinkProtocol"],
              "test_doubles": ["FakeAuthorizedReadModelQuery", "ClaimsPrincipal fixture"],
              "service_identity_http": "not implemented / not tested",
              "platform": "not connected", "production": "not touched"}
    (output / "port-evidence.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(result.strip())
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
