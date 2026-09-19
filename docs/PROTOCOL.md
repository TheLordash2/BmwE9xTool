# E9x protocol notes

This project uses the open-source EdiabasLib transport and BMW EDIABAS ECU data.

## BN2000 identity jobs

The EdiabasLib vehicle detector documents the E9x/BN2000 read path:

- VIN: `D_CAS / STATUS_FAHRGESTELLNUMMER` -> `FGNUMMER`
- fallback VIN: `D_LM / READ_FVIN`, `FRM_87 / READ_FVIN`, `D_ZGM / C_FG_LESEN`
- FA: `D_CAS / C_FA_LESEN` -> `FAHRZEUGAUFTRAG`
- FA parser: `FA / FA_STREAM2STRUCT`, argument `1;<FAHRZEUGAUFTRAG>`

The parsed FA exposes `STANDARD_FA`, `BR`, `C_DATE`, `C_TYP`, `LACK`,
`POLSTER`, `SA_n`, `HO_WORT_n`, `E_WORT_n` and `ZUSBAU_n`.

## Fault memory

The common EDIABAS job is `FS_LESEN`. The app resolves each E90 group SGBD
and only executes the job when `IsJobExisting("FS_LESEN")` is true.

## FA writes

Do not confuse `C_FA_LESEN` with NCS Expert's `FA_WRITE`.

NCS Expert uses per-ECU CABD/IPO dispatchers. Reverse-engineering work in the
open-source ncsx project shows that `FA_WRITE` requires the host to seed the
CABD parameter `FA_STREAM` and then execute the appropriate BMW IPO
dispatcher. The correct target(s) are discovered from chassis SGFAM/CABD data.

For that reason the current write engine is fail-closed. The next coding-engine
milestone is to port/integrate the IPO/CABD runtime rather than invent an
unverified direct CAS write.
