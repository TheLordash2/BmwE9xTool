using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
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
    private NcsSgfamParser _sgfam = null!;
    private FaService _faService = null!;
    private FaultService _faults = null!;
    private BackupService _backups = null!;
    private FaWriteEngine _writer = null!;
    private UsbPermissionReceiver? _usbReceiver;

    private TextView _status = null!;
    private TextView _output = null!;
    private TextView _logView = null!;
    private EditText _expectedVinInput = null!;
    private EditText _optionInput = null!;
    private Button _connect = null!;
    private Button _readVin = null!;
    private Button _readFa = null!;
    private Button _scanFaults = null!;
    private Button _backup = null!;
    private Button _write = null!;

    private string? _vin;
    private IReadOnlyList<VehicleOrder>? _orders;
    private FaEditor? _editor;
    private string? _backupPath;
    private bool _writeArmed;
    private bool _experimentalWriteAccepted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _paths = new AppPaths(FilesDir!.AbsolutePath);
        _log = new AppLog();
        _usb = (UsbManager)GetSystemService(UsbService)!;
        _session = new EdiabasSession(_paths, _log, _usb);
        _identity = new VehicleIdentityService(_session, _log);
        _sgfam = new NcsSgfamParser(_paths);
        _faService = new FaService(_session, _log, _sgfam);
        _faults = new FaultService(_session, _log, _sgfam);
        _backups = new BackupService(_paths, _log);
        _writer = new FaWriteEngine(_session, _faService, _log);

        BuildUi();
        _log.Changed += () => RunOnUiThread(() => _logView.Text = _log.Text);
        RegisterUsbReceiver();
        RefreshState();

        _log.Add("BMW E9x Tool started.");
        _log.Add("Vehicle identifiers are runtime-only and are not compiled into the app.");
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
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(32));
        scroll.AddView(root);

        var title = new TextView(this) { Text = "BMW E9x Tool" };
        title.TextSize = 26;
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        root.AddView(title);

        root.AddView(new TextView(this)
        {
            Text = "E8x/E9x diagnostics, fault reading and FA/VO editing"
        });

        AddSection(root, "Local safety VIN");

        _expectedVinInput = new EditText(this)
        {
            Hint = "Enter expected 17-character VIN locally",
            SingleLine = true,
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters
        };
        root.AddView(_expectedVinInput);

        AddButton(root, "Set expected VIN locally", () =>
        {
            var vin = _expectedVinInput.Text?.Trim();

            if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
            {
                _output.Text = "Expected VIN must be exactly 17 characters.";
                return;
            }

            VehicleConfig.ConfigureExpectedVin(vin);
            _expectedVinInput.Text = string.Empty;
            HideKeyboard(_expectedVinInput);
            _log.Add("Expected VIN configured locally (value intentionally not logged).");
            RefreshState();
        });

        AddSection(root, "Connection");

        _status = AddText(root, string.Empty);
        AddButton(root, "Import ECU / SP-Daten ZIP", PickDataZip);
        AddButton(root, "Scan USB + request permission", ScanAndRequestUsb);
        _connect = AddButton(root, "Initialize EDIABAS", InitializeSession);

        AddSection(root, "Vehicle");

        _readVin = AddButton(root, "Read VIN from car", async () => await ReadVinAsync());
        _readFa = AddButton(root, "Read FA/VO", async () => await ReadFaAsync());
        _scanFaults = AddButton(root, "Scan common fault memories", async () => await ScanFaultsAsync(false));
        AddButton(root, "Deep fault scan (imported E89 SGFAM modules)", async () => await ScanFaultsAsync(true));

        AddSection(root, "Option editor");

        _optionInput = new EditText(this)
        {
            Hint = "SA code, e.g. 6FL",
            SingleLine = true,
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters
        };
        root.AddView(_optionInput);

        AddButton(root, "Add option code", () => EditOption(true));
        AddButton(root, "Remove option code", () => EditOption(false));

        AddButton(root, "Preset: add $6FL USB/audio", () =>
        {
            _optionInput.Text = "6FL";
            EditOption(true);
        });

        AddSection(root, "Backup and write");

        _backup = AddButton(root, "Create coding backup", async () => await BackupAsync());

        var arm = new CheckBox(this)
        {
            Text = "Arm write mode: I understand an interrupted coding write can disable a module"
        };
        arm.CheckedChange += (_, e) =>
        {
            _writeArmed = e.IsChecked;
            RefreshState();
        };
        root.AddView(arm);

        var experimental = new CheckBox(this)
        {
            Text = "Allow experimental direct FA write (CAS/FRM) with immediate read-back verification"
        };
        experimental.CheckedChange += (_, e) =>
        {
            _experimentalWriteAccepted = e.IsChecked;
            RefreshState();
        };
        root.AddView(experimental);

        _write = AddButton(root, "Write staged FA/VO", async () => await WriteAsync());

        AddSection(root, "Result");

        _output = AddText(root, "No vehicle data read yet.");
        _output.SetTextIsSelectable(true);

        AddSection(root, "Session log");

        _logView = AddText(root, string.Empty);
        _logView.SetTextIsSelectable(true);
        _logView.SetTypeface(Android.Graphics.Typeface.Monospace, Android.Graphics.TypefaceStyle.Normal);
        _logView.TextSize = 11;

        SetContentView(scroll);
    }

    private void AddSection(LinearLayout root, string text)
    {
        var label = new TextView(this) { Text = text };
        label.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        label.SetPadding(0, Dp(18), 0, Dp(4));
        root.AddView(label);
    }

    private Button AddButton(LinearLayout root, string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => action();

        root.AddView(
            button,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent));

        return button;
    }

    private TextView AddText(LinearLayout root, string text)
    {
        var view = new TextView(this) { Text = text };

        root.AddView(
            view,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent));

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

        if (requestCode != PickDataZipRequest ||
            resultCode != Result.Ok ||
            data?.Data == null)
            return;

        try
        {
            await using var input = ContentResolver!.OpenInputStream(data.Data)
                ?? throw new InvalidOperationException("Could not open selected file.");

            _status.Text = "Importing data...";

            var count = await DataImporter.ImportZipAsync(input, _paths.Root);

            _log.Add($"Imported {count} BMW data files.");
            _output.Text = $"Imported {count} supported ECU/DATEN files.";
        }
        catch (Exception ex)
        {
            ShowError("Import failed", ex);
        }
        finally
        {
            RefreshState();
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
            this,
            0,
            new Intent(UsbPermissionAction).SetPackage(PackageName),
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
        {
            RegisterReceiver(_usbReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
#pragma warning disable CA1422
            RegisterReceiver(_usbReceiver, filter);
#pragma warning restore CA1422
        }
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
        catch (Exception ex)
        {
            ShowError("Initialization failed", ex);
        }
    }

    private async Task ReadVinAsync()
    {
        await BusyAsync("Reading VIN...", async () =>
        {
            _vin = await Task.Run(() => _identity.ReadVinAsync());

            var match = VehicleConfig.VinMatches(_vin);

            _output.Text =
                $"VIN read from car: {_vin}\n" +
                $"Local safety match: {(match ? "YES" : "NO")}";
        });
    }

    private async Task ReadFaAsync()
    {
        await BusyAsync("Reading FA/VO...", async () =>
        {
            _orders = await Task.Run(() => _faService.ReadAllAvailableAsync());

            var cas = _orders.FirstOrDefault(
                          x => x.Source.Equals("CAS", StringComparison.OrdinalIgnoreCase))
                      ?? _orders[0];

            _editor = new FaEditor(cas);
            _backupPath = null;

            ShowVehicleOrders(_orders);
        });
    }

    private async Task ScanFaultsAsync(bool deep)
    {
        await BusyAsync(
            deep ? "Deep fault scan..." : "Scanning common faults...",
            async () =>
            {
                var progress = new Progress<string>(
                    name => _status.Text = "Scanning " + name + "...");

                var records = deep
                    ? await Task.Run(() => _faults.ScanDeepAsync(progress))
                    : await Task.Run(() => _faults.ScanCommonAsync(progress));

                _output.Text = records.Count == 0
                    ? "No fault records returned from reachable modules."
                    : string.Join(
                        Environment.NewLine,
                        records.Select(
                            f =>
                                $"{f.Ecu}: {f.Code ?? "(no code)"}  {f.Text ?? string.Empty}" +
                                (f.Present.HasValue
                                    ? $"  present={f.Present.Value}"
                                    : string.Empty)));
            });
    }

    private void EditOption(bool add)
    {
        if (_editor == null)
        {
            _output.Text = "Read FA/VO first.";
            return;
        }

        try
        {
            var optionCode = OptionCatalog.Normalize(_optionInput.Text ?? string.Empty);
            var changed = add
                ? _editor.Add(optionCode)
                : _editor.Remove(optionCode);

            _optionInput.Text = string.Empty;
            HideKeyboard(_optionInput);

            _output.Text =
                $"{(add ? "Add" : "Remove")} $" + optionCode +
                $": {(changed ? "STAGED" : "NO CHANGE")}\n\n" +
                $"Original:\n{_editor.Original.StandardFa}\n\n" +
                $"Proposed:\n{_editor.BuildStandardFa()}";

            _log.Add((add ? "Staged add $" : "Staged remove $") + optionCode);
        }
        catch (Exception ex)
        {
            ShowError("Option edit failed", ex);
        }
        finally
        {
            RefreshState();
        }
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
            _output.Text = "Backup created in the app's private storage.";
        });
    }

    private async Task WriteAsync()
    {
        if (_editor == null)
        {
            _output.Text = "Nothing staged. Read FA/VO and edit an option first.";
            return;
        }

        var voltage = _session.BatteryVoltage();

        var gate = new WriteGateState(
            VehicleConfig.VinMatches(_vin),
            _backupPath != null,
            voltage.HasValue,
            voltage,
            _writeArmed,
            _experimentalWriteAccepted);

        if (!gate.Allowed)
        {
            _output.Text =
                "WRITE BLOCKED\n" +
                string.Join(
                    "\n",
                    gate.Blockers().Select(x => "- " + x));

            return;
        }

        await BusyAsync("Writing and verifying FA...", async () =>
        {
            var targets = await _writer.DiscoverTargetsAsync();

            if (targets.Count == 0)
            {
                throw new InvalidOperationException(
                    "No CAS/FRM FA write target found in imported ECU data.");
            }

            var proposed = _editor.BuildStandardFa();

            var written = await _writer.WriteAndVerifyAsync(
                _editor.Original,
                proposed);

            _orders = await _faService.ReadAllAvailableAsync();

            _editor = new FaEditor(
                _orders.FirstOrDefault(
                    x => x.Source.Equals("CAS", StringComparison.OrdinalIgnoreCase))
                ?? _orders[0]);

            _output.Text =
                "FA WRITE VERIFIED\nTargets: " +
                string.Join(", ", written) +
                "\n\nCurrent FA:\n" +
                _editor.Original.StandardFa;

            _backupPath = null;
            _writeArmed = false;
        });
    }

    private void ShowVehicleOrders(IReadOnlyList<VehicleOrder> orders)
    {
        var lines = new List<string>();

        foreach (var vo in orders)
        {
            lines.Add(
                $"{vo.Source}: BR={vo.Chassis} date=#{vo.ProductionDate} type={vo.TypeCode}");

            lines.Add(
                string.Join(
                    " ",
                    vo.Sa.Select(x => "$" + x)));

            lines.Add(string.Empty);
        }

        if (orders.Count > 1)
        {
            var baseline = orders[0].StandardFa;

            var same = orders.All(
                x => string.Equals(
                    x.StandardFa,
                    baseline,
                    StringComparison.OrdinalIgnoreCase));

            lines.Add("FA copies match: " + (same ? "YES" : "NO"));
        }

        _output.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task BusyAsync(string message, Func<Task> operation)
    {
        try
        {
            SetButtons(false);
            _status.Text = message;

            await operation();
        }
        catch (Exception ex)
        {
            ShowError(message, ex);
        }
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
        _backup.Enabled = enabled;
        _write.Enabled = enabled;
    }

    private void RefreshState()
    {
        if (_status == null)
            return;

        var usbGranted =
            _usb?.DeviceList.Values.Any(d => _usb.HasPermission(d)) == true;

        var voltage = _session?.BatteryVoltage();

        _status.Text =
            $"ECU data: {(_paths.HasMinimumEcuData ? "ready" : "missing")}\n" +
            $"USB permission: {(usbGranted ? "yes" : "no")}\n" +
            $"EDIABAS: {(_session.Initialized ? "initialized" : "not initialized")}\n" +
            $"Expected VIN local: {(string.IsNullOrEmpty(VehicleConfig.ExpectedVin) ? "not set" : "set")}\n" +
            $"VIN safety: {(VehicleConfig.VinMatches(_vin) ? "matched" : "not matched")}\n" +
            $"Battery: {(voltage.HasValue ? voltage.Value.ToString("0.0") + " V" : "unknown")}\n" +
            $"Backup: {(_backupPath != null ? "ready" : "not created")}\n" +
            "Direct FA writer: implemented, experimental";
    }

    private void EnsureUsbPermission()
    {
        var device =
            _usb.DeviceList.Values.FirstOrDefault(d => d.VendorId == 0x0403)
            ?? _usb.DeviceList.Values.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No USB diagnostic adapter detected.");

        if (!_usb.HasPermission(device))
        {
            throw new InvalidOperationException(
                "USB permission has not been granted.");
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _log.Add(title + ": " + ex.Message);
        _output.Text = title + "\n" + ex.Message;
    }

    private void HideKeyboard(View view)
    {
        var imm = (InputMethodManager?)GetSystemService(InputMethodService);
        imm?.HideSoftInputFromWindow(
            view.WindowToken,
            HideSoftInputFlags.None);
    }

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density);

    private sealed class UsbPermissionReceiver : BroadcastReceiver
    {
        private readonly Action<bool> _callback;

        public UsbPermissionReceiver(Action<bool> callback)
        {
            _callback = callback;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != UsbPermissionAction)
                return;

            _callback(
                intent.GetBooleanExtra(
                    UsbManager.ExtraPermissionGranted,
                    false));
        }
    }
}
