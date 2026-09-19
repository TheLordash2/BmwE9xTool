#!/usr/bin/env python3
"""
Build a trimmed E8x/E9x starter-data folder from a locally owned BMW Standard
Tools / SP-Daten installation.

No BMW data is included in this repository. This script only copies files from
paths supplied by the user.

Output layout:
  <output>/
    ecu/
    ncs/daten/
    ncs/sgdat/
"""

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path

CXX_RE = re.compile(r"\.c[0-9a-f]{2}$", re.IGNORECASE)
SGFAM_LINE_RE = re.compile(r"^S\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)", re.IGNORECASE)

DATEN_EXTS = {
    ".dat", ".000", ".asc", ".zus", ".m00", ".ssd", ".ini", ".txt"
}


def find_case_insensitive(folder: Path, stem: str, extensions: tuple[str, ...]) -> Path | None:
    stem_l = stem.lower()
    ext_l = {e.lower() for e in extensions}
    for p in folder.iterdir():
        if p.is_file() and p.stem.lower() == stem_l and p.suffix.lower() in ext_l:
            return p
    return None


def copy_one(src: Path | None, dst_dir: Path, seen: set[Path]) -> int:
    if not src or not src.exists():
        return 0
    key = src.resolve()
    if key in seen:
        return 0
    dst_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst_dir / src.name.lower())
    seen.add(key)
    return 1


def parse_sgfam(path: Path) -> tuple[set[str], set[str]]:
    sgbd: set[str] = set()
    cabd: set[str] = set()

    for original in path.read_text(errors="ignore").splitlines():
        line = original.strip()
        if not line or line.startswith(";"):
            continue
        line = line.split(";", 1)[0].strip()
        m = SGFAM_LINE_RE.match(line)
        if not m:
            continue

        # S <logical-name> <CABD> <SGBD> <zcs-holder> <fa-holder>
        cabd.add(m.group(2))
        sgbd.add(m.group(3))

    return sgbd, cabd


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ecu-dir", required=True, type=Path,
                    help="EDIABAS ECU directory containing .PRG/.GRP files")
    ap.add_argument("--e89-daten-dir", required=True, type=Path,
                    help="E89 SP-Daten daten directory")
    ap.add_argument("--sgdat-dir", required=False, type=Path,
                    help="NCS SGDAT directory containing .IPO files")
    ap.add_argument("--output", required=True, type=Path)
    args = ap.parse_args()

    ecu_dir = args.ecu_dir
    daten_dir = args.e89_daten_dir
    sgdat_dir = args.sgdat_dir
    out = args.output

    if not ecu_dir.is_dir():
        raise SystemExit(f"ECU directory not found: {ecu_dir}")
    if not daten_dir.is_dir():
        raise SystemExit(f"E89 daten directory not found: {daten_dir}")

    out_ecu = out / "ecu"
    out_daten = out / "ncs" / "daten"
    out_sgdat = out / "ncs" / "sgdat"
    seen: set[Path] = set()
    count = 0

    sgfam = next((p for p in daten_dir.iterdir()
                  if p.is_file() and p.name.lower() == "e89sgfam.dat"), None)
    if not sgfam:
        raise SystemExit("E89SGFAM.DAT was not found in the supplied E89 daten directory.")

    sgbds, cabds = parse_sgfam(sgfam)

    # Copy the complete E89 NCS daten folder content needed by parsers/rules.
    for p in daten_dir.iterdir():
        if not p.is_file():
            continue
        if p.suffix.lower() in DATEN_EXTS or CXX_RE.search(p.suffix):
            count += copy_one(p, out_daten, seen)

    # Core helpers needed by FA/VO conversion.
    for helper in ("FA",):
        count += copy_one(find_case_insensitive(ecu_dir, helper, (".prg", ".grp")), out_ecu, seen)

    # ECU group/variant files referenced by E89 SGFAM.
    for name in sorted(sgbds):
        for ext in (".prg", ".grp"):
            count += copy_one(find_case_insensitive(ecu_dir, name, (ext,)), out_ecu, seen)

    # Useful generic E9x group SGBDs used by the app's fallback scanner.
    for name in (
        "D_CAS", "D_MOTOR", "D_EKP", "D_DSC", "D_SIM", "D_MMI",
        "D_MOSTGW", "D_KLIMA", "D_KBM", "D_ZGM", "D_KOMBI",
        "D_PDC", "D_RLS", "D_EPS", "D_ISPB", "D_FZD", "FRM_87",
        "RAD2", "RAD2_GW", "MCGWPL2"
    ):
        for ext in (".prg", ".grp"):
            count += copy_one(find_case_insensitive(ecu_dir, name, (ext,)), out_ecu, seen)

    # CABD IPO dispatchers referenced by SGFAM, when a SGDAT path is supplied.
    if sgdat_dir and sgdat_dir.is_dir():
        for name in sorted(cabds):
            count += copy_one(find_case_insensitive(sgdat_dir, name, (".ipo",)), out_sgdat, seen)

    print(f"Starter pack created at: {out}")
    print(f"Files copied: {count}")
    print(f"E89 SGBD references: {len(sgbds)}")
    print(f"E89 CABD references: {len(cabds)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
