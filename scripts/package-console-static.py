#!/usr/bin/env python3
"""Package only the standalone browser files for release and static hosting."""

import argparse
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def package(source: Path, output: Path, version: str) -> dict:
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?", version):
        raise ValueError("version must be a semantic release version")
    for required in ("index.html", "config.json", "_framework/blazor.webassembly.js"):
        if not (source / required).is_file():
            raise ValueError(f"missing static entrypoint: {required}")
    if '<base href="/console/"' not in (source / "index.html").read_text():
        raise ValueError("Console must be published beneath /console/")
    files = sorted(path for path in source.rglob("*") if path.is_file())
    if any(path.is_symlink() for path in source.rglob("*")):
        raise ValueError("static release must not contain symbolic links")
    if output.resolve().is_relative_to(source.resolve()):
        raise ValueError("output must be outside the static source directory")
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"honua-console-{version}.zip"
    with ZipFile(destination, "w", compression=ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.relative_to(source))
    with destination.open("rb") as bundle:
        digest = hashlib.file_digest(bundle, "sha256").hexdigest()
    receipt = {
        "asset": destination.name,
        "version": version,
        "files": len(files),
        "uncompressedBytes": sum(path.stat().st_size for path in files),
        "zipBytes": destination.stat().st_size,
        "sha256": digest,
    }
    (output / f"{destination.name}.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="dotnet publish output's wwwroot/console directory")
    parser.add_argument("--output", type=Path, default=Path("artifacts/static-release"))
    parser.add_argument("--version", required=True)
    arguments = parser.parse_args()
    print(json.dumps(package(arguments.source, arguments.output, arguments.version), indent=2))
