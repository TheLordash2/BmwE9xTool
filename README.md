# BmwE9xTool

Experimental Android diagnostic/coding tool for BMW E8x/E9x vehicles using a USB K+DCAN/FTDI interface.

## Current milestone: v0.1 read-only

The first milestone intentionally performs **no ECU writes**.

Goals:
- Detect USB diagnostic adapters on Android
- Request USB permission
- Show VID/PID and descriptors
- Establish a clean transport abstraction for BMW K-Line/D-CAN
- Read CAS VIN once the EDIABAS transport is integrated
- Keep detailed session logs

Development vehicle configuration:
- VIN is configured locally at runtime and is never committed
- Platform family: BMW E8x/E9x / E89 daten family

## Planned milestones
1. v0.1 USB adapter detection + read-only connection shell
2. v0.2 CAS identification + VIN read
3. v0.3 ECU discovery + fault-memory scan
4. v0.4 Read FA/VO from CAS and FRM/NFRM and compare
5. v0.5 Decode and display SA option codes
6. v0.6 Safe in-memory VO editing with automatic backups
7. v0.7 Verified CAS/FRM FA writes
8. v0.8 RAD2 coding support
9. v1.0 Guided retrofit workflows

## Safety model
- Read-only by default
- No CAS/FRM/RAD2 write support until read/backup/verify paths are proven
- VIN-gated future write operations
- Read-back verification after every future write
- No blind automatic retries
