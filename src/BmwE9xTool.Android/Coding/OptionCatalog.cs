namespace BmwE9xTool.Coding;

public sealed record OptionDefinition(string Code, string Name);

public static class OptionCatalog
{
    public static readonly IReadOnlyList<OptionDefinition> Known =
    [
        new("663", "BMW Professional radio"),
        new("6FL", "USB / audio interface"),
        new("6NN", "Bluetooth hands-free"),
        new("644", "Bluetooth phone preparation"),
        new("609", "Navigation Professional"),
        new("676", "HiFi loudspeaker system"),
        new("677", "HiFi Professional / Logic 7")
    ];

    public static string Normalize(string code)
    {
        var value = code.Trim().TrimStart('$').ToUpperInvariant();
        if (value.Length is < 3 or > 4 || !value.All(char.IsLetterOrDigit))
            throw new ArgumentException("Invalid BMW SA code.", nameof(code));
        return value;
    }
}
