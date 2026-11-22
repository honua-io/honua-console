#!/usr/bin/env python3
"""Enrich the Console static-bundle CycloneDX SBOM with shipped non-NuGet assets and filter build-only NuGet packages."""

from __future__ import annotations

import argparse
import base64
from dataclasses import dataclass
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZipFile

BUILD_ONLY_NUGET_EXACT = frozenset(
    {
        "microsoft.net.illink.tasks",
        "microsoft.aspnetcore.components.webassembly.devserver",
        "microsoft.net.sdk.webassembly.pack",
    }
)

STUDIO_PREFIX = "_studio/"
CESIUM_PREFIX = "_content/Honua.Console.Shell/vendor/cesium/"
FRAMEWORK_PREFIX = "_framework/"
VENDOR_PREFIX = "_content/Honua.Console.Shell/vendor/"

SRI_ALGORITHMS = {
    "sha256": "SHA-256",
    "sha384": "SHA-384",
    "sha512": "SHA-512",
}

WASM_SOURCE_RE = re.compile(r"^nuget:Microsoft\.NETCore\.App\.Runtime\.Mono\.browser-wasm/([^/]+)/")


@dataclass(frozen=True)
class BundleLocks:
    studio: dict
    cesium: dict
    vendored: dict
    licenses_sources: dict


def is_nuget_component(component: dict) -> bool:
    return str(component.get("purl", "")).lower().startswith("pkg:nuget/")


def is_build_only_nuget(component: dict) -> bool:
    if not is_nuget_component(component):
        return False
    name = str(component.get("name", "")).strip().lower()
    return name in BUILD_ONLY_NUGET_EXACT or name.endswith(".analyzers")


def parse_sri_hash(integrity: str) -> dict[str, str]:
    if "-" not in integrity:
        raise ValueError(f"unsupported integrity string: {integrity!r}")
    alg_key, b64_digest = integrity.split("-", 1)
    alg = SRI_ALGORITHMS.get(alg_key.lower())
    if not alg:
        raise ValueError(f"unsupported SRI algorithm: {alg_key!r}")
    raw = base64.b64decode(b64_digest, validate=True)
    return {"alg": alg, "content": raw.hex()}


def require_hex_sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value.lower()):
        raise ValueError(f"{label} must be a 64-character hex SHA-256 digest")
    return value.lower()


def build_studio_component(studio_lock: dict, zip_names: set[str]) -> dict:
    if not any(name.startswith(STUDIO_PREFIX) for name in zip_names):
        raise ValueError(f"bundle zip is missing shipped Studio assets under {STUDIO_PREFIX}")
    repo = str(studio_lock.get("repository", "")).strip()
    tag = str(studio_lock.get("tag", "")).strip()
    asset = str(studio_lock.get("asset", "")).strip()
    source_commit = str(studio_lock.get("sourceCommit", "")).strip()
    sha256 = require_hex_sha256(studio_lock.get("sha256"), "studio-release.lock.json sha256")
    if not repo or not tag or not asset or not source_commit:
        raise ValueError("studio-release.lock.json is missing required identity fields")
    version = tag.removeprefix("v")
    purl = f"pkg:github/{repo}@{tag}"
    return {
        "bom-ref": purl,
        "type": "library",
        "name": repo.split("/")[-1],
        "version": version,
        "purl": purl,
        "hashes": [{"alg": "SHA-256", "content": sha256}],
        "licenses": [{"license": {"id": "Apache-2.0"}}],
        "properties": [
            {"name": "honua:bundle:path", "value": STUDIO_PREFIX},
            {"name": "honua:studio:asset", "value": asset},
            {"name": "honua:studio:tag", "value": tag},
            {"name": "honua:studio:sourceCommit", "value": source_commit},
        ],
    }


def build_cesium_component(cesium_lock: dict, zip_names: set[str]) -> dict:
    if not any(name.startswith(CESIUM_PREFIX) for name in zip_names):
        raise ValueError(f"bundle zip is missing shipped Cesium assets under {CESIUM_PREFIX}")
    package = str(cesium_lock.get("package", "")).strip()
    version = str(cesium_lock.get("version", "")).strip()
    archive_sha256 = require_hex_sha256(cesium_lock.get("archiveSha256"), "cesium-extracted-tree.lock.json archiveSha256")
    tree_sha256 = require_hex_sha256(cesium_lock.get("treeSha256"), "cesium-extracted-tree.lock.json treeSha256")
    spdx = str((cesium_lock.get("license") or {}).get("spdx", "")).strip()
    if not package or not version:
        raise ValueError("cesium-extracted-tree.lock.json is missing package or version")
    purl = f"pkg:npm/{package}@{version}"
    component: dict = {
        "bom-ref": purl,
        "type": "library",
        "name": package,
        "version": version,
        "purl": purl,
        "hashes": [{"alg": "SHA-256", "content": archive_sha256}],
        "properties": [
            {"name": "honua:bundle:path", "value": CESIUM_PREFIX},
            {"name": "honua:cesium:treeSha256", "value": tree_sha256},
        ],
    }
    if spdx:
        component["licenses"] = [{"license": {"id": spdx}}]
    return component


def build_vendored_components(vendored_lock: dict, zip_names: set[str]) -> list[dict]:
    packages = vendored_lock.get("packages")
    if not isinstance(packages, dict):
        raise ValueError("vendored-assets.lock.json must contain a packages object")
    components: list[dict] = []
    for name in sorted(packages):
        if name == "cesium":
            continue
        entry = packages[name]
        version = str(entry.get("version", "")).strip()
        integrity = str(entry.get("integrity", "")).strip()
        spdx = str(entry.get("license", "")).strip()
        if not version or not integrity:
            raise ValueError(f"vendored-assets.lock.json entry {name!r} is missing version or integrity")
        expected_prefix = f"{VENDOR_PREFIX}{name}/"
        if not any(zname.startswith(expected_prefix) for zname in zip_names):
            raise ValueError(f"bundle zip is missing locked vendored asset under {expected_prefix}")
        purl = f"pkg:npm/{name}@{version}"
        component: dict = {
            "bom-ref": purl,
            "type": "library",
            "name": name,
            "version": version,
            "purl": purl,
            "hashes": [parse_sri_hash(integrity)],
            "properties": [{"name": "honua:bundle:path", "value": expected_prefix}],
        }
        if spdx:
            component["licenses"] = [{"license": {"id": spdx}}]
        components.append(component)
    return components


def resolve_wasm_runtime_version(licenses_sources: dict) -> str:
    files = licenses_sources.get("files")
    if not isinstance(files, list):
        raise ValueError("release/licenses/sources.json must contain a files list")
    for entry in files:
        match = WASM_SOURCE_RE.match(str(entry.get("source", "")))
        if match:
            return match.group(1)
    raise ValueError("release/licenses/sources.json is missing Microsoft.NETCore.App.Runtime.Mono.browser-wasm source")


def build_wasm_runtime_component(bundle_zip: ZipFile, licenses_sources: dict) -> dict:
    framework_entries = sorted(
        name for name in bundle_zip.namelist() if name.startswith(FRAMEWORK_PREFIX) and not name.endswith("/")
    )
    if f"{FRAMEWORK_PREFIX}blazor.webassembly.js" not in framework_entries:
        raise ValueError(f"bundle zip is missing {FRAMEWORK_PREFIX}blazor.webassembly.js")
    wasm_entries = [name for name in framework_entries if name.endswith(".wasm")]
    if not wasm_entries:
        raise ValueError(f"bundle zip contains no .wasm runtime/framework files under {FRAMEWORK_PREFIX}")

    manifest_hasher = hashlib.sha256()
    native_wasm_hash: str | None = None
    for name in framework_entries:
        file_digest = hashlib.sha256(bundle_zip.read(name)).hexdigest()
        manifest_hasher.update(f"{name}\0{file_digest}\n".encode("utf-8"))
        base = name.rsplit("/", 1)[-1]
        if base.startswith("dotnet.native") and base.endswith(".wasm") and native_wasm_hash is None:
            native_wasm_hash = file_digest

    runtime_version = resolve_wasm_runtime_version(licenses_sources)
    purl = f"pkg:nuget/Microsoft.NETCore.App.Runtime.Mono.browser-wasm@{runtime_version}"
    properties = [
        {"name": "honua:bundle:path", "value": FRAMEWORK_PREFIX},
        {"name": "honua:framework:fileCount", "value": str(len(framework_entries))},
        {"name": "honua:framework:wasmFileCount", "value": str(len(wasm_entries))},
        {"name": "honua:framework:manifestSha256", "value": manifest_hasher.hexdigest()},
    ]
    if native_wasm_hash:
        properties.append({"name": "honua:framework:nativeWasmSha256", "value": native_wasm_hash})

    return {
        "bom-ref": purl,
        "type": "framework",
        "name": "Microsoft.NETCore.App.Runtime.Mono.browser-wasm",
        "version": runtime_version,
        "purl": purl,
        "licenses": [{"license": {"id": "MIT"}}],
        "properties": properties,
    }


def _filter_build_only_nuget(components: list[dict]) -> tuple[list[dict], set[str]]:
    removed_refs: set[str] = set()
    kept: list[dict] = []
    for component in components:
        if is_build_only_nuget(component):
            ref = component.get("bom-ref")
            if isinstance(ref, str):
                removed_refs.add(ref)
            continue
        kept.append(component)
    if not any(is_nuget_component(c) for c in kept):
        raise ValueError("filtered SBOM has no shipped NuGet components remaining")
    return kept, removed_refs


def _build_dependencies(bom: dict, removed_refs: set[str], added_refs: list[str]) -> list[dict] | None:
    raw_deps = bom.get("dependencies")
    if not isinstance(raw_deps, list):
        return None
    cleaned_deps: list[dict] = []
    root_ref = (bom.get("metadata") or {}).get("component", {}).get("bom-ref")
    root_entry: dict | None = None
    for dep in raw_deps:
        ref = dep.get("ref")
        if ref in removed_refs:
            continue
        updated = dict(dep)
        if isinstance(updated.get("dependsOn"), list):
            updated["dependsOn"] = [target for target in updated["dependsOn"] if target not in removed_refs]
        cleaned_deps.append(updated)
        if root_ref and ref == root_ref:
            root_entry = updated
    if root_entry is not None and added_refs:
        depends_on = list(root_entry.get("dependsOn") or [])
        for ref in added_refs:
            if ref not in depends_on:
                depends_on.append(ref)
        root_entry["dependsOn"] = depends_on
    return cleaned_deps


def enrich_sbom(bom: dict, bundle_path: Path, locks: BundleLocks) -> dict:
    if bom.get("bomFormat") != "CycloneDX":
        raise ValueError("input SBOM must have bomFormat == 'CycloneDX'")
    components = bom.get("components")
    if not isinstance(components, list) or not any(is_nuget_component(c) for c in components):
        raise ValueError("input SBOM does not list the NuGet graph")

    kept_components, removed_refs = _filter_build_only_nuget(components)

    with ZipFile(bundle_path, "r") as bundle_zip:
        zip_names = set(bundle_zip.namelist())
        additions = [
            build_studio_component(locks.studio, zip_names),
            build_cesium_component(locks.cesium, zip_names),
            *build_vendored_components(locks.vendored, zip_names),
            build_wasm_runtime_component(bundle_zip, locks.licenses_sources),
        ]

    existing_purls = {str(c.get("purl", "")) for c in kept_components if c.get("purl")}
    existing_refs = {str(c.get("bom-ref", "")) for c in kept_components if c.get("bom-ref")}
    added_refs: list[str] = []
    for component in additions:
        if component["purl"] in existing_purls or component["bom-ref"] in existing_refs:
            continue
        kept_components.append(component)
        existing_purls.add(component["purl"])
        existing_refs.add(component["bom-ref"])
        added_refs.append(component["bom-ref"])

    enriched = dict(bom)
    enriched["components"] = kept_components
    updated_deps = _build_dependencies(bom, removed_refs, added_refs)
    if updated_deps is not None:
        enriched["dependencies"] = updated_deps
    return enriched


def validate_enriched_sbom(bom: dict) -> None:
    if bom.get("bomFormat") != "CycloneDX":
        raise ValueError("SBOM is not CycloneDX")
    components = bom.get("components")
    if not isinstance(components, list):
        raise ValueError("SBOM has no components list")

    nuget = [c for c in components if is_nuget_component(c)]
    if not nuget:
        raise ValueError("SBOM is missing the NuGet graph")
    build_only = [c.get("name") for c in nuget if is_build_only_nuget(c)]
    if build_only:
        raise ValueError(f"SBOM still contains build-only NuGet packages: {build_only}")

    for label, prefix in (
        ("Studio", "pkg:github/honua-io/honua-studio@"),
        ("Cesium", "pkg:npm/cesium@"),
    ):
        matches = [c for c in components if str(c.get("purl", "")).startswith(prefix)]
        if not matches:
            raise ValueError(f"SBOM is missing required bundled component: {label} ({prefix})")
        hashes = matches[0].get("hashes") or []
        if not any(h.get("alg") == "SHA-256" and re.fullmatch(r"[0-9a-f]{64}", str(h.get("content", ""))) for h in hashes):
            raise ValueError(f"SBOM component {label} is missing a SHA-256 hash")

    runtime_prefix = "pkg:nuget/Microsoft.NETCore.App.Runtime.Mono.browser-wasm@"
    runtime = [c for c in components if str(c.get("purl", "")).startswith(runtime_prefix)]
    if not runtime:
        raise ValueError(f"SBOM is missing required bundled component: .NET WASM runtime ({runtime_prefix})")
    props = {p.get("name"): p.get("value") for p in runtime[0].get("properties") or []}
    require_hex_sha256(props.get("honua:framework:manifestSha256"), ".NET WASM runtime manifestSha256")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sbom", type=Path, required=True, help="Input CycloneDX JSON SBOM")
    parser.add_argument("--validate-only", action="store_true", help="Validate an already-enriched SBOM and exit")
    parser.add_argument("--bundle", type=Path, help="Packaged Console static bundle ZIP")
    parser.add_argument("--studio-lock", type=Path, default=Path("scripts/studio-release.lock.json"))
    parser.add_argument("--cesium-lock", type=Path, default=Path("scripts/cesium-extracted-tree.lock.json"))
    parser.add_argument("--vendored-lock", type=Path, default=Path("scripts/vendored-assets.lock.json"))
    parser.add_argument("--licenses-sources", type=Path, default=Path("release/licenses/sources.json"))
    parser.add_argument("--output", type=Path, help="Output enriched CycloneDX JSON SBOM")
    args = parser.parse_args()

    bom = json.loads(args.sbom.read_text(encoding="utf-8"))
    if args.validate_only:
        validate_enriched_sbom(bom)
        return
    if not args.bundle or not args.output:
        parser.error("--bundle and --output are required unless --validate-only is set")

    locks = BundleLocks(
        studio=json.loads(args.studio_lock.read_text(encoding="utf-8")),
        cesium=json.loads(args.cesium_lock.read_text(encoding="utf-8")),
        vendored=json.loads(args.vendored_lock.read_text(encoding="utf-8")),
        licenses_sources=json.loads(args.licenses_sources.read_text(encoding="utf-8")),
    )
    enriched = enrich_sbom(bom, args.bundle, locks)
    validate_enriched_sbom(enriched)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(enriched, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
