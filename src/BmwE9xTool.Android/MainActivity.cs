using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using BmwE9xTool.Coding;
using BmwE9xTool.Core;
using BmwE9xTool.Data;
using BmwE9xTool.Diagnostics;
using BmwE9xTool.Ediabas;
using BmwE9xTool.Safety;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool;

[Activity(Label = "BMW E9x Tool", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const int PickDataZipRequest = 9001;
    private const string UsbPermissionAction = "com.thelordash2.bmwe9xtool.USB_PERMISSION";

    private AppPaths _paths = null!;
    private AppLog _log = null!;
    private UsbManager _usb = null!;
    private EdiabasSession _session = null!;
    private VehicleIdentityService _identity = null!;
    private FaService _faService = null!;
    private FaultService _faults = null!;
    private BackupService _backups = null!;
    private FaWriteEngine _writer = null!;
    private UsbPermissionReceiver? _usbReceiver;

    private TextView _status = null!;
    private TextView _output = null!;
    private TextView _logView = null!;
    private Button _connect = null!;
    private Button _readVin = null!;
    private Button _readFa = null!;
    private Button _scanFaults = null!;
    private Button _stage6Fl = null!;
    private Button _backup = null!;
    private Button _write = null!;

    private string? _vin;
    private IReadOnlyList<VehicleOrder>? _orders;
    private FaEditor? _editor;
    private string? _backupPath;
    private bool _writeArmed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _paths = new AppPaths(FilesDir!.AbsolutePath);
        _log = new AppLog();
        _usb = (UsbManager)GetSystemService(UsbService)!;
        _session = new EdiabasSession(_paths, _log, _usb);
        _identity = new VehicleIdentityService(_session, _log);
        _faService = new FaService(_session, _log);
        _faults = new FaultService(_session, _log);
        _backups = new BackupService(_paths, _log);
        _writer = new FaWriteEngine(_log);

        _log.Changed += () => RunOnUiThread(() => _logView.Text = _log.Text);
        BuildUi();
        RegisterUsbReceiver();
        RefreshState();

        _log.Add("BMW E9x Tool started.");
        _log.Add("Write engine is fail-closed until CABD/IPO path is verified.");
    }

    protected override void OnDestroy()
    {
        if (_usbReceiver != null)
        {
            try { UnregisterReceiver(_usbReceiver); } catch { }
        }
        _session.Dispose();
        base.OnDestroy();
    }

    private void BuildUi()
    {
        var scroll = new ScrollView(this);
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(32));
        scroll.AddView(root);

        var title = new TextView(this) { Text = "BMW E9x Tool" };
        title.TextSize = 26;
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        root.AddView(title);

        var subtitle = new TextView(this)
        {
            Text = "E8x/E9x diagnostics + FA/VO tooling"
        };
        subtitle.SetPadding(0, Dp(4), 0, Dp(12));
        root.AddView(subtitle);

        _status = AddText(root, "");
        _status.SetPadding(0, Dp(6), 0, Dp(12));

        AddButton(root, "1. Import ECU / SP-Daten ZIP", () => PickDataZip());
        AddButton(root, "2. Scan USB + request permission", () => ScanAndRequestUsb());
        _connect = AddButton(root, "3. Initialize EDIABAS", () => InitializeSession());
        _readVin = AddButton(root, "4. Read VIN from CAS", async () => await ReadVinAsync());
        _readFa = AddButton(root, "5. Read FA/VO copies", async () => await ReadFaAsync());
        _scanFaults = AddButton(root, "6. Scan fault memories", async () => await ScanFaultsAsync());
        _stage6Fl = AddButton(root, "7. Stage +$6FL", () => Stage6Fl());
        _backup = AddButton(root, "8. Create coding backup", async () => await BackupAsync());

        var arm = new CheckBox(this) { Text = "I understand coding writes can disable modules" };
        arm.CheckedChange += (_, e) =>
        {
            _writeArmed = e.IsChecked;
            RefreshState();
        };
        root.AddView(arm);

        _write = AddButton(root, "9. Write staged FA/VO", async () => await WriteAsync());

        var outputLabel = new TextView(this) { Text = "Result" };
        outputLabel.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        outputLabel.SetPadding(0, Dp(18), 0, Dp(4));
        root.AddView(outputLabel);
        _output = AddText(root, "No vehicle data read yet.");
        _output.SetTextIsSelectable(true);

        var logLabel = new TextView(this) { Text = "Session log" };
        logLabel.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        logLabel.SetPadding(0, Dp(18), 0, Dp(4));
        root.AddView(logLabel);
        _logView = AddText(root, "");
        _logView.SetTextIsSelectable(true);
        _logView.SetTypeface(Android.Graphics.Typeface.Monospace, Android.Graphics.TypefaceStyle.Normal);
        _logView.TextSize = 11;

        SetContentView(scroll);
    }

    private Button AddButton(LinearLayout root, string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => action();
        root.AddView(button, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return button;
    }

    private TextView AddText(LinearLayout root, string text)
    {
        var view = new TextView(this) { Text = text };
        root.AddView(view, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return view;
    }

    private void PickDataZip()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/zip");
        StartActivityForResult(intent, PickDataZipRequest);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickDataZipRequest || resultCode != Result.Ok || data?.Data == null) return;

        try
        {
            await using var input = ContentResolver!.OpenInputStream(data.Data)
                ?? throw new InvalidOperationException("Could not open selected file.");
            _status.Text = "Importing data...";
            var count = await DataImporter.ImportZipAsync(input, _paths.Root);
            _log.Add($"Imported {count} BMW data files.");
            RefreshState();
        }
        catch (Exception ex)
        {
            ShowError("Import failed", ex);
        }
    }

    private void ScanAndRequestUsb()
    {
        var devices = _usb.DeviceList.Values.ToList();
        if (devices.Count == 0)
        {
            _output.Text = "No USB device detected.";
            return;
        }

        var device = devices.FirstOrDefault(x => x.VendorId == 0x0403) ?? devices[0];
        _output.Text =
            $"USB device\nVID:PID {device.VendorId:X4}:{device.ProductId:X4}\n" +
            $"Permission: {_usb.HasPermission(device)}";

        if (_usb.HasPermission(device))
        {
            _log.Add($"USB permission already granted for {device.VendorId:X4}:{device.ProductId:X4}");
            RefreshState();
            return;
        }

        var pending = PendingIntent.GetBroadcast(
            this, 0, new Intent(UsbPermissionAction).SetPackage(PackageName),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _usb.RequestPermission(device, pending);
        _log.Add($"Requested USB permission for {device.VendorId:X4}:{device.ProductId:X4}");
    }

    private void RegisterUsbReceiver()
    {
        _usbReceiver = new UsbPermissionReceiver(granted =>
        {
            RunOnUiThread(() =>
            {
                _log.Add("USB permission " + (granted ? "granted" : "denied"));
                RefreshState();
            });
        });

        var filter = new IntentFilter(UsbPermissionAction);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_usbReceiver, filter, ReceiverFlags.NotExported);
        else
#pragma warning disable CA1422
            RegisterReceiver(_usbReceiver, filter);
#pragma warning restore CA1422
    }

    private void InitializeSession()
    {
        try
        {
            EnsureUsbPermission();
            _session.Initialize();
            _output.Text = "EDIABAS initialized. No vehicle write has occurred.";
            RefreshState();
        }
        catch (Exception ex) { ShowError("Initialization failed", ex); }
    }

    private async Task ReadVinAsync()
    {
        await BusyAsync("Reading CAS VIN...", async () =>
        {
            _vin = await Task.Run(() => _identity.ReadVinAsync());
            var match = VehicleConfig.VinMatches(_vin);
            _output.Text =
                $"VIN from car: {_vin}\nExpected: {VehicleConfig.ExpectedVin}\n" +
                $"Safety match: {(match ? "YES" : "NO - WRITES BLOCKED")}";
            RefreshState();
        });
    }

    private async Task ReadFaAsync()
    {
        await BusyAsync("Reading FA/VO...", async () =>
        {
            _orders = await Task.Run(() => _faService.ReadAllAvailableAsync());
            var cas = _orders.FirstOrDefault(x => x.Source.Equals("CAS", StringComparison.OrdinalIgnoreCase))
                      ?? _orders[0];
            _editor = new FaEditor(cas);
            _backupPath = null;

            var lines = new List<string>();
            foreach (var vo in _orders)
            {
                lines.Add($"{vo.Source}: BR={vo.Chassis} date=#{vo.ProductionDate} type={vo.TypeCode}");
                lines.Add(string.Join(" ", vo.Sa.Select(x => "$" + x)));
                lines.Add("");
            }
            if (_orders.Count > 1)
            {
                var baseline = _orders[0].StandardFa;
                var same = _orders.All(x => string.Equals(x.StandardFa, baseline, StringComparison.OrdinalIgnoreCase));
                lines.Add("FA copies match: " + (same ? "YES" : "NO"));
            }
            _output.Text = string.Join(Environment.NewLine, lines);
            RefreshState();
        });
    }

    private async Task ScanFaultsAsync()
    {
        await BusyAsync("Scanning faults...", async () =>
        {
            var progress = new Progress<string>(name => _status.Text = "Scanning " + name + "...");
            var records = await Task.Run(() => _faults.ScanAsync(progress));
            if (records.Count == 0)
            {
                _output.Text = "No fault records returned from reachable modules.";
                return;
            }

            _output.Text = string.Join(Environment.NewLine,
                records.Select(f =>
                    $"{f.Ecu}: {f.Code ?? "(no code)"}  {f.Text ?? ""}" +
                    (f.Present.HasValue ? $"  present={f.Present.Value}" : "")));
        });
    }

    private void Stage6Fl()
    {
        if (_editor == null)
        {
            _output.Text = "Read FA/VO first.";
            return;
        }

        var changed = _editor.Add("6FL");
        var rebuilt = _editor.BuildStandardFa();
        _output.Text =
            (changed ? "Staged +$6FL" : "$6FL was already present") +
            "\n\nOriginal:\n" + (_editor.Original.StandardFa ?? "(none)") +
            "\n\nProposed:\n" + rebuilt;
        _log.Add(changed ? "Staged FA change: +$6FL" : "$6FL already present; no change staged.");
        RefreshState();
    }

    private async Task BackupAsync()
    {
        if (_vin == null || _orders == null)
        {
            _output.Text = "Read VIN and FA/VO before creating a coding backup.";
            return;
        }

        await BusyAsync("Creating backup...", async () =>
        {
            _backupPath = await _backups.SaveAsync(_vin, _orders);
            _output.Text = "Backup created:\n" + _backupPath;
            RefreshState();
        });
    }

    private async Task WriteAsync()
    {
        if (_editor == null) return;
        var voltage = _session.BatteryVoltage();
        var gate = new WriteGateState(
            VehicleConfig.VinMatches(_vin),
            _backupPath != null,
            voltage.HasValue,
            voltage,
            _writeArmed,
            _writer.HardwareVerified);

        if (!gate.Allowed)
        {
            _output.Text = "WRITE BLOCKED\n" + string.Join("\n", gate.Blockers().Select(x => "- " + x));
            return;
        }

        await BusyAsync("Writing FA...", async () =>
        {
            await _writer.WriteAsync(_editor.Original, _editor.BuildStandardFa());
        });
    }

    private async Task BusyAsync(string message, Func<Task> operation)
    {
        try
        {
            SetButtons(false);
            _status.Text = message;
            await operation();
        }
        catch (Exception ex) { ShowError(message, ex); }
        finally
        {
            SetButtons(true);
            RefreshState();
        }
    }

    private void SetButtons(bool enabled)
    {
        _connect.Enabled = enabled;
        _readVin.Enabled = enabled;
        _readFa.Enabled = enabled;
        _scanFaults.Enabled = enabled;
        _stage6Fl.Enabled = enabled;
        _backup.Enabled = enabled;
        _write.Enabled = enabled;
    }

    private void RefreshState()
    {
        if (_status == null) return;
        var usbGranted = _usb?.DeviceList.Values.Any(d => _usb.HasPermission(d)) == true;
        var voltage = _session?.BatteryVoltage();
        _status.Text =
            $"ECU data: {(_paths.HasMinimumEcuData ? "ready" : "missing")}\n" +
            $"USB permission: {(usbGranted ? "yes" : "no")}\n" +
            $"EDIABAS: {(_session.Initialized ? "initialized" : "not initialized")}\n" +
            $"VIN safety: {(VehicleConfig.VinMatches(_vin) ? "matched" : "not matched")}\n" +
            $"Battery: {(voltage.HasValue ? voltage.Value.ToString("0.0") + " V" : "unknown")}\n" +
            $"Backup: {(_backupPath != null ? "ready" : "not created")}\n" +
            $"FA write runtime: {(_writer.HardwareVerified ? "verified" : "blocked / unverified")}";
    }

    private void EnsureUsbPermission()
    {
        var device = _usb.DeviceList.Values.FirstOrDefault(d => d.VendorId == 0x0403)
                     ?? _usb.DeviceList.Values.FirstOrDefault()
                     ?? throw new InvalidOperationException("No USB diagnostic adapter detected.");
        if (!_usb.HasPermission(device))
            throw new InvalidOperationException("USB permission has not been granted.");
    }

    private void ShowError(string title, Exception ex)
    {
        _log.Add(title + ": " + ex.Message);
        _output.Text = title + "\n" + ex.Message;
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private sealed class UsbPermissionReceiver : BroadcastReceiver
    {
        private readonly Action<bool> _callback;
        public UsbPermissionReceiver(Action<bool> callback) => _callback = callback;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != UsbPermissionAction) return;
            _callback(intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false));
        }
    }
}
