using System.Runtime.InteropServices;

namespace AltImeSwitcher;

internal static class InputSender
{
    public static void ValidateLayout()
    {
        var expected = IntPtr.Size == 8 ? 40 : 28;
        if (Marshal.SizeOf<NativeMethods.Input>() != expected)
            throw new InvalidOperationException("Win32 INPUT layout is invalid.");
    }

    public static uint Send(NativeMethods.Input[] inputs)
    {
        var count = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (count != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Write($"SendInput failed: {count}/{inputs.Length}, error={error}. Check target privilege level.");
        }
        return count;
    }
}
