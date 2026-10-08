# AltImeSwitcher

[English](README.md) | [日本語](README.ja.md)

C# / .NET WinForms notification-area application using Win32 hooks directly.
No AutoHotkey or keyboard-remapping tool is required. The portable release bundles
the .NET runtime; this is a managed Windows desktop application, not a C++ binary.

Generative AI was used to develop this project, including source code implementation
and refactoring, documentation, and icon image generation. The application does
not use generative AI at runtime.

## Repository Layout

- `AltImeSwitcher/`: application source and project file.
- `AltImeSwitcher/Assets/`: generated PNG source and the ICO embedded in the app.
- `AltImeSwitcher.Tests/`: regression runner and Windows integration checks.
- `tools/BuildIcon.ps1`: rebuilds the ICO from the PNG source.

Build output (`bin/`, `obj/`), local SDK state (`.dotnet/`), IDE per-user files,
logs and test results are excluded by `.gitignore`. Keep both icon assets in
Git; the ICO is required to build. Distribute the published executable separately
as a GitHub Release asset rather than committing it to the source repository.

## Behavior

| Input | Result |
| --- | --- |
| Left Alt alone, released before the selected threshold | Half-width alphanumeric (IME off) |
| Right Alt alone, released before the selected threshold | Hiragana (IME on) |
| Either Alt held for at least the selected threshold | Normal Alt down, followed by Alt up on release |
| Alt followed quickly by another key or mouse button/wheel | Normal Alt combination, even before the threshold |
| Ctrl, Shift, Windows key or another keyboard key held before Alt | Normal combination; no IME switch |
| Both Alt keys together | Both act as Alt; no IME switch |

Right-click the notification-area icon and open `長押し判定` to choose 150, 180,
220 (default), 300, 400 or 500 ms. The selected item has a check mark. Changes
apply to the next Alt press without restarting the hook. An Alt press already
in progress retains the threshold captured at its initial down event.

Settings are saved atomically in `%LOCALAPPDATA%\AltImeSwitcher\settings.json`
and restored at the next launch. Missing or invalid settings use 220 ms. If a
save fails, the new value remains active for the current session and a warning
notification is displayed. Changing the value while paused is also supported.

The generated `Aあ` keycap icon is embedded in both the executable and the tray
icon. The ICO contains 16, 20, 24, 32, 40, 48, 64, 128 and 256 px images.

The app runs only in the notification area. Its internal dispatch windows are
hidden from creation and excluded from the taskbar and Alt+Tab, while still
receiving session and power notifications.

The notification-area menu also contains `Pause` and `Exit`. While paused, all
input passes through. Pause, exit, session lock/unlock and resume clear pending
state and release keys/buttons that the app successfully replayed. Keys already
held when a hook starts pass through until released.

Only physical keyboard/mouse input is transformed. Events injected by other
applications pass through. Ctrl+right Alt (including AltGr) is preserved.
An IME request is discarded if its foreground window changed before execution.

## Run

From the workspace root, the published executable is:

```text
AltImeSwitcher\bin\Release\net9.0-windows\win-x64\publish\AltImeSwitcher.exe
```

Exit the old version before starting the updated executable. Windows may place
its tray icon in the notification area's overflow menu. To start at sign-in,
place a shortcut to the published executable in `shell:startup`.

Logs: `%LOCALAPPDATA%\AltImeSwitcher\AltImeSwitcher.log` (rotates at 1 MiB).
Logs contain diagnostics, not typed text or a history of keystrokes.

## Build

Run from the workspace root with a .NET 9 SDK and Windows Desktop targeting pack:

```powershell
dotnet build .\AltImeSwitcher.Tests\AltImeSwitcher.Tests.csproj -c Release
dotnet publish .\AltImeSwitcher\AltImeSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Use `--self-contained false` instead for a smaller release on machines with the
.NET 9 Windows Desktop Runtime installed. Keep the publish directory together.

## Verification

The regression runner has no external test packages:

```powershell
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe --hook-lifecycle --tray-lifecycle
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe --integration
```

The first command checks ABI layout and deterministic key state transitions,
including short taps, boundaries, repeat, paired Alt keys, ordered replay,
mouse combinations, failures, partial writes, focus changes and cleanup. It also
checks every threshold, in-flight setting changes, settings persistence and
recovery, embedded icon frames and native executable icon extraction.
The second additionally installs/removes real keyboard and mouse hooks 20 times
and starts/exits the real tray context. It exercises the actual threshold menu,
settings file and pause/resume using isolated test settings, without changing
the user's preferences. It also checks native window visibility at startup,
after pause/resume and during the message loop, taskbar exclusion styles and
callback delivery through hidden dispatchers. It does not generate keyboard input.

The integration command opens a probe window and uses specially tagged test
input through the production hook/controller. It checks the OS Alt state and
`WM_SYSKEYDOWN` delivery for left/right Alt+F, fast combinations, short taps and
shutdown. Input is sent only while the probe owns the foreground. If Windows
refuses foreground activation, the test fails before sending input. It verifies
hook/replay behavior; it does not change the real IME or prove IME compatibility.

Manual checks in Notepad: left/right short taps; hold either Alt then press F;
quick Alt+F; long Alt+Tab; Ctrl+Alt combinations; pause/resume; exit while holding
Alt. Switch between alphanumeric, hiragana and katakana before checking IME modes.

## Design

- `AltKeyController`: deterministic state machine with an injectable sender.
- `AltKeyHook`: dedicated STA message loop for both hooks and the hold timer.
- `InputSender` / `NativeMethods`: ABI validation, tagged input, ordered batches,
  return-value checks and pointer-sized Win32 signatures.
- `ImeController`: focused-control lookup and bounded IMM messages on the UI
  thread, away from the hook. Roman/kana preference is preserved.
- `TrayApplicationContext`: pause/resume, generation-checked IME dispatch and
  explicit disposal. Replay failures pause the hook and show a notification.
- `AppSettings`: selectable thresholds, validated preferences and atomic saving.
- `ApplicationIcon`: loads the embedded generated icon without external files.
- `HiddenDispatcher`: permanently hidden top-level windows for UI dispatch and
  power/session messages, without taskbar entries or activation.

Down and up of replayed keys/buttons are both queued so rapidly arriving physical
releases cannot overtake a replayed down. The controller records only successful
insertions and falls back to the original event when replay fails.

## Limits

SendInput is subject to UIPI: an unelevated process cannot inject into an elevated
target. Run at the same privilege level when needed. UAC secure desktops and
the sign-in screen are outside this application's scope. Force termination
cannot run normal cleanup; use the tray's `Exit` command.

IME conversion behavior varies between Microsoft IME, third-party IMEs and
TSF-only controls. `VK_IME_ON/OFF` and focused-window IMM messages are both used,
but successful injection alone cannot prove that a target changed its mode.
Applications that use Raw Input or reject injected input may differ from ordinary
desktop applications. The hold threshold is nominal: Windows timer scheduling
can delay delivery, so elapsed time is also checked at release.

API references:
[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput),
[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc),
[SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw),
[ImmSetConversionStatus limitations](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immsetconversionstatus).

## Icon Source

The generated source image is `AltImeSwitcher\Assets\AltImeSwitcher.png`. Rebuild the ICO from
the workspace root with Windows PowerShell or PowerShell on Windows:

```powershell
.\tools\BuildIcon.ps1 -Source .\AltImeSwitcher\Assets\AltImeSwitcher.png -Destination .\AltImeSwitcher\Assets\AltImeSwitcher.ico
```
