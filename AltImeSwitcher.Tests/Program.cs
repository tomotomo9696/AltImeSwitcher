using AltImeSwitcher;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ImeMode = AltImeSwitcher.ImeMode;

namespace AltImeSwitcher.Tests;

internal static class Program
{
    private static int _passed;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a is "--integration" or "--hook-lifecycle" or "--tray-lifecycle"))
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            }
            RunUnitTests();
            RunSettingsAndIconTests();
            if (args.Contains("--hook-lifecycle")) RunHookLifecycleTests();
            if (args.Contains("--tray-lifecycle")) RunTrayLifecycleTest();
            if (args.Contains("--integration")) RunIntegrationTests();
            Console.WriteLine($"PASS: {_passed} checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        _passed++;
        Console.WriteLine("PASS: " + description);
    }

    private static KeyEvent Alt(bool down, bool right = false) => new(right ? NativeMethods.VK_RMENU : NativeMethods.VK_LMENU, 0x38, right, down);
    private static KeyEvent F(bool down) => new(0x46, 0x21, false, down);
    private static bool IsAlt(NativeMethods.Input input, bool down, bool right = false) =>
        input.Type == 1 && input.Data.Keyboard.ScanCode == 0x38 &&
        ((input.Data.Keyboard.Flags & NativeMethods.KEYEVENTF_KEYUP) == 0) == down &&
        ((input.Data.Keyboard.Flags & NativeMethods.KEYEVENTF_EXTENDEDKEY) != 0) == right;

    private static void RunUnitTests()
    {
        InputSender.ValidateLayout();
        Check(Marshal.SizeOf<NativeMethods.Input>() == (IntPtr.Size == 8 ? 40 : 28), "Win32 INPUT ABI size");
        Check(Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)).ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "Win32 INPUT union offset");
        Check(new KeyEvent(0x13, 0x45, false, true).ToInput().Data.Keyboard.VirtualKey == 0x13 &&
            new KeyEvent(0x2C, 0x37, true, true).ToInput().Data.Keyboard.VirtualKey == 0x2C,
            "Pause and PrintScreen replay by virtual key to preserve special scan sequences");
        foreach (var threshold in AppSettings.HoldOptions)
        {
            foreach (var right in new[] { false, true })
            {
                var tap = new Rig(threshold);
                tap.Key(Alt(true, right), 0); tap.Key(Alt(false, right), threshold - 1);
                Check(tap.Requests.Count == 1 && tap.Batches.Count == 0, $"{threshold} ms: short {(right ? "right" : "left")} tap");
                var held = new Rig(threshold);
                held.Key(Alt(true, right), 0); held.Controller.Tick(threshold - 1);
                Check(held.Batches.Count == 0, $"{threshold} ms: timer never fires early");
                held.Controller.Tick(threshold); held.Key(Alt(false, right), threshold + 10);
                Check(held.Requests.Count == 0 && held.Batches.Count == 2 &&
                    IsAlt(held.Batches[0][0], true, right) && IsAlt(held.Batches[1][0], false, right),
                    $"{threshold} ms: long {(right ? "right" : "left")} hold");
            }
        }
        {
            var rig = new Rig(500); rig.Key(Alt(true), 0); rig.Controller.SetHoldMilliseconds(150); rig.Controller.Tick(200);
            Check(rig.Batches.Count == 0 && rig.Key(Alt(false), 250) && rig.Requests.Count == 1,
                "threshold change preserves in-flight tap classification");
            rig.Key(Alt(true), 300); rig.Controller.Tick(450);
            Check(rig.Batches.Count == 1 && IsAlt(rig.Batches[0][0], true), "next press uses updated threshold without restart");
            rig.Key(Alt(false), 451);
        }
        {
            var rig = new Rig(150); rig.Key(Alt(true), 0); rig.Controller.SetHoldMilliseconds(500); rig.Controller.Tick(150);
            Check(rig.Batches.Count == 1, "increasing threshold does not delay a press already in progress");
            rig.Key(Alt(false), 151);
            rig.Key(Alt(true), 200); rig.Key(Alt(false), 600);
            Check(rig.Requests.Count == 1, "new press uses increased threshold");
        }
        foreach (var right in new[] { false, true })
        {
            var rig = new Rig();
            Check(rig.Key(Alt(true, right), 0), "short Alt down suppressed");
            Check(rig.Key(Alt(false, right), 219), "short Alt up suppressed");
            Check(rig.Requests.Count == 1 && rig.Requests[0].Mode == (right ? ImeMode.Hiragana : ImeMode.Alphanumeric) && rig.Batches.Count == 0, "short tap selects correct IME without replay");
        }
        {
            var rig = new Rig();
            rig.Key(Alt(true), 0); rig.Controller.Tick(219);
            Check(rig.Batches.Count == 0, "threshold does not fire early");
            rig.Controller.Tick(220); rig.Controller.Tick(500);
            Check(rig.Batches.Count == 1 && IsAlt(rig.Batches[0][0], true), "held Alt down emitted exactly once");
            Check(rig.Key(Alt(false), 600) && IsAlt(rig.Batches[1][0], false) && rig.Requests.Count == 0, "physical up replays held Alt release without IME");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0);
            Check(rig.Key(Alt(false), 220), "late timer still handles exact threshold");
            Check(rig.Batches.Count == 1 && rig.Batches[0].Length == 2 && IsAlt(rig.Batches[0][0], true) && IsAlt(rig.Batches[0][1], false), "late timer emits paired Alt down/up");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Controller.Tick(220);
            Check(rig.Key(F(true), 221) && rig.Key(F(false), 222), "chord immediately after timer forwards both key edges");
            rig.Key(Alt(false), 223);
            Check(rig.Batches.Count == 4 && IsAlt(rig.Batches[0][0], true) &&
                rig.Batches[1][0].Data.Keyboard.ScanCode == 0x21 &&
                (rig.Batches[2][0].Data.Keyboard.Flags & NativeMethods.KEYEVENTF_KEYUP) != 0 && IsAlt(rig.Batches[3][0], false),
                "post-timer chord cannot overtake queued Alt down");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0);
            Check(rig.Key(F(true), 30), "quick Alt chord triggering key suppressed for replay");
            Check(rig.Batches.Count == 1 && rig.Batches[0].Length == 2 && IsAlt(rig.Batches[0][0], true) && rig.Batches[0][1].Data.Keyboard.ScanCode == 0x21, "Alt and chord key are one ordered batch");
            Check(rig.Key(F(false), 40) && rig.Key(Alt(false), 50) && rig.Requests.Count == 0, "chord releases replay after queued downs");
            Check(rig.Batches.Count == 3 && rig.Batches[1][0].Data.Keyboard.ScanCode == 0x21 &&
                (rig.Batches[1][0].Data.Keyboard.Flags & NativeMethods.KEYEVENTF_KEYUP) != 0 &&
                IsAlt(rig.Batches[2][0], false), "burst chord down/up batches remain ordered");
        }
        {
            var rig = new Rig(); rig.Key(new(0xA2, 0x1D, false, true), 0);
            Check(!rig.Key(Alt(true, true), 10) && !rig.Key(Alt(false, true), 20), "Ctrl then right Alt preserves AltGr");
            Check(rig.Batches.Count == 0 && rig.Requests.Count == 0, "modified Alt never becomes IME tap");
        }
        {
            var rig = new Rig();
            Check(!rig.Controller.Process(Alt(true, true), 0, 1, modifierHeld: true), "externally injected modifier preserves native Alt");
            rig.Key(Alt(false, true), 20);
            Check(rig.Requests.Count == 0, "externally injected modifier does not toggle IME");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Key(Alt(true, true), 10);
            Check(rig.Batches.Count == 1 && rig.Batches[0].Length == 2 && IsAlt(rig.Batches[0][0], true) && IsAlt(rig.Batches[0][1], true, true), "both Alt keys forwarded in press order");
            rig.Key(Alt(false), 20); rig.Key(Alt(false, true), 30);
            Check(rig.Requests.Count == 0, "both Alt keys never become separate IME taps");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Key(Alt(false), 20, 2);
            Check(rig.Requests.Count == 0, "focus change cancels IME tap");
            Check(!rig.Key(Alt(false), 30), "unmatched up passes through");
        }
        {
            var rig = new Rig(); rig.Controller.Initialize([NativeMethods.VK_LMENU]);
            Check(!rig.Key(Alt(true), 0) && !rig.Key(Alt(false), 10) && rig.Requests.Count == 0, "Alt held before hook installation passes through");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Key(Alt(true), 20); rig.Key(Alt(true), 40); rig.Key(Alt(false), 50);
            Check(rig.Requests.Count == 1, "auto-repeat does not restart tap deadline");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0);
            Check(rig.Controller.ProcessMouse(NativeMethods.Input.Mouse(2)), "quick Alt click forwarded");
            Check(rig.Batches[0].Length == 2 && IsAlt(rig.Batches[0][0], true) && rig.Batches[0][1].Type == 0, "Alt precedes mouse click in one batch");
            Check(rig.Controller.ProcessMouse(NativeMethods.Input.Mouse(4)) && rig.Batches[1][0].Data.Mouse.Flags == 4,
                "quick mouse release replays after click down");
            rig.Key(Alt(false), 30);
            Check(rig.Requests.Count == 0, "Alt click never toggles IME");
        }
        {
            var rig = new Rig { Accepted = 0 }; rig.Key(Alt(true), 0);
            Check(!rig.Key(F(true), 20), "failed replay never swallows unrelated key");
            rig.Key(Alt(false), 30);
            Check(rig.Requests.Count == 0, "failed chord replay never becomes IME tap");
        }
        {
            var rig = new Rig { Accepted = 1 }; rig.Key(Alt(true), 0);
            Check(!rig.Key(F(true), 20), "partial replay preserves unsent chord key");
            rig.Controller.ReleaseAll();
            Check(rig.Batches.Count == 2 && IsAlt(rig.Batches[1][0], false), "shutdown releases only successfully inserted Alt");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Controller.Tick(220); rig.Controller.ReleaseAll(); rig.Controller.ReleaseAll();
            Check(rig.Batches.Count == 2 && IsAlt(rig.Batches[1][0], false) && rig.Requests.Count == 0, "shutdown releases held Alt exactly once");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Controller.ReleaseAll(); rig.Controller.Tick(300);
            Check(rig.Batches.Count == 0 && rig.Requests.Count == 0, "pending Alt canceled on shutdown/session reset");
            rig.Key(Alt(true), 400); rig.Key(Alt(false), 420);
            Check(rig.Requests.Count == 1, "new tap works after reset");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Key(F(true), 10); rig.Controller.ReleaseAll();
            Check(rig.Batches[1].Length == 2 && rig.Batches[1][0].Data.Keyboard.ScanCode == 0x21 &&
                (rig.Batches[1][0].Data.Keyboard.Flags & NativeMethods.KEYEVENTF_KEYUP) != 0 && IsAlt(rig.Batches[1][1], false),
                "shutdown releases replayed chord key before Alt");
        }
        {
            var rig = new Rig(); rig.Key(Alt(true), 0); rig.Controller.ProcessMouse(NativeMethods.Input.Mouse(2)); rig.Controller.ReleaseAll();
            Check(rig.Batches[1].Length == 2 && rig.Batches[1][0].Data.Mouse.Flags == 4 && IsAlt(rig.Batches[1][1], false),
                "shutdown releases replayed mouse button before Alt");
        }
    }

    private static void RunHookLifecycleTests()
    {
        var failures = new ConcurrentQueue<Exception>();
        for (var cycle = 0; cycle < 20; cycle++)
        {
            using var hook = new AltKeyHook(_ => { }, failures.Enqueue);
            hook.Install();
            if (!hook.IsRunning) throw new InvalidOperationException("Hook stopped after startup.");
            hook.Dispose();
            if (hook.IsRunning) throw new InvalidOperationException("Hook thread leaked after disposal.");
        }
        Check(failures.IsEmpty, "20 real keyboard/mouse hook start-stop cycles without exceptions or leaked worker threads");
    }

    private static void RunTrayLifecycleTest()
    {
        using var dispatcher = new HiddenDispatcher();
        var handle = dispatcher.Handle;
        Check(!dispatcher.Visible && !IsWindowVisible(handle), "dispatcher is hidden from initial HWND creation");
        Check((GetWindowLong(handle, -16) & (0x40000000 | 0x10000000)) == 0,
            "dispatcher is a non-visible top-level window for broadcast notifications");
        var extendedStyle = GetWindowLong(handle, -20);
        Check((extendedStyle & 0x00040000) == 0 && (extendedStyle & (0x80 | 0x08000000)) == (0x80 | 0x08000000),
            "dispatcher excludes taskbar and Alt+Tab entries and cannot activate");
        dispatcher.Show();
        dispatcher.Visible = true;
        Check(!dispatcher.Visible && !IsWindowVisible(handle), "Show and Visible cannot reveal the internal dispatcher");
        var dispatched = false;
        dispatcher.BeginInvoke((Action)(() => dispatched = true));
        var folder = Path.Combine(AppContext.BaseDirectory, "tray-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settingsPath = Path.Combine(folder, "settings.json");
        try
        {
            using var context = new TrayApplicationContext(settingsPath);
            CheckInvisibleWindows("tray startup");
            var thresholdMenu = (ToolStripMenuItem)context.TrayMenu.Items[0];
            Check(thresholdMenu.DropDownItems.Count == AppSettings.HoldOptions.Count &&
                thresholdMenu.DropDownItems.OfType<ToolStripMenuItem>().Count(i => i.Checked) == 1,
                "tray menu contains threshold choices with one selected item");
            ((ToolStripMenuItem)thresholdMenu.DropDownItems[^1]).PerformClick();
            Check(context.CurrentHoldMilliseconds == 500 && AppSettings.Load(settingsPath).HoldMilliseconds == 500 &&
                thresholdMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Checked).Tag is 500,
                "real menu click updates live hook, check mark and persisted settings");
            var pause = context.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Pause");
            pause.Checked = true;
            ((ToolStripMenuItem)thresholdMenu.DropDownItems[0]).PerformClick();
            pause.Checked = false;
            CheckInvisibleWindows("pause/resume");
            Check(context.CurrentHoldMilliseconds == 150 && AppSettings.Load(settingsPath).HoldMilliseconds == 150,
                "threshold selection while paused survives hook restart");
            using var timer = new System.Windows.Forms.Timer { Interval = 250 };
            var exited = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    CheckInvisibleWindows("running message loop");
                    Check(dispatched, "hidden dispatcher still delivers BeginInvoke callbacks");
                }
                finally { exited = true; context.ExitThread(); }
            };
            timer.Start();
            Application.Run(context);
            Check(exited && !Application.MessageLoop, "real tray context starts and exits its message loop normally");
        }
        finally
        {
            File.Delete(settingsPath); File.Delete(settingsPath + ".tmp"); Directory.Delete(folder);
        }
    }

    private static void RunSettingsAndIconTests()
    {
        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 128 })
        {
            using var icon = ApplicationIcon.Load(new Size(size, size));
            using var bitmap = icon.ToBitmap();
            Check(icon.Width == size && icon.Height == size && bitmap.GetPixel(size / 2, size / 2).A > 0,
                $"embedded custom icon loads at {size} px (actual={icon.Width}x{icon.Height}, center alpha={bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A})");
            Check(bitmap.GetPixel(0, 0).A == 0, $"{size} px icon retains transparent corners");
        }
        // Shell/executable icons use Windows' native loader, including the ICO 256 px frame.
        var executable = Path.ChangeExtension(typeof(AltKeyController).Assembly.Location, ".exe");
        foreach (var size in new[] { 32, 256 })
        {
            var handles = new nint[1];
            var count = PrivateExtractIcons(executable, 0, size, size, handles, new uint[1], 1, 0);
            try
            {
                Check(count == 1 && handles[0] != 0, $"exe contains custom icon extractable by Windows at {size} px");
                using var icon = Icon.FromHandle(handles[0]);
                using var bitmap = icon.ToBitmap();
                Check(bitmap.Width == size && bitmap.Height == size && bitmap.GetPixel(0, 0).A == 0,
                    $"native executable icon has correct {size} px frame and transparency");
            }
            finally { if (handles[0] != 0) DestroyIcon(handles[0]); }
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        var blockedPath = Path.Combine(folder, "blocked");
        try
        {
            Check(AppSettings.Load(path).HoldMilliseconds == 220, "missing settings use 220 ms default");
            foreach (var threshold in AppSettings.HoldOptions)
            {
                Check(new AppSettings { HoldMilliseconds = threshold }.TrySave(path) &&
                    AppSettings.Load(path).HoldMilliseconds == threshold && !File.Exists(path + ".tmp"),
                    $"{threshold} ms settings persist atomically and reload");
            }
            File.WriteAllText(path, "{broken");
            Check(AppSettings.Load(path).HoldMilliseconds == 220, "corrupt JSON falls back to default");
            File.WriteAllText(path, "{\"HoldMilliseconds\": -1}");
            Check(AppSettings.Load(path).HoldMilliseconds == 220, "unsupported threshold falls back to default");
            File.WriteAllText(path, "null");
            Check(AppSettings.Load(path).HoldMilliseconds == 220, "null settings fall back to default");
            Directory.CreateDirectory(blockedPath);
            Check(!new AppSettings { HoldMilliseconds = 300 }.TrySave(blockedPath), "settings save failure is reported without crashing");
            var rejected = false;
            try { new Rig().Controller.SetHoldMilliseconds(0); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "controller rejects invalid runtime threshold");
        }
        finally
        {
            File.Delete(path); File.Delete(path + ".tmp"); File.Delete(blockedPath + ".tmp");
            if (Directory.Exists(blockedPath)) Directory.Delete(blockedPath);
            Directory.Delete(folder);
        }
    }

    private sealed class Rig
    {
        public readonly List<NativeMethods.Input[]> Batches = [];
        public readonly List<ImeRequest> Requests = [];
        public uint Accepted = uint.MaxValue;
        public AltKeyController Controller { get; }
        public Rig(int threshold = AppSettings.DefaultHoldMilliseconds) => Controller = new(
            inputs => { Batches.Add(inputs); return Math.Min(Accepted, (uint)inputs.Length); }, Requests.Add, threshold);
        public bool Key(KeyEvent key, long time, nint foreground = 1) => Controller.Process(key, time, foreground);
    }

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height,
        [Out] nint[] handles, [Out] uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    private static void CheckInvisibleWindows(string phase)
    {
        var visible = new List<nint>();
        EnumWindowsProc callback = (window, _) =>
        {
            GetWindowThreadProcessId(window, out var process);
            if (process == Environment.ProcessId && IsWindowVisible(window)) visible.Add(window);
            return true;
        };
        if (!EnumWindows(callback, 0)) throw new InvalidOperationException("Window enumeration failed.");
        Check(visible.Count == 0, $"{phase}: no visible top-level app windows ({visible.Count} found)");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern int GetWindowLong(nint window, int index);

    private static void RunIntegrationTests()
    {
        using var form = new ProbeForm();
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            AltKeyHook? hook = null;
            var requests = new ConcurrentQueue<ImeRequest>();
            var errors = new ConcurrentQueue<Exception>();
            try
            {
                form.Activate();
                var activated = NativeMethods.SetForegroundWindow(form.Handle);
                await Task.Delay(500);
                Check(NativeMethods.GetForegroundWindow() == form.Handle,
                    $"integration probe owns foreground (activated={activated}, foreground=0x{NativeMethods.GetForegroundWindow():X}, probe=0x{form.Handle:X})");
                Check(new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }.All(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) == 0), "integration starts with modifiers released");
                hook = new AltKeyHook(requests.Enqueue, errors.Enqueue, diagnostic: true);
                hook.Install();
                foreach (var right in new[] { false, true })
                {
                    form.KeyMessages.Clear();
                    Inject(form, NativeMethods.Input.ScanCode(0x38, right, true, NativeMethods.TestTag));
                    await Task.Delay(350);
                    Check((NativeMethods.GetAsyncKeyState(right ? NativeMethods.VK_RMENU : NativeMethods.VK_LMENU) & 0x8000) != 0, "real SendInput holds " + (right ? "right" : "left") + " Alt");
                    Inject(form, NativeMethods.Input.ScanCode(0x21, false, true, NativeMethods.TestTag), NativeMethods.Input.ScanCode(0x21, false, false, NativeMethods.TestTag));
                    await Task.Delay(60);
                    Inject(form, NativeMethods.Input.ScanCode(0x38, right, false, NativeMethods.TestTag));
                    await Task.Delay(80);
                    Check(form.KeyMessages.Any(e => e.Key == 0x46 && e.Message == NativeMethods.WM_SYSKEYDOWN && e.AltContext), "target receives held " + (right ? "right" : "left") + " Alt+F as WM_SYSKEYDOWN");
                    Check((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) == 0, "held Alt released in OS");
                }
                form.KeyMessages.Clear();
                Inject(form, NativeMethods.Input.ScanCode(0x38, false, true, NativeMethods.TestTag),
                    NativeMethods.Input.ScanCode(0x21, false, true, NativeMethods.TestTag),
                    NativeMethods.Input.ScanCode(0x21, false, false, NativeMethods.TestTag),
                    NativeMethods.Input.ScanCode(0x38, false, false, NativeMethods.TestTag));
                await Task.Delay(150);
                Check(form.KeyMessages.Any(e => e.Key == 0x46 && e.Message == NativeMethods.WM_SYSKEYDOWN && e.AltContext), "immediate Alt+F has correct OS ordering");
                Check(requests.IsEmpty, "long holds and quick chords emit no IME requests");
                foreach (var right in new[] { false, true })
                {
                    form.KeyMessages.Clear();
                    Inject(form, NativeMethods.Input.ScanCode(0x38, right, true, NativeMethods.TestTag), NativeMethods.Input.ScanCode(0x38, right, false, NativeMethods.TestTag));
                    await Task.Delay(80);
                    Check(requests.TryDequeue(out var request) && request.Mode == (right ? ImeMode.Hiragana : ImeMode.Alphanumeric), "real hook detects short " + (right ? "right" : "left") + " Alt");
                    Check(form.KeyMessages.Count == 0, "short tap does not leak Alt to target");
                }
                Inject(form, NativeMethods.Input.ScanCode(0x38, false, true, NativeMethods.TestTag));
                await Task.Delay(350);
                hook.Dispose(); hook = null;
                await Task.Delay(80);
                Check((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) == 0, "disposing live hook releases OS Alt state");
                Check(errors.IsEmpty, "no hook exceptions in integration run");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                hook?.Dispose();
                if (NativeMethods.GetForegroundWindow() == form.Handle)
                    _ = InputSender.Send([NativeMethods.Input.Alt(NativeMethods.VK_LMENU, false), NativeMethods.Input.Alt(NativeMethods.VK_RMENU, false)]);
                form.Close();
            }
        };
        Application.Run(form);
        if (failure is not null) throw failure;
    }

    private static void Inject(Form form, params NativeMethods.Input[] inputs)
    {
        if (NativeMethods.GetForegroundWindow() != form.Handle) throw new InvalidOperationException("Probe lost foreground; input test aborted.");
        if (InputSender.Send(inputs) != inputs.Length) throw new InvalidOperationException("Probe SendInput failed.");
    }

    private sealed class ProbeForm : Form
    {
        public List<(int Message, int Key, bool AltContext)> KeyMessages { get; } = [];
        public ProbeForm() { Text = "AltImeSwitcher input verification"; Width = 460; Height = 140; StartPosition = FormStartPosition.CenterScreen; TopMost = true; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
            {
                KeyMessages.Add((message.Msg, (int)message.WParam, (message.LParam.ToInt64() & (1L << 29)) != 0));
                message.Result = 0;
                return;
            }
            base.WndProc(ref message);
        }
    }
}
