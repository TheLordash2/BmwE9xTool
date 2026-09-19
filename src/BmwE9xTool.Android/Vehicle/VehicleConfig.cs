namespace BmwE9xTool.Vehicle;

public static class VehicleConfig
{
    public const string Series = "E90";
    public const string NcsChassis = "E89";

    // Expected VIN is deliberately runtime-only. Never commit a vehicle identifier.
    public static string? ExpectedVin { get; private set; }

    public static void ConfigureExpectedVin(string? vin)
    {
        ExpectedVin = string.IsNullOrWhiteSpace(vin) ? null : vin.Trim().ToUpperInvariant();
    }

    public static bool VinMatches(string? vin) =>
        !string.IsNullOrEmpty(ExpectedVin) &&
        string.Equals(vin?.Trim(), ExpectedVin, StringComparison.OrdinalIgnoreCase);
}
