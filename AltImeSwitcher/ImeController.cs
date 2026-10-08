using System.Runtime.InteropServices;

namespace AltImeSwitcher;

internal static class ImeController
{
    public static void Apply(ImeRequest request)
    {
        if (request.Foreground == 0 || NativeMethods.GetForegroundWindow() != request.Foreground) return;
        var info = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        var target = NativeMethods.GetGUIThreadInfo(0, ref info) && info.Focus != 0 ? info.Focus : request.Foreground;
        var ime = NativeMethods.ImmGetDefaultIMEWnd(target);
        // VK_IME_ON/OFF supports newer TSF-backed controls. IMM messages supplement legacy controls.
        var key = request.Mode == ImeMode.Hiragana ? NativeMethods.VK_IME_ON : NativeMethods.VK_IME_OFF;
        _ = InputSender.Send([NativeMethods.Input.Keyboard(key, true), NativeMethods.Input.Keyboard(key, false)]);
        if (ime == 0) return;
        if (NativeMethods.GetForegroundWindow() != request.Foreground) return;
        _ = SendControl(ime, NativeMethods.IMC_SETOPENSTATUS, request.Mode == ImeMode.Hiragana ? 1 : 0, out _);
        if (request.Mode == ImeMode.Hiragana)
        {
            if (SendControl(ime, NativeMethods.IMC_GETCONVERSIONMODE, 0, out var mode))
            {
                // Preserve Roman/kana preference and unrelated conversion flags.
                var hiragana = ((int)mode | NativeMethods.IME_CMODE_NATIVE | NativeMethods.IME_CMODE_FULLSHAPE) & ~NativeMethods.IME_CMODE_KATAKANA;
                _ = SendControl(ime, NativeMethods.IMC_SETCONVERSIONMODE, hiragana, out _);
            }
        }
    }

    private static bool SendControl(nint ime, int command, int value, out nuint result)
    {
        var success = NativeMethods.SendMessageTimeout(ime, NativeMethods.WM_IME_CONTROL,
            (nuint)command, value, 0x2 | 0x20, 100, out result) != 0;
        if (!success) AppLog.Write($"IME control failed/timed out: command={command}.");
        return success;
    }
}
