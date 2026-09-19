using Android.Hardware.Usb;
using BmwE9xTool.Core;
using BmwE9xTool.Vehicle;
using EdiabasLib;

namespace BmwE9xTool.Ediabas;

public sealed class EdiabasSession : IDisposable
{
    private readonly AppPaths _paths;
    private readonly AppLog _log;
    private readonly UsbManager _usbManager;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private EdiabasNet? _ediabas;
    private EdInterfaceObd? _interface;

    public EdiabasSession(AppPaths paths, AppLog log, UsbManager usbManager)
    {
        _paths = paths;
        _log = log;
        _usbManager = usbManager;
    }

    public bool Initialized => _ediabas != null;

    public void Initialize()
    {
        DisposeEdiabas();

        _interface = new EdInterfaceObd
        {
            IfhName = "STD:OBD",
            ApplicationName = "BmwE9xTool",
            ComPort = EdFtdiInterface.PortId + "0",
            ConnectParameter = new EdFtdiInterface.ConnectParameterType(_usbManager)
        };

        _ediabas = new EdiabasNet
        {
            EdInterfaceClass = _interface
        };

        _ediabas.SetConfigProperty("EcuPath", _paths.Ecu);
        _ediabas.SetConfigProperty("Interface", "STD:OBD");
        _ediabas.SetConfigProperty("ObdComPort", EdFtdiInterface.PortId + "0");
        _log.Add("EDIABAS initialized: STD:OBD / FTDI0");
    }

    public double? BatteryVoltage()
    {
        var value = _interface?.BatteryVoltage ?? long.MinValue;
        if (value == long.MinValue || value <= 0) return null;
        return value / 1000.0;
    }

    public async Task<bool> JobExistsAsync(string sgbd, string job, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            EnsureInitialized();
            _ediabas!.ResolveSgbdFile(sgbd);
            return _ediabas.IsJobExisting(job);
        }
        finally { _mutex.Release(); }
    }

    public async Task<JobResult> RunJobAsync(
        string sgbd,
        string job,
        string? args = null,
        byte[]? binaryArgs = null,
        string? requestedResults = null,
        CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            EnsureInitialized();
            _log.Add($"JOB {sgbd}/{job}" + (string.IsNullOrEmpty(args) ? "" : $" args={args}"));
            _ediabas!.ResolveSgbdFile(sgbd);
            _ediabas.ArgString = args ?? string.Empty;
            _ediabas.ArgBinary = binaryArgs;
            _ediabas.ArgBinaryStd = null;
            _ediabas.ResultsRequests = requestedResults ?? string.Empty;
            _ediabas.ExecuteJob(job);

            var sets = new List<JobSet>();
            if (_ediabas.ResultSets != null)
            {
                foreach (var source in _ediabas.ResultSets)
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in source) dict[kv.Key] = kv.Value.OpData;
                    sets.Add(new JobSet(dict));
                }
            }

            string? status = null;
            foreach (var set in sets)
            {
                status ??= set.String("JOB_STATUS");
            }

            _log.Add($"JOB {sgbd}/{job} -> {status ?? "no JOB_STATUS"} ({sets.Count} set(s))");
            return new JobResult(sgbd, job, sets, status);
        }
        catch (Exception ex)
        {
            _log.Add($"JOB {sgbd}/{job} FAILED: {ex.Message}");
            throw;
        }
        finally { _mutex.Release(); }
    }

    private void EnsureInitialized()
    {
        if (_ediabas == null) throw new InvalidOperationException("EDIABAS session is not initialized.");
        if (!_paths.HasMinimumEcuData) throw new InvalidOperationException("BMW ECU data missing. Import EDIABAS/SP-Daten ECU files first.");
    }

    private void DisposeEdiabas()
    {
        try { _ediabas?.Dispose(); } catch { }
        _ediabas = null;
        _interface = null;
    }

    public void Dispose()
    {
        DisposeEdiabas();
        _mutex.Dispose();
    }
}
