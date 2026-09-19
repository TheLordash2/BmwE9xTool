#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$ROOT/vendor/ediabaslib"
COMMIT="00b53173c84db420c3bb20711a71b8dd92df0f6e"
if [ ! -d "$DEST/.git" ]; then
  mkdir -p "$(dirname "$DEST")"
  git clone https://github.com/uholeschak/ediabaslib.git "$DEST"
fi
git -C "$DEST" fetch --all --tags
git -C "$DEST" checkout "$COMMIT"
echo "EdiabasLib pinned to $COMMIT"
