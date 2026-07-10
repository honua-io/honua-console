#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
vendor_dir="$repo_root/contracts/honua-server"
map_file="$vendor_dir/ops-parity-map.yaml"
metadata_file="$vendor_dir/ops-parity-map.source.json"
source_repository="honua-io/honua-server"
source_path="tests/dotnet/Honua.Ai.Tests/ConformanceSchemas/geospatial-mcp/ops-parity-map.yaml"
mode="check"
source_commit=""

usage() {
  echo "Usage: $0 --check | --update <40-character-honua-server-commit>" >&2
}

case "${1:---check}" in
  --check)
    [[ $# -eq 1 || $# -eq 0 ]] || { usage; exit 2; }
    [[ -f "$metadata_file" ]] || { echo "Missing source metadata: $metadata_file" >&2; exit 1; }
    source_commit=$(sed -nE 's/^[[:space:]]*"commit":[[:space:]]*"([0-9a-f]{40})",?[[:space:]]*$/\1/p' "$metadata_file")
    ;;
  --update)
    [[ $# -eq 2 ]] || { usage; exit 2; }
    mode="update"
    source_commit=$2
    ;;
  --help|-h)
    usage
    exit 0
    ;;
  *)
    usage
    exit 2
    ;;
esac

[[ "$source_commit" =~ ^[0-9a-f]{40}$ ]] || {
  echo "Source ref must be a full lowercase 40-character honua-server commit SHA." >&2
  exit 2
}

tmp_dir=$(mktemp -d)
trap 'rm -rf "$tmp_dir"' EXIT
downloaded_map="$tmp_dir/ops-parity-map.yaml"
source_url="https://raw.githubusercontent.com/$source_repository/$source_commit/$source_path"

curl --fail --location --silent --show-error "$source_url" --output "$downloaded_map"
grep -q '^routes:$' "$downloaded_map" || {
  echo "Downloaded file is not the expected ops parity map: $source_url" >&2
  exit 1
}

if [[ "$mode" == "check" ]]; then
  [[ -f "$map_file" ]] || { echo "Missing vendored parity map: $map_file" >&2; exit 1; }
  cmp --silent "$downloaded_map" "$map_file" || {
    echo "Vendored ops parity map differs from honua-server commit $source_commit." >&2
    echo "Run scripts/sync-ops-parity-map.sh --update $source_commit after reviewing the upstream change." >&2
    exit 1
  }

  grep -q '"repository": "honua-io/honua-server"' "$metadata_file"
  grep -q '"path": "tests/dotnet/Honua.Ai.Tests/ConformanceSchemas/geospatial-mcp/ops-parity-map.yaml"' "$metadata_file"
  echo "Vendored ops parity map matches honua-server@$source_commit."
  exit 0
fi

mkdir -p "$vendor_dir"
install -m 0644 "$downloaded_map" "$map_file"
printf '{\n  "repository": "%s",\n  "commit": "%s",\n  "path": "%s"\n}\n' \
  "$source_repository" "$source_commit" "$source_path" > "$tmp_dir/ops-parity-map.source.json"
install -m 0644 "$tmp_dir/ops-parity-map.source.json" "$metadata_file"
echo "Vendored ops parity map from honua-server@$source_commit."
