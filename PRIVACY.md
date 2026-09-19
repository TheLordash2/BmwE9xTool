# Privacy

BmwE9xTool is designed to operate locally.

- No VIN or other vehicle identifier is hardcoded in the source repository.
- The expected VIN used as a write-safety check is entered at runtime.
- The configured expected VIN is not written to the session log.
- Diagnostic data is not uploaded by the application.
- Backups and session logs are stored in Android app-private storage.
- The application contains no analytics, advertising or telemetry code.

A VIN read from the vehicle can appear on the device screen and in locally created backup filenames/data. Do not publish logs or backups without reviewing them first.
