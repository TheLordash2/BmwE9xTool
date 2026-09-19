#!/usr/bin/env bash
set -euo pipefail

# Reject likely full BMW VINs in tracked source/docs. Runtime values are allowed
# only on the device and must never be committed.
if grep -RInE --exclude-dir=.git --exclude='privacy-check.sh' '\b(WBA|WBS|WBY)[A-HJ-NPR-Z0-9]{14}\b' .; then
  echo "Potential BMW VIN found in repository content."
  exit 1
fi

echo "Privacy scan passed."
