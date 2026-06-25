using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using backgroundControl.Tools;

namespace backgroundControl
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<TabItemViewModel> SessionItems { get; set; }
        private int sshSessionCounter = 1;

        // 🔥 Win32 低级键盘钩子 —— 在操作系统层面拦截 Ctrl+F，早于任何窗口消息处理
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc? _hookProc;
        private IntPtr _hookId = IntPtr.Zero;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_F = 0x46;
        private const int VK_CONTROL = 0x11;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public MainWindow()
        {
            InitializeComponent();
            SessionItems = new ObservableCollection<TabItemViewModel>();

            // 1. 默认启动：本地 CMD
            var localCmdControl = new LocalCmdControl();
            SessionItems.Add(new TabItemViewModel
            {
                Header = "本地 CMD",
                Content = localCmdControl,
                IsClosable = false
            });

            // 2. 默认启动：第一个 SSH 会话
            AddNewSshSession();

            SessionTabControl.ItemsSource = SessionItems;
            DataContext = this;

            // 🔥 安装低级键盘钩子，拦截 Ctrl+F
            _hookProc = HookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule!)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
            }

            SshHistoryManager.HistoryChanged += OnHistoryChanged;
        }

        private void OnHistoryChanged()
        {
            Dispatcher.Invoke(() =>
            {
                if (_sidebarOpen) RefreshHistoryList();
            });
        }

        /// <summary>
        /// 低级键盘钩子回调 —— 在操作系统层面拦截 Ctrl+F，不受 HwndHost 影响
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // 检测 Ctrl+F
                if (vkCode == VK_F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // 只在本窗口激活时拦截
                    if (IsActive)
                    {
                        var activeSession = GetActiveSessionControl();
                        if (activeSession != null)
                        {
                            Dispatcher.InvokeAsync(() => activeSession.OpenSearchBar());
                            // 返回非零值，吞掉这个按键，不再传递
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// 获取当前活跃的 SessionControl
        /// </summary>
        private SessionControl? GetActiveSessionControl()
        {
            if (SessionTabControl.SelectedItem is TabItemViewModel tab
                && tab.Content is SessionControl session)
            {
                return session;
            }
            return null;
        }

        /// <summary>
        /// 新建 SSH 会话
        /// </summary>
        private void AddNewSshSession()
        {
            var sessionControl = new SessionControl();

            // 预填上次成功连接的 SSH 信息
            try
            {
                var lastSsh = SshHistoryManager.Load().FirstOrDefault(e => e.ConnectionType == "SSH");
                if (lastSsh != null)
                {
                    sessionControl.IpTextBox.Text = lastSsh.Ip;
                    sessionControl.PortTextBox.Text = lastSsh.Port.ToString();
                    sessionControl.UserTextBox.Text = lastSsh.Username;
                    var pwd = SshHistoryManager.DecryptPassword(lastSsh.Password);
                    sessionControl.PasswordBox.Password = pwd;
                    sessionControl.PasswordVisibleBox.Text = pwd;
                }
            }
            catch { }

            var newSession = new TabItemViewModel
            {
                Header = $"SSH 会话 {sshSessionCounter++}",
                Content = sessionControl
            };
            sessionControl.DataContext = newSession;
            SessionItems.Add(newSession);
            SessionTabControl.SelectedItem = newSession;
        }

        /// <summary>
        /// 新建 串口会话 ✅ 完整实现
        /// </summary>
        private void AddNewSerialSession()
        {
            var serialControl = new SerialPortControl();
            var tab = new TabItemViewModel
            {
                Header = "串口调试",
                Content = serialControl,
                IsClosable = true
            };
            SessionItems.Add(tab);
            SessionTabControl.SelectedItem = tab;
        }

        private void AddNewTelnetSession()
        {
            var telnetControl = new Views.TelnetControl();
            var tab = new TabItemViewModel
            {
                Header = "Telnet 会话",
                Content = telnetControl,
                IsClosable = true
            };
            telnetControl.DataContext = tab;
            SessionItems.Add(tab);
            SessionTabControl.SelectedItem = tab;
        }

        // 按钮：新建 SSH
        private void AddSessionButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewSshSession();
        }

        // 按钮：新建 串口 ✅ 这里就是你要的点击函数
        private void AddSerialSession_Click(object sender, RoutedEventArgs e)
        {
            AddNewSerialSession();
        }

        // 按钮：新建 Telnet
        private void AddTelnetSession_Click(object sender, RoutedEventArgs e)
        {
            AddNewTelnetSession();
        }

        // 工具弹窗：单例非模态
        private ToolsWindow? _toolsWindow;
        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_toolsWindow == null || !_toolsWindow.IsVisible)
            {
                _toolsWindow = new ToolsWindow();
                _toolsWindow.Owner = this;
                _toolsWindow.Closed += (_, _) => _toolsWindow = null;
            }
            _toolsWindow.Show();
            _toolsWindow.Activate();
        }

        // ---------- 侧边栏 ----------
        private bool _sidebarOpen;

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarOpen = !_sidebarOpen;
            SidebarColumn.Width = _sidebarOpen ? new GridLength(200) : new GridLength(0);
            if (_sidebarOpen) RefreshHistoryList();
        }

        private void RefreshHistoryList()
        {
            var entries = SshHistoryManager.Load();
            HistoryList.ItemsSource = entries;
        }

        private void HistoryList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HistoryList.SelectedItem is SshHistoryEntry entry)
            {
                if (entry.ConnectionType == "Telnet")
                {
                    var telnetControl = new Views.TelnetControl();
                    var tab = new TabItemViewModel
                    {
                        Header = $"{entry.Ip}",
                        Content = telnetControl
                    };
                    telnetControl.DataContext = tab;
                    SessionItems.Add(tab);
                    SessionTabControl.SelectedItem = tab;
                    telnetControl.ConnectWithCredentials(entry.Ip, entry.Port, entry.Username, entry.Password);
                }
                else
                {
                    var sessionControl = new SessionControl();
                    var session = new TabItemViewModel
                    {
                        Header = entry.Ip,
                        Content = sessionControl
                    };
                    sessionControl.DataContext = session;
                    SessionItems.Add(session);
                    SessionTabControl.SelectedItem = session;

                    try
                    {
                        var password = SshHistoryManager.DecryptPassword(entry.Password);
                        sessionControl.ConnectWithCredentials(entry.Ip, entry.Port, entry.Username, password);
                    }
                    catch
                    {
                        // 解密失败，让用户手动输入
                    }
                }
            }
        }

        private void HistoryList_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.SelectedItem is SshHistoryEntry entry)
            {
                var list = SshHistoryManager.Load();
                list.RemoveAll(e => string.Equals(e.Ip, entry.Ip, StringComparison.OrdinalIgnoreCase));
                SshHistoryManager.Save(list);
                RefreshHistoryList();
            }
        }

        private void HistoryList_ClearAll_Click(object sender, RoutedEventArgs e)
        {
            SshHistoryManager.Save(new System.Collections.Generic.List<SshHistoryEntry>());
            RefreshHistoryList();
        }

        // 关闭标签
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var tabItemVM = btn?.Tag as TabItemViewModel;
            if (tabItemVM != null)
            {
                // 释放资源
                if (tabItemVM.Content is SessionControl session)
                    session.DisposeSession();
                else if (tabItemVM.Content is LocalCmdControl local)
                    local.Dispose();
                else if (tabItemVM.Content is SerialPortControl serial)
                    serial.Dispose();
                else if (tabItemVM.Content is Views.TelnetControl telnet)
                    telnet.Dispose();

                SessionItems.Remove(tabItemVM);
            }
        }

        // 双击关闭标签
        private void TabControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SessionTabControl.SelectedItem is TabItemViewModel tabItemVM)
            {
                if (tabItemVM.Content is SessionControl session)
                    session.DisposeSession();
                else if (tabItemVM.Content is LocalCmdControl local)
                    local.Dispose();
                else if (tabItemVM.Content is SerialPortControl serial)
                    serial.Dispose();
                else if (tabItemVM.Content is Views.TelnetControl telnet)
                    telnet.Dispose();

                SessionItems.Remove(tabItemVM);
            }
        }

        // 窗口关闭，释放所有
        protected override void OnClosed(EventArgs e)
        {
            // 卸载键盘钩子
            if (_hookId != IntPtr.Zero)
                UnhookWindowsHookEx(_hookId);

            foreach (var item in SessionItems)
            {
                if (item.Content is SessionControl session)
                    session.DisposeSession();
                else if (item.Content is LocalCmdControl local)
                    local.Dispose();
                else if (item.Content is SerialPortControl serial)
                    serial.Dispose();
                else if (item.Content is Views.TelnetControl telnet)
                    telnet.Dispose();
            }
            base.OnClosed(e);
        }
    }

    // 标签页模型（你原版，正确）
    public class TabItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _header;
        private bool _isClosable = true;

        public string Header
        {
            get => _header;
            set
            {
                if (_header != value)
                {
                    _header = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Header)));
                }
            }
        }

        public object Content { get; set; }

        public bool IsClosable
        {
            get => _isClosable;
            set
            {
                if (_isClosable != value)
                {
                    _isClosable = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsClosable)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // 显示/隐藏转换器
    public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
    {
        private static BoolToVisibilityConverter _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ??= new BoolToVisibilityConverter();
        }
    }
}