# BmwE9xTool

Android diagnostic, FA/VO and classic NCS-style coding utility for BMW E8x/E9x vehicles using a USB K+DCAN/FTDI adapter and the open-source EdiabasLib transport.

The app is intentionally data-driven. BMW EDIABAS/SP-Daten files are imported by the user at runtime and are **not** distributed in this repository.

## Implemented

### Connection / diagnostics

- Android USB host support for FTDI/K+DCAN adapters
- EDIABAS initialization through EdiabasLib
- vehicle VIN read from E-series identity modules
- common E90/E9x fault-memory scan with `FS_LESEN`
- deep fault scan driven by imported `E89SGFAM.DAT`
- session logging without hard-coded vehicle identifiers

### FA / VO

- read FA/VO from CAS and other FA holders discovered from E89 SGFAM data
- compare redundant stored FA copies
- display current SA option codes and descriptions
- generic add/remove staging for SA codes such as `$6FL`
- preserve non-SA FA tokens while editing
- convert edited FA with `FA.PRG / FA_STREAM_FOR_ECU`
- write supported FA holders with `C_FA_AUFTRAG`
- write redundant holders before CAS
- immediate read-back and normalized FA comparison after every write

### NCS/DATEN parsing

- BMW binary DATEN frame parser
- E89 SGFAM parser
- NCS AT description parser
- `SWTFSWxx.DAT` / `SWTPSWxx.DAT` lookup parser
- CABD `.Cxx` parser for:
  - `PARZUWEISUNG_FSW`
  - `PARZUWEISUNG_PSW1/2`
  - `CODIERDATENBLOCK`
  - `SPEICHERORG`
  - `SGID_CODIERINDEX`
- SP-Daten ZIP importer including `.C00..​.CFF` CABD files

### RAD2 coding

The current coding engine is focused on the late E-series Professional Radio / RAD2 family.

It can:

- identify the reachable radio SGBD
- read `ID_COD_INDEX`
- locate a compatible CABD for that coding index
- resolve FSW/PSW keywords from SWT data
- read coding bytes through `C_C_LESEN`
- decode enumerated FSW values
- display `USB_RAD2`, `BLUETOOTH_RAD2`, `ULF_ECE` when present
- edit any enumerated RAD2 FSW/PSW exposed by the loaded CABD
- apply CABD masks without overwriting unrelated bits
- write coding ranges with `C_C_AUFTRAG`
- immediately re-read and verify the requested FSW value
- create a private binary coding backup before a RAD2 write

A convenience action is included for:

```text
USB_RAD2 = aktiv
```

The generic RAD2 editor can also be used for other enumerated FSW/PSW pairs present in the loaded CABD.

## Safety model

Writes are deliberately gated. The app requires:

- VIN read from the connected vehicle
- locally entered expected VIN to match
- battery voltage to be available and above the configured minimum
- a backup
- explicit write-mode arming
- explicit acknowledgement that the direct FA/RAD2 writer is not yet physically validated

The expected VIN is entered only on the Android device. No VIN is compiled into the app or committed to this repository. Backup directory names use a SHA-256-derived vehicle key rather than the VIN.

## Data import

Use **Import ECU / SP-Daten ZIP** and select a compatible E89/E9x data set that you are licensed to use.

The importer separates data into:

- `ecu/` — EDIABAS PRG/GRP files
- `ncs/daten/` — E89 NCS tables and CABD `.Cxx` files
- `ncs/sgdat/` — IPO files

At minimum, diagnosis requires appropriate EDIABAS ECU group/variant files. FA conversion requires `FA.PRG`. RAD2 coding additionally requires the matching E89 SWT and CABD data.

## Recommended first vehicle test

Do the first session read-only:

1. Import the E89 data ZIP.
2. Attach the USB K+DCAN cable through OTG.
3. Grant USB permission.
4. Initialize EDIABAS.
5. Enter the expected VIN locally.
6. Read VIN.
7. Read FA/VO.
8. Run the fault scan.
9. Read RAD2 coding.

Only enable write mode after the read path has been confirmed against the physical vehicle.

## Build

GitHub Actions performs:

- privacy scan
- core smoke tests
- Android workload installation
- EdiabasLib bootstrap
- Release APK build
- APK artifact upload

Local build:

```bash
bash scripts/bootstrap-ediabas.sh
dotnet workload install android
dotnet build src/BmwE9xTool.Android/BmwE9xTool.Android.csproj \
  -c Release \
  -f net10.0-android36.1 \
  -p:EnableAndroidTargets=true \
  -p:EnableWindowsTargeting=true
```

EdiabasLib is pinned by the bootstrap script to a known source revision for reproducible builds.

## Validation status

The Android project and offline tests build successfully in CI. The FA and RAD2 write paths have defensive backups and read-back verification, but they are **not yet validated on the physical car**. That is the only remaining step before removing the experimental label from those write operations.
