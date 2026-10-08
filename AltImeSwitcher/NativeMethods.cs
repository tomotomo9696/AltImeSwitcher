using System.Runtime.InteropServices;

namespace AltImeSwitcher;

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    public const int WM_IME_CONTROL = 0x283;
    public const int IMC_GETCONVERSIONMODE = 1, IMC_SETCONVERSIONMODE = 2, IMC_SETOPENSTATUS = 6;
    public const int VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    public const int VK_IME_ON = 0x16, VK_IME_OFF = 0x1A;
    public const int LLKHF_EXTENDED = 1, LLKHF_INJECTED = 0x10;
    public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
    public const int IME_CMODE_NATIVE = 1, IME_CMODE_KATAKANA = 2, IME_CMODE_FULLSHAPE = 8;
    // Only our own replay events bypass the controller. The test tag is accepted only in diagnostics.
    public static readonly nuint ReplayTag = 0x41494D45;
    public static readonly nuint TestTag = 0x41495453;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", ExactSpelling = true, SetLastError = true)]
    public static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("imm32.dll")]
    public static extern nint ImmGetDefaultIMEWnd(nint window);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", ExactSpelling = true, SetLastError = true)]
    public static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardHookData
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHookData
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Data;

        public static Input Keyboard(int key, bool down, nuint tag = 0) => new()
        {
            Type = 1,
            Data = new() { Keyboard = new() { VirtualKey = (ushort)key,
                Flags = down ? 0 : KEYEVENTF_KEYUP, ExtraInfo = tag == 0 ? ReplayTag : tag } }
        };

        public static Input ScanCode(ushort scan, bool extended, bool down, nuint tag = 0) => new()
        {
            Type = 1,
            Data = new() { Keyboard = new() { ScanCode = scan,
                Flags = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                ExtraInfo = tag == 0 ? ReplayTag : tag } }
        };

        public static Input Alt(int key, bool down) => ScanCode(0x38, key == VK_RMENU, down);
        public static Input Mouse(uint flags, uint data = 0) => new()
        {
            Type = 0,
            Data = new() { Mouse = new() { Flags = flags, MouseData = data, ExtraInfo = ReplayTag } }
        };
    }
    // MOUSEINPUT is the largest union member: INPUT must be 40 bytes on x64, 28 on x86.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput { public uint Message; public ushort Low, High; }
}
