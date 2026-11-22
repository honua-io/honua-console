#!/usr/bin/env python3
"""Package the standalone browser files, with the Console and .NET license texts, for release and static hosting."""

import argparse
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

REPOSITORY = Path(__file__).resolve().parent.parent
# Operators edit config.json after extraction; a shipped precompressed copy would be served
# instead of the edit by hosts that serve .gz/.br siblings (nginx gzip_static, brotli_static).
OPERATOR_EDITED_PRECOMPRESSED = frozenset({"config.json.gz", "config.json.br"})


def legal_files() -> dict[str, Path]:
    """The Console's Apache-2.0 LICENSE and NOTICE plus the upstream licences of the shipped .NET code."""
    files = {"LICENSE": REPOSITORY / "LICENSE", "NOTICE": REPOSITORY / "NOTICE"}
    licenses = REPOSITORY / "release" / "licenses"
    files.update((f"licenses/{path.relative_to(licenses).as_posix()}", path)
                 for path in sorted(licenses.rglob("*")) if path.is_file())
    return files


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
    entries = {name: path for path in files
               if (name := path.relative_to(source).as_posix()) not in OPERATOR_EDITED_PRECOMPRESSED}
    legal = legal_files()
    if clash := sorted(set(entries) & set(legal)):
        raise ValueError(f"published files collide with release licence files: {clash}")
    entries.update(legal)
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"honua-console-{version}.zip"
    with ZipFile(destination, "w", compression=ZIP_DEFLATED) as archive:
        for name in sorted(entries):
            archive.write(entries[name], name)
    with destination.open("rb") as bundle:
        digest = hashlib.file_digest(bundle, "sha256").hexdigest()
    receipt = {
        "asset": destination.name,
        "version": version,
        "files": len(entries),
        "uncompressedBytes": sum(path.stat().st_size for path in entries.values()),
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
