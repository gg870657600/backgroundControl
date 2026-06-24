using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace backgroundControl
{
    public partial class LocalCmdControl : System.Windows.Controls.UserControl, IDisposable
    {
        private EmbeddedCmdManager? _cmdManager;
        private bool _disposed;
        private bool _isStarted = false;

        public LocalCmdControl()
        {
            InitializeComponent();
            Loaded += LocalCmdControl_Loaded;
            // 移除 Unloaded 事件，避免切换 Tab 时销毁进程
            // Unloaded += (s, e) => Dispose();  // 删除这一行
        }

        private void LocalCmdControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isStarted) return;
            _isStarted = true;

            _cmdManager = new EmbeddedCmdManager(CmdPanel);
            _cmdManager.CmdStarted += (s, args) => { };
            _cmdManager.CmdStopped += (s, args) => { };
            _cmdManager.ErrorOccurred += (s, ex) => System.Windows.MessageBox.Show($"CMD 错误: {ex.Message}");
            _cmdManager.StartEmbeddedCmd();

            CmdPanel.SizeChanged += (s, args) => _cmdManager?.ResizeCmdWindow();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _cmdManager?.Dispose();
                _cmdManager = null;
            }
            _disposed = true;
        }
    }
}