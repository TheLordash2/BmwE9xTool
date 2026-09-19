# BmwE9xTool

Android diagnostic and FA/VO utility for BMW E8x/E9x vehicles using a USB K+DCAN/FTDI adapter and the open-source EdiabasLib transport.

## Current scope

The active app is the .NET Android project under `src/BmwE9xTool.Android`.

Implemented:

- USB host access for FTDI/K+DCAN adapters
- EDIABAS initialization through EdiabasLib
- VIN read from E9x identity modules
- FA/VO read from CAS and other available FA holders
- comparison of multiple stored FA copies
- display of current SA option codes
- generic add/remove staging for SA codes such as `$6FL`
- common E90 fault-memory scan using `FS_LESEN`
- automatic private backup before writes
- battery-voltage, VIN-match and explicit-user write gates
- experimental CAS/FRM FA write using `FA_STREAM_FOR_ECU` + `C_FA_AUFTRAG`
- immediate FA read-back verification after every write target
- GitHub Actions Android build with APK artifact upload

## Privacy

No vehicle VIN is committed to this repository. The expected VIN is entered locally at runtime. The app does not log that configured expected VIN. VIN and FA backups created by the app remain in Android app-private storage unless the user deliberately exports them.

## Data files

BMW EDIABAS/SP-Daten files are not distributed in this repository. In the app, choose **Import ECU / SP-Daten ZIP** and provide files you are licensed to use. The importer accepts the relevant `.PRG`, `.GRP`, NCS/DATEN and related files.

At minimum, E9x diagnosis requires the appropriate ECU group/variant PRG files, including the CAS group data. FA conversion also requires `FA.PRG`.

## Typical workflow

1. Import compatible ECU/SP-Daten files.
2. Attach the USB K+DCAN adapter using USB OTG.
3. Grant Android USB permission.
4. Initialize EDIABAS.
5. Enter the expected VIN locally.
6. Read the VIN from the car and confirm the safety match.
7. Read FA/VO copies.
8. Scan faults if desired.
9. Stage an SA-code addition or removal.
10. Create a backup.
11. Only if intentionally testing the write path: arm write mode and accept the experimental FA-write acknowledgement.
12. Write. The app verifies each written FA by reading it back.

## Important limitation

Changing the FA/VO tells the vehicle what equipment is installed. It does **not yet reproduce NCS Expert's full `SG_CODIEREN`/DATEN coding-data generation** for an arbitrary ECU. For the Professional Radio retrofit, the remaining later milestone is default-coding the RAD2 from the updated VO or implementing the required RAD2 parameter coding.

The FA writer remains labelled experimental until validated on physical E9x hardware.

## Build

The repository includes a GitHub Actions workflow. A successful workflow run uploads the generated APK as the `BmwE9xTool-android` artifact.

Local build:

```bash
bash scripts/bootstrap-ediabas.sh
dotnet workload install android
dotnet build src/BmwE9xTool.Android/BmwE9xTool.Android.csproj -c Release
```

EdiabasLib is pinned by the bootstrap script to a known source revision to keep builds reproducible.
