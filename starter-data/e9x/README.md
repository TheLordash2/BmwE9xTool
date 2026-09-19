# Local E9x starter-data staging

This directory is intentionally empty in the public repository.

BMW EDIABAS/SP-Daten files are not redistributed here. If you have a lawful local copy, build a trimmed E9x starter package with:

```bash
python tools/build_e9x_starter_pack.py \
  --ecu-dir /path/to/EDIABAS/ECU \
  --e89-daten-dir /path/to/SP-Daten-E89/daten \
  --sgdat-dir /path/to/NCSEXPER/SGDAT \
  --output starter-data/e9x
```

Then build the APK with:

```bash
dotnet build src/BmwE9xTool.Android/BmwE9xTool.Android.csproj \
  -c Release \
  -f net10.0-android36.1 \
  -p:E9xStarterDataDir="$PWD/starter-data/e9x" \
  -p:EnableAndroidTargets=true \
  -p:EnableWindowsTargeting=true
```

The resulting APK will contain the supplied starter package under Android assets. On the phone, press **Install bundled E9x starter data** once.

The payload files remain ignored by Git so they are not accidentally published.
