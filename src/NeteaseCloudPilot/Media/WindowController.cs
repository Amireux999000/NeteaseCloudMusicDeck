using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace Amireux999000.NeteaseCloudPilot.Media;

/// <summary>
/// Controls the NetEase Cloud Music desktop window through Win32 APIs:
/// finding the window, bringing it to the foreground, and sending keyboard
/// shortcuts for actions SMTC does not expose (like, volume, lyrics, mini mode).
/// </summary>
public sealed class WindowController
{
    private readonly ILogger _logger;

    public WindowController(ILogger logger)
    {
        _logger = logger.ForContext<WindowController>();
    }

    // ── Window finding ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the HWND of the NetEase Cloud Music main window, or IntPtr.Zero if not found.
    /// </summary>
    public IntPtr FindWindow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return IntPtr.Zero;
        }

        foreach (var pattern in NeteaseValues.ProcessNamePatterns)
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (proc.ProcessName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                        && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        return proc.MainWindowHandle;
                    }
                }
                catch
                {
                    // Process may have exited; ignore.
                }
            }
        }

        return IntPtr.Zero;
    }

    public bool IsRunning => FindWindow() != IntPtr.Zero;

    /// <summary>
    /// Brings the NetEase Cloud Music window to the foreground.
    /// </summary>
    public bool BringToFront()
    {
        var hwnd = FindWindow();
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            SetForegroundWindow(hwnd);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to bring NetEase Cloud Music window to front.");
            return false;
        }
    }

    /// <summary>
    /// Launches NetEase Cloud Music if it is not already running.
    /// </summary>
    public bool Launch()
    {
        if (IsRunning)
        {
            BringToFront();
            return true;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            // Try common install paths
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Netease", "CloudMusic", "cloudmusic.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Netease", "CloudMusic", "cloudmusic.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Netease", "CloudMusic", "cloudmusic.exe"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return true;
                }
            }

            // Fall back to shell execute (handles Store apps via protocol)
            Process.Start(new ProcessStartInfo("orpheus://") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to launch NetEase Cloud Music.");
            return false;
        }
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    /// <summary>
    /// Sends a key combination to the NetEase Cloud Music window.
    /// The window is briefly brought to the foreground to receive the keys.
    /// </summary>
    public bool SendShortcut(VirtualKey key, params VirtualKey[] modifiers)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        var hwnd = FindWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger.Debug("NetEase Cloud Music window not found for shortcut.");
            return false;
        }

        try
        {
            var wasForeground = GetForegroundWindow() == hwnd;
            if (!wasForeground)
            {
                BringToFront();
                Thread.Sleep(80);
            }

            var inputs = new List<INPUT>();

            // Press modifiers
            foreach (var mod in modifiers)
            {
                inputs.Add(MakeKeyInput(mod, true));
            }

            // Press and release main key
            inputs.Add(MakeKeyInput(key, true));
            inputs.Add(MakeKeyInput(key, false));

            // Release modifiers in reverse
            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                inputs.Add(MakeKeyInput(modifiers[i], false));
            }

            var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                _logger.Debug("SendInput returned 0; no keyboard events were injected.");
            }

            if (!wasForeground)
            {
                Thread.Sleep(50);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send shortcut to NetEase Cloud Music.");
            return false;
        }
    }

    /// <summary>Toggle "Like" for the current song (Alt+L).</summary>
    public bool ToggleLike() => SendShortcut(VirtualKey.VK_L, VirtualKey.VK_MENU);

    /// <summary>Volume up (Ctrl+Up).</summary>
    public bool VolumeUp() => SendShortcut(VirtualKey.VK_UP, VirtualKey.VK_CONTROL);

    /// <summary>Volume down (Ctrl+Down).</summary>
    public bool VolumeDown() => SendShortcut(VirtualKey.VK_DOWN, VirtualKey.VK_CONTROL);

    /// <summary>Toggle mute (Ctrl+M).</summary>
    public bool ToggleMute() => SendShortcut(VirtualKey.VK_M, VirtualKey.VK_CONTROL);

    /// <summary>Toggle desktop lyrics (Ctrl+D).</summary>
    public bool ToggleLyrics() => SendShortcut(VirtualKey.VK_D, VirtualKey.VK_CONTROL);

    // ── Win32 interop ────────────────────────────────────────────────────────

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static INPUT MakeKeyInput(VirtualKey vk, bool keyDown) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = keyDown ? 0u : KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };
}

/// <summary>
/// Subset of virtual-key codes used by NetEase Cloud Music shortcuts.
/// </summary>
public enum VirtualKey : ushort
{
    VK_CONTROL = 0x11,
    VK_MENU = 0x12,    // Alt
    VK_UP = 0x26,
    VK_DOWN = 0x28,
    VK_L = 0x4C,
    VK_M = 0x4D,
    VK_D = 0x44,
}
