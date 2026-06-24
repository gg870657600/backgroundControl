using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace backgroundControl
{
    public class EmbeddedCmdManager : IDisposable
    {
        // Win32 API 声明
        [DllImport("user32.dll")]
        private static extern int SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern int MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_DLGFRAME = 0x00400000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOSIZE = 0x0001;


        private Process? _cmdProcess;
        private Panel _cmdPanel;
        private bool _isDisposed;

        public event EventHandler? CmdStarted;
        public event EventHandler? CmdStopped;
        public event EventHandler<Exception>? ErrorOccurred;

        public EmbeddedCmdManager(Panel cmdPanel)
        {
            _cmdPanel = cmdPanel ?? throw new ArgumentNullException(nameof(cmdPanel));
        }

        public void StartEmbeddedCmd()
        {
            try
            {
                if (_cmdProcess != null && !_cmdProcess.HasExited) return;

                _cmdProcess = new Process();
                _cmdProcess.StartInfo.FileName = "cmd.exe";
                _cmdProcess.StartInfo.UseShellExecute = false;
                _cmdProcess.StartInfo.RedirectStandardInput = false;
                _cmdProcess.StartInfo.RedirectStandardOutput = false;
                _cmdProcess.StartInfo.CreateNoWindow = false;
                _cmdProcess.Start();

                // 等待窗口句柄创建
                while (_cmdProcess.MainWindowHandle == IntPtr.Zero)
                {
                    System.Threading.Thread.Sleep(100);
                    Application.DoEvents();
                }

                // 设置父窗口为 Panel
                SetParent(_cmdProcess.MainWindowHandle, _cmdPanel.Handle);

                // 移除标题栏和边框样式
                RemoveWindowStyles();

                // 调整 CMD 窗口大小
                ResizeCmdWindow();

                CmdStarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private void RemoveWindowStyles()
        {
            if (_cmdProcess == null || _cmdProcess.HasExited) return;
            IntPtr hWnd = _cmdProcess.MainWindowHandle;
            if (hWnd == IntPtr.Zero) return;

            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_SYSMENU);
            SetWindowLong(hWnd, GWL_STYLE, style);
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);
        }

        public void ResizeCmdWindow()
        {
            if (_cmdProcess != null && !_cmdProcess.HasExited && _cmdPanel.IsHandleCreated)
            {
                int width = _cmdPanel.ClientRectangle.Width;
                int height = _cmdPanel.ClientRectangle.Height;
                if (width > 0 && height > 0)
                {
                    MoveWindow(_cmdProcess.MainWindowHandle, 0, 0, width, height, true);
                    // 强制刷新窗口内容
                    SetWindowPos(_cmdProcess.MainWindowHandle, IntPtr.Zero, 0, 0, width, height,
                        SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
        }

        public void StopEmbeddedCmd()
        {
            try
            {
                if (_cmdProcess != null && !_cmdProcess.HasExited)
                {
                    _cmdProcess.Kill();
                    _cmdProcess.WaitForExit();
                }
                _cmdProcess = null;
                CmdStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    StopEmbeddedCmd();
                }
                _isDisposed = true;
            }
        }

        ~EmbeddedCmdManager()
        {
            Dispose(false);
        }
    }
}