using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Terminal.Wpf;

namespace backgroundControl.Tools
{
    public class TerminalFontZoom
    {
        private static TerminalFontZoom? _instance;
        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc? _proc;
        private readonly List<(TerminalControl ctl, Dispatcher disp, short size)> _terminals = new();
        private readonly object _lock = new();

        public static TerminalFontZoom Instance => _instance ??= new TerminalFontZoom();

        public void Register(TerminalControl terminal, Dispatcher dispatcher)
        {
            lock (_lock)
                _terminals.Add((terminal, dispatcher, 12));
            if (_hookHandle == IntPtr.Zero)
                InstallHook();
        }

        public void Unregister(TerminalControl terminal)
        {
            lock (_lock)
                _terminals.RemoveAll(t => t.ctl == terminal);
            if (_terminals.Count == 0 && _hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _proc = null;
            }
        }

        private void InstallHook()
        {
            _proc = HookProc;
            _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(IntPtr.Zero), 0);
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
            {
                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var delta = (short)(ms.mouseData >> 16);

                lock (_lock)
                {
                    foreach (var (ctl, disp, _) in _terminals)
                    {
                        var hwnd = FindTerminalHwnd(ctl);
                            if (hwnd == IntPtr.Zero) continue;
                            var hwndAt = WindowFromPoint(ms.pt);
                            if (hwndAt == hwnd || IsChild(hwnd, hwndAt))
                            {
                                var idx = _terminals.FindIndex(t => t.ctl == ctl);
                            if (idx < 0) continue;
                            var entry = _terminals[idx];
                            short newSize = (short)(entry.size + (delta > 0 ? 1 : -1));
                            newSize = Math.Clamp(newSize, (short)8, (short)48);
                            _terminals[idx] = (entry.ctl, entry.disp, newSize);
                            disp.BeginInvoke(() => SetFont(ctl, newSize));
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static void SetFont(TerminalControl ctl, short size)
        {
            var theme = new TerminalTheme
            {
                DefaultBackground = 0xFF1E1E1E,
                DefaultForeground = 0xFFD4D4D4,
                DefaultSelectionBackground = 0xFF264F78,
                CursorStyle = CursorStyle.BlinkingBlock,
                ColorTable = new uint[]
                {
                    0xFF000000, 0xFFCD3131, 0xFF0DBC79, 0xFFE5E510,
                    0xFF2472C8, 0xFFBC3FBC, 0xFF11A8CD, 0xFFE5E5E5,
                    0xFF666666, 0xFFF14C4C, 0xFF23D18B, 0xFFF5F543,
                    0xFF3B8EEA, 0xFFD670D6, 0xFF29B8DB, 0xFFE5E5E5
                }
            };
            ctl.SetTheme(theme, "Consolas", size, System.Windows.Media.Colors.Transparent);
        }

        private static IntPtr FindTerminalHwnd(TerminalControl ctl)
        {
            var container = FindVisualChild<TerminalContainer>(ctl);
            return container?.Handle ?? IntPtr.Zero;
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var desc = FindVisualChild<T>(child);
                if (desc != null) return desc;
            }
            return null;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int VK_CONTROL = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
