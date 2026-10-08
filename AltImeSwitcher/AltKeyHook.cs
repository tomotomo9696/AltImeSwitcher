using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AltImeSwitcher;

internal sealed class AltKeyHook(Action<ImeRequest> requestIme, Action<Exception> reportFailure, bool diagnostic = false,
    int holdMilliseconds = AppSettings.DefaultHoldMilliseconds) : IDisposable
{
    private static readonly int[] ModifierKeys = [0xA0, 0xA1, 0xA2, 0xA3, 0x5B, 0x5C];
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private Control? _dispatcher;
    private Exception? _startupError;
    private bool _disposed;
    private int _holdMilliseconds = AppSettings.ValidateHoldMilliseconds(holdMilliseconds);
    public bool IsRunning => _thread?.IsAlive == true;
    public int HoldMilliseconds => Volatile.Read(ref _holdMilliseconds);

    public void SetHoldMilliseconds(int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Volatile.Write(ref _holdMilliseconds, AppSettings.ValidateHoldMilliseconds(value));
    }

    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null) throw new InvalidOperationException("Hook is already started.");
        InputSender.ValidateLayout();
        _thread = new Thread(Run) { IsBackground = true, Name = "AltImeSwitcher keyboard hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Hook startup timed out.");
        if (_startupError is not null) throw new InvalidOperationException("Hook startup failed.", _startupError);
    }

    private void Run()
    {
        nint keyboard = 0, mouse = 0;
        var sendFailed = false;
        var controller = new AltKeyController(inputs =>
        {
            var sent = InputSender.Send(inputs);
            if (sent != inputs.Length && !sendFailed)
            {
                sendFailed = true;
                Fail(new InvalidOperationException("Input replay failed. Check whether the target is running as administrator."));
            }
            return sent;
        }, requestIme, HoldMilliseconds);
        NativeMethods.HookProc keyboardProc = (code, message, pointer) =>
        {
            if (code == 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(pointer);
                    var injected = (data.Flags & NativeMethods.LLKHF_INJECTED) != 0;
                    if (data.ExtraInfo != NativeMethods.ReplayTag &&
                        (!injected || (diagnostic && data.ExtraInfo == NativeMethods.TestTag)))
                    {
                        var msg = (int)message;
                        if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                        {
                            var extended = (data.Flags & NativeMethods.LLKHF_EXTENDED) != 0;
                            var key = (int)data.VirtualKey;
                            if (key == NativeMethods.VK_MENU) key = extended ? NativeMethods.VK_RMENU : NativeMethods.VK_LMENU;
                            controller.SetHoldMilliseconds(HoldMilliseconds);
                            if (controller.Process(new(key, (ushort)data.ScanCode, extended,
                                msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN),
                                Environment.TickCount64, NativeMethods.GetForegroundWindow(),
                                (key is NativeMethods.VK_LMENU or NativeMethods.VK_RMENU) &&
                                ModifierKeys.Any(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) != 0))) return 1;
                        }
                    }
                }
                catch (Exception ex) { Fail(ex); }
            }
            return NativeMethods.CallNextHookEx(keyboard, code, message, pointer);
        };
        NativeMethods.HookProc mouseProc = (code, message, pointer) =>
        {
            if (code == 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(pointer);
                    if ((data.Flags & 1) == 0)
                    {
                        uint flags = (int)message switch
                        {
                            0x201 => 0x2, 0x202 => 0x4, 0x204 => 0x8, 0x205 => 0x10,
                            0x207 => 0x20, 0x208 => 0x40, 0x20B => 0x80, 0x20C => 0x100,
                            0x20A => 0x800, 0x20E => 0x1000, _ => 0
                        };
                        var extra = flags is 0x80 or 0x100 or 0x800 or 0x1000
                            ? unchecked((uint)(short)(data.MouseData >> 16)) : 0;
                        if (flags != 0 && controller.ProcessMouse(NativeMethods.Input.Mouse(flags, extra))) return 1;
                    }
                }
                catch (Exception ex) { Fail(ex); }
            }
            return NativeMethods.CallNextHookEx(mouse, code, message, pointer);
        };

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
            void Reset()
            {
                controller.ReleaseAll();
                controller.Initialize(GetHeldKeys());
            }
            using var dispatcher = new HookWindow(Reset);
            _ = dispatcher.Handle;
            _dispatcher = dispatcher;
            using var context = new ApplicationContext();
            void Stop() => context.ExitThread();
            dispatcher.Disposed += (_, _) => Stop();
            controller.Initialize(GetHeldKeys());
            var module = NativeMethods.GetModuleHandle(null);
            keyboard = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardProc, module, 0);
            if (keyboard == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard hook installation failed.");
            mouse = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseProc, module, 0);
            if (mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Mouse hook installation failed.");
            using var timer = new System.Windows.Forms.Timer { Interval = 10 };
            var lastTick = Environment.TickCount64;
            timer.Tick += (_, _) =>
            {
                var now = Environment.TickCount64;
                controller.SetHoldMilliseconds(HoldMilliseconds);
                if (now - lastTick > 5000) Reset();
                else controller.Tick(now);
                lastTick = now;
            };
            timer.Start();
            AppLog.Write($"Hooks installed. INPUT size={Marshal.SizeOf<NativeMethods.Input>()}.");
            _ready.Set();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            if (!_ready.IsSet) _startupError = ex;
            else NotifyFailure(ex);
            AppLog.Write(ex);
        }
        finally
        {
            try { controller.ReleaseAll(); }
            catch (Exception ex) { AppLog.Write(ex); }
            if (mouse != 0) NativeMethods.UnhookWindowsHookEx(mouse);
            if (keyboard != 0) NativeMethods.UnhookWindowsHookEx(keyboard);
            GC.KeepAlive(keyboardProc);
            GC.KeepAlive(mouseProc);
            _ready.Set();
            _dispatcher = null;
        }
    }

    private void Fail(Exception ex)
    {
        AppLog.Write(ex);
        NotifyFailure(ex);
        try { _dispatcher?.BeginInvoke((Action)(() => _dispatcher?.Dispose())); }
        catch (InvalidOperationException) { /* Already shutting down. */ }
    }

    private void NotifyFailure(Exception error)
    {
        try { reportFailure(error); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private static IEnumerable<int> GetHeldKeys() => Enumerable.Range(8, 247)
        .Where(k => k is not (0x10 or 0x11 or NativeMethods.VK_MENU) && (NativeMethods.GetAsyncKeyState(k) & 0x8000) != 0);

    private sealed class HookWindow(Action reset) : HiddenDispatcher
    {
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!NativeMethods.WTSRegisterSessionNotification(Handle, 0))
                AppLog.Write("Session notifications unavailable.");
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.WTSUnRegisterSessionNotification(Handle);
            base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message message)
        {
            if ((message.Msg == 0x2B1 && (int)message.WParam is 7 or 8) ||
                (message.Msg == 0x218 && (int)message.WParam is 7 or 18)) reset();
            base.WndProc(ref message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var dispatcher = _dispatcher;
        if (dispatcher is { IsDisposed: false })
        {
            try { dispatcher.BeginInvoke((Action)dispatcher.Dispose); }
            catch (InvalidOperationException) { /* The worker has already exited. */ }
        }
        if (_thread is { IsAlive: true } && !_thread.Join(TimeSpan.FromSeconds(5)))
            AppLog.Write("Hook thread did not stop within five seconds.");
        if (_thread is null || !_thread.IsAlive) _ready.Dispose();
    }
}
