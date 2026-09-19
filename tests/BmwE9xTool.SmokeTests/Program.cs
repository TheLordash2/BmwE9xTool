using BmwE9xTool.Coding;
using BmwE9xTool.Vehicle;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var original = new VehicleOrder(
    Source: "TEST",
    RawFa: "raw",
    StandardFa: "E89_#0911*KG91%0A52&NCSW|1234567$1CA$249$663+K123-E456",
    Version: "02",
    Chassis: "E89",
    ProductionDate: "0911",
    TypeCode: "KG91",
    Paint: "0A52",
    Upholstery: "NCSW",
    Sa: new[] { "1CA", "249", "663" },
    HoWords: new[] { "K123" },
    EWords: new[] { "E456" },
    ZbWords: new[] { "1234567" });

var editor = new FaEditor(original);
Require(editor.Add("6FL"), "Adding 6FL should change the FA.");
var withUsb = editor.BuildStandardFa();

Require(withUsb.Contains("$6FL", StringComparison.Ordinal), "6FL missing.");
Require(withUsb.Contains("%0A52", StringComparison.Ordinal), "Paint token changed.");
Require(withUsb.Contains("&NCSW", StringComparison.Ordinal), "Upholstery token changed.");
Require(withUsb.Contains("|1234567", StringComparison.Ordinal), "ZUSBAU token changed.");
Require(withUsb.Contains("+K123", StringComparison.Ordinal), "HO word changed.");
Require(withUsb.Contains("-E456", StringComparison.Ordinal), "E word changed.");

Require(editor.Remove("663"), "Removing 663 should change the FA.");
var without663 = editor.BuildStandardFa();
Require(!without663.Contains("$663", StringComparison.Ordinal), "663 was not removed.");
Require(without663.Contains("$6FL", StringComparison.Ordinal), "6FL was lost after removing another option.");

Console.WriteLine("BmwE9xTool smoke tests passed.");
