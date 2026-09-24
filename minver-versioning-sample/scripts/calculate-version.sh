#!/usr/bin/env bash
set -euo pipefail

minver_cli="${MINVER_CLI:-minver}"
if [[ "$minver_cli" == "minver" && ! -e "$minver_cli" && -f ".tools/minver/minver" ]]; then
	minver_cli=".tools/minver/minver"
elif [[ ! -e "$minver_cli" && -f "${minver_cli}.exe" ]]; then
	minver_cli="${minver_cli}.exe"
fi
if [[ "$minver_cli" == ".tools/minver/minver" && ! -e "$minver_cli" && -f "${minver_cli}.exe" ]]; then
	minver_cli="${minver_cli}.exe"
fi
exec "$minver_cli" -t v -m 1.0 -p preview "$@"
