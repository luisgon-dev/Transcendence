#!/usr/bin/env bash
# The historical bulk path uses the same bounded, resumable, verified archive contract.
# Archives now live under <NAS_DIR>/<patch>/chunk-*/ instead of overwriting one bulk export.
set -Eeuo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec bash "${script_dir}/archive-old-patches.sh" "$@"
