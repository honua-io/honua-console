#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if [[ "${1:-}" == --prepared-cache ]]; then
    if [[ $# -lt 3 || ! -d "$2" || ! "$2" =~ ^(/home/ubuntu/ext)?/tmp/honua-console-studio\.[A-Za-z0-9]+$ ]]; then
        echo 'usage: remote-console-dotnet.sh --prepared-cache CACHE DOTNET_ARGS...' >&2
        exit 2
    fi
    export HONUA_STUDIO_RELEASE_CACHE="$2"
    trap 'rm -rf -- "${HONUA_STUDIO_RELEASE_CACHE:?}"' EXIT
    shift 2
    dotnet "$@"
    exit
fi

if [[ $# -eq 0 ]]; then
    echo 'usage: scripts/remote-console-dotnet.sh DOTNET_ARGS...' >&2
    exit 2
fi

cache_root="${HONUA_STUDIO_RELEASE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/honua-console/studio}"
mkdir -p "$cache_root"
HONUA_STUDIO_RELEASE_CACHE="$cache_root" node scripts/fetch-studio-release.mjs --force

# The transport discovers external paths in arguments and cannot transfer paths with spaces.
transfer_cache="$(mktemp -d /tmp/honua-console-studio.XXXXXXXX)"
trap 'rm -rf -- "${transfer_cache:?}"' EXIT
cp -a "$cache_root/." "$transfer_cache/"
remote-run bash scripts/remote-console-dotnet.sh --prepared-cache "$transfer_cache" "$@"
