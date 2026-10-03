"""Verify analyzer-only NuGet layout and, optionally, a built package consumer."""

import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile


def verify(package: Path, consumer: Path | None) -> None:
    expected = {
        "analyzers/dotnet/cs/UO.Analyzers.dll",
        "analyzers/dotnet/cs/UO.Analyzers.CodeFixes.dll",
    }
    with ZipFile(package) as archive:
        names = set(archive.namelist())
        assert {name for name in names if name.endswith(".dll")} == expected, names
        assert not any(name.startswith(("lib/", "runtimes/", "ref/")) for name in names), names
        assert "README.md" in names
        manifest = next(name for name in names if name.endswith(".nuspec"))
        root = ET.fromstring(archive.read(manifest))
        assert not root.findall(".//{*}dependency"), "Runtime/package dependencies were packed"

    if consumer:
        assets = json.loads((consumer / "obj/project.assets.json").read_text())
        package_entries = [value for key, value in assets["libraries"].items() if key.startswith("UO.Analyzers/")]
        assert len(package_entries) == 1, "Consumer did not restore UO.Analyzers"
        assert expected.issubset(set(package_entries[0]["files"])), "Missing analyzer assets"
        for target in assets["targets"].values():
            for name, entry in target.items():
                if name.startswith("UO.Analyzers/"):
                    assert not entry.get("runtime"), "Analyzer became a runtime asset"
                    assert not entry.get("compile"), "Analyzer became a compile reference"
        output = consumer / "bin/Release/net10.0"
        assert (output / "PackageConsumer.dll").exists(), "Consumer was not built"
        assert not list(output.glob("UO.Analyzers*.dll")), "Analyzer copied to runtime output"
        generated = list((consumer / "obj/generated").rglob("*.cs"))
        assert any("LogProcessingOrder" in path.read_text() for path in generated), "Logging generator did not run"
        print("Consumer: analyzer assets resolved, logging generated, no analyzer runtime DLLs")

    print(f"Verified {package}: both DLLs in analyzers/dotnet/cs, no runtime dependencies")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--consumer", type=Path)
    args = parser.parse_args()
    verify(args.package, args.consumer)
