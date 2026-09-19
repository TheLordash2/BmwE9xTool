using BmwE9xTool.Core;
using BmwE9xTool.Ediabas;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

/// <summary>
/// Experimental FA write engine.
///
/// Reading/diagnostics use EDIABAS PRG jobs directly. NCS identity writes are
/// dispatched by BMW CABD IPO scripts and require FA_STREAM host seeding.
/// Until the IPO/CABD runtime is integrated and verified on hardware, this
/// class deliberately refuses to write. This prevents a guessed CAS/FRM write
/// sequence from being presented as safe.
/// </summary>
public sealed class FaWriteEngine
{
    private readonly AppLog _log;
    public FaWriteEngine(AppLog log) => _log = log;

    public bool HardwareVerified => false;

    public Task WriteAsync(VehicleOrder original, string modifiedStandardFa, CancellationToken ct = default)
    {
        _log.Add("FA WRITE BLOCKED: CABD/IPO coding runtime is not hardware-verified.");
        throw new InvalidOperationException(
            "FA write is intentionally blocked until the BMW CABD/IPO FA_WRITE path is integrated and read-back verified. " +
            "The staged FA and backup are safe; do not bypass this guard with a guessed raw ECU write.");
    }
}
