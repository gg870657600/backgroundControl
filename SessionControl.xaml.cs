using Microsoft.Terminal.Wpf;
using Microsoft.Win32;  // 用于文件选择对话框
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using backgroundControl.Tools;

namespace backgroundControl
{
    public partial class SessionControl : System.Windows.Controls.UserControl
    {
        private enum ShellEnvironment { Unknown, SshShell, TelnetCli }

        private SshClient? _sshClient = null;
        private ShellStream _globalShell;
        private bool _isConnected = false;
        private string _deviceType = "unknown";
        private List<RuleItem> _ruleItems;
        private List<IntentRule> _intentRules;
        private readonly List<CommandPattern> _commandPatterns;
        private const int MaxHistoryCount = 20;
        private List<string> _inputHistory = new List<string>();
        private const string HistoryFilePath = "command_history.txt";
        private System.Timers.Timer _keepAliveTimer;
        private const int KeepAliveInterval = 60000;
        private System.Timers.Timer _loopTimer;
        private string _loopedCommand;
        private bool _isLooping;
        private bool _isTelnetLoggedIn = false;
        private ShellEnvironment _currentEnv = ShellEnvironment.Unknown;
        private CancellationTokenSource _cmdCts;
        private Task _activeContinuousTask;
        private SshTerminalConnection? _terminalConnection;
        //private LocalProcessConnection? _localConnection;

        private EmbeddedCmdManager? _cmdManager;
        private bool _isInCmdMode;
        private SftpClient? _sftpClient;
        private string _currentRemotePath = "/";
        private readonly IRemoteFileService _fileService;
        private readonly object _logLock = new();
        public StringBuilder _allTerminalOutput = new StringBuilder(); // 终端日志原始缓存
        private StringBuilder _cleanOutput = new StringBuilder();       // ANSI/控制字符已过滤，复制用

        // Telnet 凭据与端口
        private const string TelnetDefaultUser     = "root";
        private const string TelnetDefaultPassword = "Changeme_123";
        private const int    TelnetDefaultPort     = 2323;

        // Telnet 切换序列片段
        private const string EnterTelnetCmd        = "telnet 0 2323";
        private const string TelnetLoginFormat     = "login:{0},{1}";

        // 切换时序（毫秒）
        private const int DelayEnterTelnetMs       = 200;
        private const int DelayAfterLoginMs        = 300;
        private const int DelayExitTelnetCtrlCMs   = 100;
        private const int DelayAfterExitKeyMs      = 300;

        // 串行化环境切换
        private readonly SemaphoreSlim _switchLock = new SemaphoreSlim(1, 1);
        public bool IsSwitching { get; private set; }

        // Ctrl+滚轮缩放终端字号
        private int _terminalFontSize = 12;

        private void EnsureMouseWheelHook()
        {
            backgroundControl.Tools.TerminalFontZoom.Instance.Register(TerminalControl, Dispatcher);
        }


        public SessionControl() : this(new WinSCPFileService()) { }

        public SessionControl(IRemoteFileService fileService)
        {
            _fileService = fileService;
            InitializeComponent();
            Loaded += (_, _) => EnsureMouseWheelHook();
            PasswordBox.Password = "andisat";
            UpdateTelnetStatus(false);
            _currentEnv = ShellEnvironment.Unknown;
            LoadRules();

            _commandPatterns = new List<CommandPattern>
            {
                new CommandPattern
                {
                    Name = "寄存器读取",
                    Regex = new Regex(@"(?:查询|读|查看)?\s*寄存器\s*([0-9a-fA-Fx]+)|(?:^|\s)([0-9a-fA-F]{1,4})(?:\s|$)", RegexOptions.IgnoreCase),
                    CommandBuilder = (match) =>
                    {
                        string addr = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                        if (!addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) addr = "0x" + addr;
                        return $"get-fpga-reg:id=0,addr={addr}";
                    }
                }
            };

            SendButton.IsEnabled = true;
            LoopButton.IsEnabled = true;
            LoadHistory();
            InputComboBox.ItemsSource = _inputHistory;


            // 隐藏左侧文件浏览器
            LeftColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
        }
        //private void OnCmdStarted(object? sender, EventArgs e)
        //{
        //    _isInCmdMode = true;
        //    CmdHost.Visibility = Visibility.Visible;
        //    TerminalControl.Visibility = Visibility.Collapsed;
        //}

        //private void OnCmdStopped(object? sender, EventArgs e)
        //{
        //    _isInCmdMode = false;
        //    CmdHost.Visibility = Visibility.Collapsed;
        //    TerminalControl.Visibility = Visibility.Visible;
        //}

        //private void OnErrorOccurred(object? sender, Exception e)
        //{
        //    System.Windows.MessageBox.Show($"CMD 操作错误: {e.Message}");
        //}
        /// <summary>
        /// 启动本地 CMD 终端
        /// </summary>
        //private void StartLocalTerminal()
        //{
        //    if (_localConnection != null) return;

        //    _localConnection = new LocalProcessConnection();
        //    TerminalControl.Connection = _localConnection;
        //    _localConnection.Start();
        //}

        /// <summary>
        /// 处理终端控件的预览按键事件
        /// </summary>
        private void TerminalControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 注意：此控件内嵌 HwndHost（原生 Win32 窗口），多数按键会绕过 WPF 路由事件
            // Ctrl+F 搜索由 MainWindow 的 InputManager.PreProcessInput 拦截，此处不再处理

            // --------------------------
            // 🔥 未连接 → 直接return，不处理任何按键
            // --------------------------
            if (!_isConnected || _terminalConnection == null)
            {
                e.Handled = false;
                return;
            }

            // 🔥 监听 Ctrl+C：手动退出 Telnet → 强制切换回 SSH 模式
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_currentEnv == ShellEnvironment.TelnetCli)
                {
                    _currentEnv = ShellEnvironment.SshShell;
                    UpdateTelnetStatus(false);
                    StopKeepAliveTimer();
                }
                return;
            }

            // 只处理功能键
            if (e.Key is not (Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Back))
                return;

            switch (e.Key)
            {
                case Key.Tab:
                    _terminalConnection.WriteInput("\t");
                    break;
                case Key.Up:
                    _terminalConnection.WriteInput("\x1b[A");
                    break;
                case Key.Down:
                    _terminalConnection.WriteInput("\x1b[B");
                    break;
                case Key.Left:
                    _terminalConnection.WriteInput("\x1b[D");
                    break;
                case Key.Right:
                    _terminalConnection.WriteInput("\x1b[C");
                    break;
                case Key.Enter:
                    // 发送命令前检查是否有待输入内容，用于判断是否需要延迟触发提示符
                    bool hasPendingInput = _terminalConnection.HasPendingInput;
                    _terminalConnection.WriteInput("\r\n");
                    if (hasPendingInput)
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(300);
                            _terminalConnection.WriteInput("\r\n");
                        });
                    }
                    break;
                case Key.Back:
                    _terminalConnection.WriteInput("\b");
                    break;
            }
            e.Handled = true;
        }


        #region 历史记录 & 规则
        private void LoadHistory()
        {
            _inputHistory.Clear();
            if (File.Exists(HistoryFilePath))
            {
                try { _inputHistory = File.ReadAllLines(HistoryFilePath).Take(MaxHistoryCount).ToList(); }
                catch { }
            }
        }

        private void SaveHistory()
        {
            try { File.WriteAllLines(HistoryFilePath, _inputHistory); } catch { }
        }

        private void UpdateHistory(string input)
        {
            string currentText = InputComboBox.Text;

            _inputHistory.RemoveAll(x => x == input);
            _inputHistory.Insert(0, input);
            if (_inputHistory.Count > MaxHistoryCount)
                _inputHistory.RemoveAt(MaxHistoryCount);

            InputComboBox.ItemsSource = null;
            InputComboBox.ItemsSource = _inputHistory;

            InputComboBox.Text = currentText;

            SaveHistory();
        }

        private void LoadRules()
        {
            _ruleItems = RuleStorage.LoadRules();
            _intentRules = _ruleItems.Select(r => new IntentRule(r.Keywords, r.Command)).ToList();
        }
        #endregion

        #region SSH 连接
        public void ConnectWithCredentials(string ip, int port, string username, string password)
        {
            IpTextBox.Text = ip;
            PortTextBox.Text = port.ToString();
            UserTextBox.Text = username;
            PasswordBox.Password = password;
            ConnectSshWithPassword(ip, username, password);
        }

        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordBox.Visibility == Visibility.Visible)
            {
                PasswordVisibleBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordVisibleBox.Visibility = Visibility.Visible;
            }
            else
            {
                PasswordBox.Password = PasswordVisibleBox.Text;
                PasswordVisibleBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string ip = IpTextBox.Text.Trim();
            string username = UserTextBox.Text.Trim();
            string password = PasswordBox.Password;
            ConnectSshWithPassword(ip, username, password);
        }

        private string ConnectSshWithPassword(string targetIp, string username, string password)
        {
            try
            {
                var port = 22;
                int.TryParse(PortTextBox.Text, out port);

                // 1. 建立 SSH 终端连接（始终使用默认 SSH 端口，保持不变）
                var connectionInfo = new ConnectionInfo(targetIp, port, username,
                    new PasswordAuthenticationMethod(username, password));
                var client = new SshClient(connectionInfo);
                connectionInfo.Timeout = TimeSpan.FromSeconds(3);//超时时间
                client.Connect();

                if (client.IsConnected)
                {
                    _sshClient = client;
                    _isConnected = true;
                    _globalShell = client.CreateShellStream("xterm-256color", 80, 24, 800, 600, 1024 * 1024);
                    var conn = new SshTerminalConnection(new ShellStreamAdapter(_globalShell), cmd => Dispatcher.InvokeAsync(() => AutoSwitchByFullCommand(cmd)));
                    _terminalConnection = conn;
                    var proxy = new HighlightTerminalConnection(conn);
                    proxy.OnRawOutput = text => { lock (_logLock) {
                        _allTerminalOutput.Append(text);
                        _cleanOutput.Append(AnsiStripper.Strip(text));
                    } };
                    TerminalControl.Connection = proxy;
                    _globalShell.WriteLine("export TMOUT=0");
                    // 订阅输出事件，以便在每次输出后滚动到底部
                    conn.TerminalOutput += (sender, args) =>
                    {
                        Dispatcher.InvokeAsync(() => ScrollTerminalToBottom());
                    };

                    _currentEnv = ShellEnvironment.SshShell;
                    UpdateTabHeader(targetIp);

                    Dispatcher.Invoke(() =>
                    {
                        ConnectionStatus.Text = "SSH已连接";
                        ConnectionStatus.Foreground = System.Windows.Media.Brushes.Green;
                        ConnectButton.IsEnabled = false;
                        DisconnectButton.IsEnabled = true;
                        SendButton.IsEnabled = true;
                        LoopButton.IsEnabled = true;
                    });

                    SshHistoryManager.RecordConnection(targetIp, port, username, password);

                }

                // 2. 尝试 SFTP + SCP 回退（最常用）
                bool sftpSuccess = _fileService.Connect(targetIp, port, username, password);
                if (sftpSuccess)
                {
                    LoadRemoteDirectory("/");
                }
                // 连接成功且 SFTP 可用时
                Dispatcher.Invoke(() =>
                {
                    LeftColumn.Width = new GridLength(280);
                    SplitterColumn.Width = new GridLength(5);
                });

                return "连接完成";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"SSH 连接错误: {ex.Message}");
                return $"错误: {ex.Message}";
            }
        }
        private ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }
        /// <summary>
        /// 终端显示滚动到底部
        /// </summary>
        private void ScrollTerminalToBottom()
        {
            var scrollViewer = FindScrollViewer(TerminalControl);
            scrollViewer?.ScrollToEnd();
        }
        #endregion
        #region 左侧文件目录
        private void LoadRemoteDirectory(string path)
        {
            CurrentPathTextBox.Text = path;
            if (!_fileService.IsConnected) return;

            try
            {
                _currentRemotePath = path;
                FileListView.ItemsSource = _fileService.ListDirectory(path);
                UpdateBreadcrumb(path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载目录失败: {ex.Message}");
            }
        }

        // 辅助类 -> RemoteFileInfo.cs

        private void UpdateBreadcrumb(string path)
        {
            BreadcrumbPanel.Children.Clear();
            if (string.IsNullOrEmpty(path)) return;

            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = "";

            // 根目录按钮
            var rootBtn = new System.Windows.Controls.Button
            {
                Content = "/",
                Tag = "/",
                Style = (Style)FindResource("BreadcrumbButtonStyle")
            };
            rootBtn.Click += (s, e) => LoadRemoteDirectory("/");
            BreadcrumbPanel.Children.Add(rootBtn);

            // 添加分隔符
            BreadcrumbPanel.Children.Add(new TextBlock { Style = (Style)FindResource("BreadcrumbSeparatorStyle") });

            for (int i = 0; i < parts.Length; i++)
            {
                current += "/" + parts[i];
                var btn = new System.Windows.Controls.Button
                {
                    Content = parts[i],
                    Tag = current,
                    Style = (Style)FindResource("BreadcrumbButtonStyle")
                };
                btn.Click += (s, e) => LoadRemoteDirectory((s as System.Windows.Controls.Button)?.Tag as string);
                BreadcrumbPanel.Children.Add(btn);

                // 如果不是最后一个，添加分隔符
                if (i < parts.Length - 1)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock { Style = (Style)FindResource("BreadcrumbSeparatorStyle") });
                }
            }
        }
        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selected = FileListView.SelectedItem as RemoteFileInfo;
            if (selected == null) return;
            if (selected.IsDirectory)
                LoadRemoteDirectory(selected.FullPath);
            else
                DownloadRemoteFile(selected.FullPath, selected.Name);
        }
        private void FileListView_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void FileListView_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }
        private async void FileListView_Drop(object sender, System.Windows.DragEventArgs e)
        {
            string[]? files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            if (!_fileService.IsConnected)
            {
                System.Windows.MessageBox.Show("尚未连接远程设备，请先连接", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (System.Windows.MessageBox.Show($"确定要将 {files.Length} 个项目上传到当前目录 {_currentRemotePath} 吗？", "确认上传",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            ShowProgress("准备上传...");

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (string localPath in files)
                {
                    try
                    {
                        string remotePath;
                        if (Directory.Exists(localPath))
                        {
                            string dirName = new DirectoryInfo(localPath).Name;
                            remotePath = _currentRemotePath.TrimEnd('/') + "/" + dirName;
                            _fileService.UploadFile(localPath, remotePath + "/*");
                        }
                        else
                        {
                            string fileName = Path.GetFileName(localPath);
                            remotePath = _currentRemotePath.TrimEnd('/') + "/" + fileName;
                            _fileService.UploadFile(localPath, remotePath);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        errors.Add($"{Path.GetFileName(localPath)}: {ex.Message}");
                    }
                }
            });

            HideProgress();
            LoadRemoteDirectory(_currentRemotePath);

            if (failCount == 0)
                System.Windows.MessageBox.Show($"成功上传 {successCount} 个项目", "上传完成", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                System.Windows.MessageBox.Show($"成功 {successCount} 个，失败 {failCount} 个\n失败详情:\n{string.Join("\n", errors)}", "上传完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        //private System.Windows.Point _startPoint;
        //private bool _isDragging;

        //private void FileListView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        //{
        //    if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        //    {
        //        System.Windows.Point position = e.GetPosition(null);
        //        if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
        //            Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        //        {
        //            StartDrag();
        //        }
        //    }
        //    else if (e.LeftButton == MouseButtonState.Released)
        //    {
        //        _isDragging = false;
        //    }
        //}

        //private void FileListView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        //{
        //    _startPoint = e.GetPosition(null);
        //}

        //private void StartDrag()
        //{
        //    var selectedItems = FileListView.SelectedItems;
        //    if (selectedItems == null || selectedItems.Count == 0) return;

        //    // 只处理文件，目录拖拽下载比较复杂，这里简化为文件
        //    var filesToDownload = selectedItems.Cast<RemoteFileInfo>().Where(f => !f.IsDirectory).ToList();
        //    if (filesToDownload.Count == 0) return;

        //    // 弹出文件夹选择对话框
        //    using (var dialog = new FolderBrowserDialog())
        //    {
        //        dialog.Description = "选择下载保存的文件夹";
        //        if (dialog.ShowDialog() == DialogResult.OK)
        //        {
        //            string localRoot = dialog.SelectedPath;
        //            _ = Task.Run(() => DownloadFilesAsync(filesToDownload, localRoot)); // 异步下载
        //        }
        //    }
        //}

        private void FileListView_GiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true;
            e.Handled = true;
        }
        private RemoteFileInfo? GetSingleSelectedFile()
        {
            return FileListView.SelectedItems.Count == 1
                ? FileListView.SelectedItem as RemoteFileInfo : null;
        }

        private void FileListMenu_OpenView(object sender, RoutedEventArgs e)
        {
            var file = GetSingleSelectedFile();
            if (file == null || file.IsDirectory) return;
            if (!_fileService.IsConnected)
            { System.Windows.MessageBox.Show("未连接"); return; }

            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), file.Name);
                int idx = 1;
                while (File.Exists(tmp))
                    tmp = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(file.Name)}_{idx++}{Path.GetExtension(file.Name)}");

                _fileService.DownloadFile(file.FullPath, tmp);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tmp) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开失败: {ex.Message}");
            }
        }

        private void FileListMenu_Download(object sender, RoutedEventArgs e)
        {
            var file = GetSingleSelectedFile();
            if (file == null || file.IsDirectory) return;
            DownloadRemoteFile(file.FullPath, file.Name);
        }

        private void FileListMenu_Delete(object sender, RoutedEventArgs e)
        {
            DeleteFile_Click(sender, e);
        }

        private void FileListMenu_CopyPath(object sender, RoutedEventArgs e)
        {
            var file = GetSingleSelectedFile();
            if (file == null) return;
            try { System.Windows.Clipboard.SetText(file.FullPath); } catch { }
        }

        private void DownloadRemoteFile(string remotePath, string fileName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = fileName };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _fileService.DownloadFile(remotePath, dlg.FileName);
                    System.Windows.MessageBox.Show("下载完成");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"下载失败: {ex.Message}");
                }
            }
        }
        private async void UploadFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() != true) return;

            ShowProgress("准备上传...");
            int total = dlg.FileNames.Length;
            int completed = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (string localFile in dlg.FileNames)
                {
                    try
                    {
                        string remoteFile = _currentRemotePath.TrimEnd('/') + "/" + Path.GetFileName(localFile);
                        _fileService.UploadFile(localFile, remoteFile);
                        completed++;
                        Dispatcher.Invoke(() => UpdateProgress($"已上传 {completed}/{total}", (int)((completed * 100) / total)));
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(localFile)}: {ex.Message}");
                    }
                }
            });

            HideProgress();
            LoadRemoteDirectory(_currentRemotePath);

            if (errors.Count == 0)
                System.Windows.MessageBox.Show($"成功上传 {completed} 个文件", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                System.Windows.MessageBox.Show($"成功 {completed} 个，失败 {errors.Count} 个\n{string.Join("\n", errors)}", "完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }


        private async void DownloadFile_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FileListView.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0) return;

            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                string localRoot = dialog.SelectedPath;

                ShowProgress("准备下载...");
                int total = selectedItems.Count;
                int completed = 0;
                var errors = new List<string>();

                await Task.Run(() =>
                {
                    foreach (RemoteFileInfo item in selectedItems)
                    {
                        try
                        {
                            string localPath;
                            if (item.IsDirectory)
                            {
                                localPath = Path.Combine(localRoot, item.Name.TrimEnd('/'));
                                string remoteDir = item.FullPath.TrimEnd('/') + "/*";
                                _fileService.DownloadFile(remoteDir, localPath);
                            }
                            else
                            {
                                localPath = Path.Combine(localRoot, item.Name);
                                _fileService.DownloadFile(item.FullPath, localPath);
                            }
                            completed++;
                            Dispatcher.Invoke(() => UpdateProgress($"已下载 {completed}/{total}", (int)((completed * 100) / total)));
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{item.Name}: {ex.Message}");
                        }
                    }
                });

                HideProgress();
                if (errors.Count == 0)
                    System.Windows.MessageBox.Show($"成功下载 {completed} 个项目", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    System.Windows.MessageBox.Show($"成功 {completed} 个，失败 {errors.Count} 个\n{string.Join("\n", errors)}", "完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FileListView.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("请选择要删除的文件或目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasDir = selectedItems.Cast<RemoteFileInfo>().Any(i => i.IsDirectory);
            string warnMsg = hasDir
                ? "包含目录，将被递归删除，不可恢复！"
                : "文件将被永久删除，不可恢复！";
            if (System.Windows.MessageBox.Show($"确定删除选中的 {selectedItems.Count} 个项目吗？\n{warnMsg}", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (RemoteFileInfo item in selectedItems)
            {
                try
                {
                    _fileService.RemoveFiles(item.FullPath);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            LoadRemoteDirectory(_currentRemotePath);

            if (failCount == 0)
                System.Windows.MessageBox.Show($"成功删除 {successCount} 个项目", "删除完成", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                System.Windows.MessageBox.Show($"成功 {successCount} 个，失败 {failCount} 个\n失败详情:\n{string.Join("\n", errors)}",
                                "删除完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        // 实现 DownloadFilesAsync 方法（异步下载文件）
        private async Task DownloadFilesAsync(List<RemoteFileInfo> files, string localRoot)
        {
            int success = 0, fail = 0;
            var errors = new List<string>();
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        string localPath = System.IO.Path.Combine(localRoot, file.Name);
                        _fileService.DownloadFile(file.FullPath, localPath);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errors.Add($"{file.Name}: {ex.Message}");
                    }
                }
            });
            await Dispatcher.InvokeAsync(() =>
            {
                if (fail == 0)
                    System.Windows.MessageBox.Show($"成功下载 {success} 个文件", "下载完成", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    System.Windows.MessageBox.Show($"成功 {success} 个，失败 {fail} 个\n{string.Join("\n", errors)}", "下载完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void ShowProgress(string initialText)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = initialText;
                TransferProgressBar.Value = 0;
            });
        }

        private void UpdateProgress(string status, int percent)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = status;
                TransferProgressBar.Value = percent;
            });
        }

        private void HideProgress()
        {
            Dispatcher.Invoke(() =>
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                TransferProgressBar.Value = 0;
            });
        }
        #endregion
        #region 保活
        private void StartKeepAliveTimer()
        {
            if (_keepAliveTimer == null)
            {
                _keepAliveTimer = new System.Timers.Timer(KeepAliveInterval);
                _keepAliveTimer.Elapsed += async (s, e) =>
                {
                    if (_currentEnv == ShellEnvironment.TelnetCli)
                        try
                        {
                            _globalShell.WriteLine("get-ne-type");                
                            // 等待设备返回命令结果和提示符
                            await Task.Delay(300);
                            _globalShell.WriteLine("");
                        }
                        catch { }
                };
                _keepAliveTimer.AutoReset = true;
            }
            _keepAliveTimer.Start();
        }

        private void StopKeepAliveTimer() => _keepAliveTimer?.Stop();
        #endregion

        private async Task DetectDeviceTypeAsync()
        {
            string response = await ExecuteRemoteCommandAsync("get-ne-type");
            _deviceType = response.Contains("E600X") || response.Contains("E300X") ? "dual"
                        : response.Contains("E600T") || response.Contains("E300T") ? "single" : "unknown";
        }

        #region 命令匹配
        private string MatchCommand(string userInput)
        {
            return CommandClassifier.MatchCommand(userInput, _commandPatterns, _intentRules);
        }

        private bool IsDirectCommand(string input)
        {
            return CommandClassifier.IsDirectCommand(input);
        }
        #endregion

        #region 按钮事件
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                System.Windows.MessageBox.Show("SSH 连接已断开，请重新连接设备。");
                // 可选：更新 _isConnected 状态并清理 UI
                DisconnectButton_Click(null, null);
                return;
            }

            string input = InputComboBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            UpdateHistory(input);

            var (isLinuxCmd, finalCmd) = ClassifyCommand(input);

            // 切换到命令所需环境（已在目标环境则 SwitchToAsync 内部快速返回）
            await SwitchToAsync(toTelnet: !isLinuxCmd);

            try
            {
                _globalShell.WriteLine(finalCmd);

                // 等待设备返回命令结果和提示符
                await Task.Delay(300);
                _globalShell.WriteLine("");
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                System.Windows.MessageBox.Show($"SSH 连接已断开，请重新连接: {ex.Message}");
                DisconnectButton_Click(null, null);
            }
        }

        private void LoopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLooping)
            {
                StopLoop();
                return;
            }

            string input = InputComboBox.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                System.Windows.MessageBox.Show("请输入要循环下发的指令");
                return;
            }

            var (_, finalCmd) = ClassifyCommand(input);
            _loopedCommand = finalCmd;

            if (!int.TryParse(IntervalBox.Text, out var seconds) || seconds < 1)
            {
                System.Windows.MessageBox.Show("间隔时间必须 ≥ 1 秒");
                return;
            }

            _loopTimer = new System.Timers.Timer(seconds * 1000);
            _loopTimer.Elapsed += async (_, _) =>
            {
                try
                {
                    if (_sshClient == null || !_sshClient.IsConnected)
                    {
                        Dispatcher.Invoke(() => StopLoop());
                        return;
                    }
                    _globalShell.WriteLine(_loopedCommand);
                    await Task.Delay(100);
                    _globalShell.WriteLine("");
                }
                catch { }
            };
            _loopTimer.AutoReset = true;
            _loopTimer.Start();
            _isLooping = true;

            LoopButton.Content = "⏹ 停止";
            LoopButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
            SendButton.IsEnabled = false;
            InputComboBox.IsEnabled = false;
            IntervalBox.IsEnabled = false;

            if (_currentEnv != ShellEnvironment.TelnetCli)
                _ = SwitchToAsync(toTelnet: true);
        }

        private void StopLoop()
        {
            _loopTimer?.Stop();
            _loopTimer?.Dispose();
            _loopTimer = null;
            _isLooping = false;

            LoopButton.Content = "🔄 循环下发";
            LoopButton.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
            SendButton.IsEnabled = true;
            InputComboBox.IsEnabled = true;
            IntervalBox.IsEnabled = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. 检查 SSH 客户端和 ShellStream 是否有效
            if (_sshClient == null || !_sshClient.IsConnected || _globalShell == null)
            {
                System.Windows.MessageBox.Show("SSH 连接已断开，无法发送中断信号。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _globalShell.Write("\x03");
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                System.Windows.MessageBox.Show($"SSH 连接已断开，无法停止: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 可选：自动断开并清理 UI 状态
                DisconnectButton_Click(null, null);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 清空本地日志缓存
                lock (_logLock)
                {
                    _allTerminalOutput.Clear();
                    _cleanOutput.Clear();
                }
                _searchMatchPositions.Clear();
                _currentSearchIndex = -1;
                SearchResultText.Text = "";

                // 2. 通过 TerminalOutput 发送 VT100 清屏序列，让原生终端引擎清除显示
                //    \x1b[2J = 清除可见屏幕
                //    \x1b[3J = 清除滚动回看缓冲区（scrollback）
                //    \x1b[H  = 光标移到左上角
                if (_terminalConnection != null)
                {
                    _terminalConnection.WriteLog("\x1b[2J\x1b[3J\x1b[H");
                }
            }
            catch { }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _fileService.Disconnect();

            // 关闭 SSH 会话
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
            _sshClient = null;
            _isConnected = false;
            _currentEnv = ShellEnvironment.Unknown;

            _sftpClient?.Disconnect();
            _sftpClient?.Dispose();
            _sftpClient = null;

            Dispatcher.Invoke(() =>
            {
                ConnectionStatus.Text = "SSH未连接";
                ConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                SendButton.IsEnabled = false;
                LoopButton.IsEnabled = false;
                // 隐藏文件面板
                LeftColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
            });

            UpdateTelnetStatus(false);
        }
        #endregion

        #region 远程命令
        private async Task<string> ExecuteRemoteCommandAsync(string cmd)
        {
            return await Task.Run(() =>
            {
                try { _globalShell.WriteLine(cmd); Thread.Sleep(1000); return _globalShell.Read(); }
                catch { return "执行错误"; }
            });
        }

        private string ReconnectTelnet()
        {
            try
            {
                _globalShell.WriteLine(EnterTelnetCmd);
                Thread.Sleep(DelayEnterTelnetMs);
                _globalShell.WriteLine(string.Format(
                    TelnetLoginFormat, TelnetDefaultUser, TelnetDefaultPassword));
                Thread.Sleep(DelayAfterLoginMs);
                _globalShell.Read();
                UpdateTelnetStatus(true);
                StartKeepAliveTimer();
                _currentEnv = ShellEnvironment.TelnetCli;
                return "重连成功";
            }
            catch { return "重连失败"; }
        }

        private void UpdateTelnetStatus(bool loggedIn)
        {
            _isTelnetLoggedIn = loggedIn;
            Dispatcher.Invoke(() =>
            {
                TelnetStatus.Text = loggedIn ? "Telnet: 已登录" : "Telnet: 未登录";
                TelnetStatus.Foreground = loggedIn ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            });
        }

        private (bool isLinux, string finalCmd) ClassifyCommand(string input)
        {
            return CommandClassifier.ClassifyCommand(input, _commandPatterns, _intentRules);
        }

        /// <summary>
        /// 串行化的环境切换：进入 telnet (toTelnet=true) 或 退出 telnet (toTelnet=false)。
        /// - 同一时刻只有一个切换动作在执行
        /// - 已在目标环境时直接返回
        /// - 异常时回滚 _currentEnv 到调用前值
        /// </summary>
        private async Task SwitchToAsync(bool toTelnet)
        {
            await _switchLock.WaitAsync();
            try
            {
                // 已在目标环境，无需切换
                bool inTelnet = _currentEnv == ShellEnvironment.TelnetCli;
                if (toTelnet == inTelnet) return;

                var previousEnv = _currentEnv;
                IsSwitching = true;
                try
                {
                    if (toTelnet)
                    {
                        _globalShell.WriteLine(EnterTelnetCmd);
                        await Task.Delay(DelayEnterTelnetMs);
                        _globalShell.WriteLine(string.Format(
                            TelnetLoginFormat, TelnetDefaultUser, TelnetDefaultPassword));
                        await Task.Delay(DelayAfterLoginMs);
                        _currentEnv = ShellEnvironment.TelnetCli;
                        UpdateTelnetStatus(true);
                        StartKeepAliveTimer();
                    }
                    else
                    {
                        _globalShell.Write("\x03");
                        await Task.Delay(DelayExitTelnetCtrlCMs);
                        _globalShell.WriteLine("e");
                        await Task.Delay(DelayAfterExitKeyMs);
                        _currentEnv = ShellEnvironment.SshShell;
                        UpdateTelnetStatus(false);
                        StopKeepAliveTimer();
                    }
                }
                catch
                {
                    // 切换失败：回滚到调用前状态
                    _currentEnv = previousEnv;
                    throw;
                }
                finally
                {
                    IsSwitching = false;
                }
            }
            finally
            {
                _switchLock.Release();
            }
        }
        #endregion

        private async void SendRuleCommand(string cmd)
        {
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                System.Windows.MessageBox.Show("SSH 连接已断开，请重新连接设备。");
                return;
            }
            try
            {
                await SwitchToAsync(toTelnet: true);
                _globalShell.WriteLine(cmd);
                await Task.Delay(300);
                _globalShell.WriteLine("");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"发送命令失败: {ex.Message}");
            }
        }

        private void EditRulesButton_Click(object sender, RoutedEventArgs e)
        {
            var w = new RuleEditorWindow(new ObservableCollection<RuleItem>(_ruleItems));
            w.OnSendCommand = SendRuleCommand;
            if (w.ShowDialog() == true)
            {
                _ruleItems = w.Rules.ToList();
                RuleStorage.SaveRules(_ruleItems);
                _intentRules = _ruleItems.Select(r => new IntentRule(r.Keywords, r.Command)).ToList();
                System.Windows.MessageBox.Show("规则已更新");
            }
        }

        private void InputComboBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!_isConnected || _sshClient == null) return;

                SendButton_Click(sender, e);
                e.Handled = true;
            }
        }

        public void DisposeSession()
        {
            try
            {
                if (_isLooping) Dispatcher.Invoke(() => StopLoop());
                StopKeepAliveTimer();
                _fileService.Disconnect();

                // 关闭 SSH
                if (_sshClient != null)
                {
                    if (_sshClient.IsConnected)
                        _sshClient.Disconnect();

                    _sshClient.Dispose();
                    _sshClient = null;

                    _sftpClient?.Disconnect();
                    _sftpClient?.Dispose();
                    _sftpClient = null;
                }

                _isConnected = false;
            }
            catch { }
        }

        public void UpdateTabHeader(string ip)
        {
            Dispatcher.Invoke(() =>
            {
                if (DataContext is TabItemViewModel tabItem)
                {
                    tabItem.Header = $"SSH {ip}";
                }
            });
        }
        /// <summary>
        /// 给 SshTerminalConnection 调用：根据完整命令自动切换模式(SSH/Telnet)
        /// </summary>
        public async Task AutoSwitchByFullCommand(string cmd)
        {
            try
            {
                var (needsSwitch, toTelnet, finalCmd) = CommandClassifier.GetSwitchDecision(
                    cmd, _commandPatterns, _intentRules, _currentEnv == ShellEnvironment.TelnetCli);
                if (needsSwitch)
                {
                    await SwitchToAsync(toTelnet);
                    _globalShell.WriteLine(finalCmd);
                }
            }
            catch { }
        }


        /// <summary>
        /// 复制控制台所有日志到剪贴板
        /// </summary>
        private void CopyAllLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string processed;
                lock (_logLock)
                    processed = LogClipboardHelper.PrepareLog(_cleanOutput).text;

                if (string.IsNullOrWhiteSpace(processed))
                {
                    System.Windows.MessageBox.Show("暂无日志可复制", "提示");
                    return;
                }

                System.Windows.Clipboard.SetText(processed, System.Windows.TextDataFormat.UnicodeText);
                System.Windows.MessageBox.Show($"✅ 已复制 {processed.Length} 字符控制台日志！", "成功");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "错误");
            }
        }

        #region 终端搜索功能
        private List<int> _searchMatchPositions = new List<int>();
        private int _currentSearchIndex = -1;
        private string _lastSearchTerm = "";
        private const int MaxSearchResults = 200; // 最多显示的搜索结果行数
        private Microsoft.Terminal.Wpf.TerminalContainer? _cachedTerminalContainer = null;

        /// <summary>
        /// 获取终端内嵌的 TerminalContainer（HwndHost），用于调用 UserScroll 等原生 API
        /// </summary>
        private Microsoft.Terminal.Wpf.TerminalContainer? FindTerminalContainer()
        {
            if (_cachedTerminalContainer != null) return _cachedTerminalContainer;
            _cachedTerminalContainer = FindVisualChild<Microsoft.Terminal.Wpf.TerminalContainer>(TerminalControl);
            return _cachedTerminalContainer;
        }

        /// <summary>
        /// 在可视化树中递归查找指定类型的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var descendent = FindVisualChild<T>(child);
                if (descendent != null) return descendent;
            }
            return null;
        }

        /// <summary>
        /// 通过反射调用 TerminalContainer 的 internal UserScroll(int viewTop) 方法
        /// </summary>
        private static void InvokeUserScroll(Microsoft.Terminal.Wpf.TerminalContainer container, int viewTop)
        {
            var method = typeof(Microsoft.Terminal.Wpf.TerminalContainer).GetMethod(
                "UserScroll",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (method != null)
            {
                method.Invoke(container, new object[] { viewTop });
            }
        }

        /// <summary>
        /// 通过反射获取 TerminalContainer 的 internal Rows 属性
        /// </summary>
        private static int GetContainerRows(Microsoft.Terminal.Wpf.TerminalContainer container)
        {
            var prop = typeof(Microsoft.Terminal.Wpf.TerminalContainer).GetProperty(
                "Rows",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (prop != null) return (int)prop.GetValue(container)!;
            return 24;
        }

        /// <summary>
        /// 计算匹配位置在终端缓冲区中的大致可视行号
        /// </summary>
        private int CalculateTerminalViewTop(int matchPosition)
        {
            string logText;
            lock (_logLock) logText = _cleanOutput.ToString();

            int columns = TerminalControl.Columns;
            if (columns <= 0) columns = 80;

            int cleanMatchPos = Math.Min(matchPosition, logText.Length - 1);
            if (cleanMatchPos < 0) cleanMatchPos = 0;

            // 计算匹配位置之前有多少个逻辑行
            string textBeforeMatch = logText.Substring(0, cleanMatchPos);
            var logicalLines = textBeforeMatch.Split('\n');

            // 每个逻辑行在终端中可能因为列宽而折行，计算总可视行数
            int totalVisualLines = 0;
            for (int i = 0; i < logicalLines.Length; i++)
            {
                string line = logicalLines[i].TrimEnd('\r');
                if (line.Length == 0)
                    totalVisualLines += 1;
                else
                    totalVisualLines += (int)Math.Ceiling((double)line.Length / columns);
            }

            // 将匹配行显示在可视区域中上部（预留几行上下文）
            int viewTop = Math.Max(0, totalVisualLines - 3);
            return viewTop;
        }

        /// <summary>
        /// 打开搜索栏（公开方法，供 MainWindow Ctrl+F 调用）
        /// </summary>
        public void OpenSearchBar()
        {
            // 尝试获取终端中选中的文本，自动填入搜索框
            try
            {
                string selected = TerminalControl.GetSelectedText()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(selected))
                {
                    // 如果选中多行，只取第一行（避免换行符干扰搜索）
                    var firstLine = selected.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)[0];
                    SearchTextBox.Text = firstLine;
                }
            }
            catch { }

            SearchBar.Visibility = Visibility.Visible;
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }

        /// <summary>
        /// 关闭搜索栏
        /// </summary>
        private void CloseSearchBar()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
            SearchResultText.Text = "";
            _searchMatchPositions.Clear();
            _currentSearchIndex = -1;
            _lastSearchTerm = "";
        }

        /// <summary>
        /// 搜索栏按键事件（Enter搜索，Escape关闭）
        /// </summary>
        private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformSearch(SearchTextBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseSearchBar();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 执行搜索：在终端日志缓存中查找所有匹配行，并高亮显示
        /// </summary>
        private void PerformSearch(string keyword)
        {
            _searchMatchPositions.Clear();
            _currentSearchIndex = -1;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                SearchResultText.Text = "";
                SearchResultsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            string logText;
            lock (_logLock) logText = _cleanOutput.ToString();
            if (string.IsNullOrEmpty(logText))
            {
                SearchResultText.Text = "无内容";
                SearchResultsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // 1. 查找所有匹配位置
            int idx = 0;
            while (idx < logText.Length)
            {
                int pos = logText.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;
                _searchMatchPositions.Add(pos);
                idx = pos + 1;
            }

            if (_searchMatchPositions.Count == 0)
            {
                SearchResultText.Text = "无匹配";
                _lastSearchTerm = "";
                SearchResultsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _lastSearchTerm = keyword;
            _currentSearchIndex = 0;
            UpdateSearchResultText();

            // 2. 提取匹配的行，并在搜索结果面板中高亮显示
            BuildSearchResultsPanel(logText, keyword);

            // 3. 自动跳转到第一个匹配位置
            ScrollTerminalToMatch(_searchMatchPositions[0]);
        }

        /// <summary>
        /// 构建搜索结果面板：提取匹配行，关键词用黄色背景高亮
        /// </summary>
        private void BuildSearchResultsPanel(string logText, string keyword)
        {
            // 按行分割日志
            var lines = logText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var resultItems = new List<TextBlock>();
            int matchCount = 0;
            int matchedLineCount = 0;

            foreach (var rawLine in lines)
            {
                if (matchedLineCount >= MaxSearchResults) break;

                // 清理 ANSI 转义序列以便在搜索结果中显示纯文本
                string line = Regex.Replace(rawLine, @"\x1b\[[0-9;]*[A-Za-z]", "");
                line = Regex.Replace(line, @"\x1b\][^\x07]*(\x07|\x1b\\)", "");

                // 检查此行是否包含关键词
                int pos = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) continue;

                matchedLineCount++;

                // 构建带高亮的 TextBlock
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(2, 1, 2, 1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = matchCount  // 记录这是第几个匹配行
                };

                // 逐段添加 Run：普通文本 + 高亮关键词 + 普通文本 ...
                int lastEnd = 0;
                while (pos >= 0)
                {
                    // 普通文本段
                    if (pos > lastEnd)
                        tb.Inlines.Add(new Run(line.Substring(lastEnd, pos - lastEnd)));

                    // 高亮关键词段
                    tb.Inlines.Add(new Run(line.Substring(pos, keyword.Length))
                    {
                        Background = System.Windows.Media.Brushes.Yellow,
                        FontWeight = FontWeights.Bold
                    });

                    matchCount++;
                    lastEnd = pos + keyword.Length;
                    pos = line.IndexOf(keyword, lastEnd, StringComparison.OrdinalIgnoreCase);
                }

                // 剩余普通文本
                if (lastEnd < line.Length)
                    tb.Inlines.Add(new Run(line.Substring(lastEnd)));

                // 点击行时跳转到终端中对应位置
                tb.MouseLeftButtonDown += (s, e) =>
                {
                    try
                    {
                        var clickedTb = s as TextBlock;
                        if (clickedTb != null && clickedTb.Tag is int matchIdx && matchIdx < _searchMatchPositions.Count)
                        {
                            _currentSearchIndex = matchIdx;
                            UpdateSearchResultText();
                            ScrollTerminalToMatch(_searchMatchPositions[matchIdx]);
                            HighlightCurrentSearchResult();
                        }
                    }
                    catch { }
                };

                resultItems.Add(tb);
            }

            // 显示搜索结果面板
            SearchResultsList.ItemsSource = resultItems;
            SearchResultsPanel.Visibility = resultItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 滚动终端到指定匹配位置
        /// </summary>
        private void ScrollTerminalToMatch(int matchPosition)
        {
            try
            {
                var container = FindTerminalContainer();
                if (container != null)
                {
                    int viewTop = CalculateTerminalViewTop(matchPosition);
                    InvokeUserScroll(container, viewTop);
                }
            }
            catch { }
        }

        /// <summary>
        /// 搜索下一个
        /// </summary>
        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMatchPositions.Count == 0)
            {
                PerformSearch(SearchTextBox.Text);
                return;
            }

            _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatchPositions.Count;
            UpdateSearchResultText();
            ScrollTerminalToMatch(_searchMatchPositions[_currentSearchIndex]);
            HighlightCurrentSearchResult();
        }

        /// <summary>
        /// 搜索上一个
        /// </summary>
        private void SearchPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMatchPositions.Count == 0)
            {
                PerformSearch(SearchTextBox.Text);
                return;
            }

            _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatchPositions.Count) % _searchMatchPositions.Count;
            UpdateSearchResultText();
            ScrollTerminalToMatch(_searchMatchPositions[_currentSearchIndex]);
            HighlightCurrentSearchResult();
        }

        /// <summary>
        /// 关闭搜索按钮
        /// </summary>
        private void SearchClose_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchBar();
        }

        /// <summary>
        /// 更新搜索结果文本显示
        /// </summary>
        private void UpdateSearchResultText()
        {
            if (_searchMatchPositions.Count > 0)
                SearchResultText.Text = $"{_currentSearchIndex + 1}/{_searchMatchPositions.Count}";
            else
                SearchResultText.Text = "无匹配";
        }

        /// <summary>
        /// 高亮当前搜索结果（在搜索结果面板中标记当前项）
        /// </summary>
        private void HighlightCurrentSearchResult()
        {
            if (SearchResultsList.ItemsSource == null) return;

            // 计算当前匹配项所在的行索引
            var items = SearchResultsList.ItemsSource as List<TextBlock>;
            if (items == null || items.Count == 0) return;

            // 简单轮转高亮：将当前索引映射到结果行
            int lineIndex = _currentSearchIndex % items.Count;

            // 清除所有行的高亮背景，只高亮当前行
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Background = (i == lineIndex)
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 120, 212))  // 淡蓝色当前行
                    : null;
            }

            // 滚动到当前项
            if (lineIndex < SearchResultsList.Items.Count)
            {
                var container = SearchResultsList.ItemContainerGenerator.ContainerFromIndex(lineIndex);
                if (container is FrameworkElement fe)
                    fe.BringIntoView();
            }

            SearchTextBox.Focus();
        }
        #endregion
    }

    /// <summary>
    /// SSH 终端连接
    /// </summary>
    public class SshTerminalConnection : ITerminalConnection
    {
        private readonly IShellStream _stream;
        private readonly Action<string>? _commandHandler;

        public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

        private readonly StringBuilder _inputBuffer = new();

        public bool HasPendingInput => _inputBuffer.Length > 0;

        public SshTerminalConnection(IShellStream stream, Action<string>? commandHandler = null)
        {
            _stream = stream;
            _commandHandler = commandHandler;
        }

        public void WriteLog(string text)
        {
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
        }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                while (true)
                {
                    try
                    {
                        int n = await _stream.ReadAsync(buffer, 0, buffer.Length);
                        if (n <= 0) break;

                        string text = Encoding.UTF8.GetString(buffer, 0, n);

                        TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
                    }
                    catch
                    {
                        break;
                    }
                }
            });
        }

        public void Close() { }

        public void WriteInput(string data)
        {
            if (_stream == null || !_stream.CanWrite)
                return;

            try
            {
                foreach (char c in data)
                {
                    // 回车 - 只处理 \r，跳过 \n（避免重复处理）
                    if (c == '\r')
                    {
                        string cmd = _inputBuffer.ToString().Trim();
                        _inputBuffer.Clear();
                        _stream.Write("\r");  // ✅ 修复：写入完整的换行序列

                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            _commandHandler?.Invoke(cmd);
                        }
                    }
                    // 跳过 \n（因为 \r 已经处理了完整的换行）
                    else if (c == '\n')
                    {
                        // 不做任何处理
                    }
                    // 退格
                    else if (c == '\b')
                    {
                        if (_inputBuffer.Length > 0)
                            _inputBuffer.Length--;

                        _stream.Write(c.ToString());
                    }
                    // 可打印字符
                    else if (!char.IsControl(c))
                    {
                        _inputBuffer.Append(c);
                        _stream.Write(c.ToString());
                    }
                    // 其他控制字符（方向键等）
                    else
                    {
                        _stream.Write(c.ToString());
                    }
                }
            }
            catch { }
        }

        public void Resize(uint rows, uint columns) { }
    }

    public class RuleItem
    {
        public string Keywords { get; set; } = "";
        public string Command { get; set; } = "";
        public List<string> KeywordList => Keywords.Split(new[] { ' ', '、' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        public List<string> NormalizedKeywords => KeywordList.Select(k => k.Replace(" ", "").ToLowerInvariant()).ToList();
    }

    public static class RuleStorage
    {
        private static readonly string FilePath = "rules.json";

        public static void SaveRules(List<RuleItem> rules)
        {
            try
            {
                var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存规则失败: {ex.Message}");
            }
        }

        public static List<RuleItem> LoadRules()
        {
            if (!File.Exists(FilePath))
                return GetDefaultRules();

            try
            {
                var json = File.ReadAllText(FilePath);
                var rules = JsonSerializer.Deserialize<List<RuleItem>>(json);
                return rules ?? GetDefaultRules();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载规则失败: {ex.Message}");
                return GetDefaultRules();
            }
        }

        private static List<RuleItem> GetDefaultRules()
        {
            return new List<RuleItem>
            {
                new RuleItem { Keywords = "版本 ver 查询版本 查看版本 软件版本", Command = "ver" },
                new RuleItem { Keywords = "LAN IP 业务IP", Command = "get-lan-ip" },
                new RuleItem { Keywords = "管理IP 设备IP", Command = "get-ne-ip" },
                new RuleItem { Keywords = "satIP 高层信令IP sat 卫星IP", Command = "get-sat-ip" },
                new RuleItem { Keywords = "AUPC 查看AUPC", Command = "get-if-aupc" },
                new RuleItem { Keywords = "关闭AUPC 关aupc aupc关", Command = "set-if-aupc:aupc=disable@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "开启AUPC 开aupc aupc开", Command = "set-if-aupc:aupc=enable@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "上下线原因 事件记录 离线原因 离线", Command = "get-ne-event" },
                new RuleItem { Keywords = "接收增益 增益", Command = "dump-app.cfg.if:getrxgain" },
                new RuleItem { Keywords = "登录请求 登录状态", Command = "dump-app.logon" },
                new RuleItem { Keywords = "发射开关 TX开关", Command = "get-if-tx-switch" },
                new RuleItem { Keywords = "接收开关 RX开关", Command = "get-if-rx-switch" },
                new RuleItem { Keywords = "TDMA发射开 发射开", Command = "set-if-tx-switch:switch=on@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "SCPC发射开", Command = "set-if-tx-switch:switch=on@bid=4&pid=1&chid=1" },
                new RuleItem { Keywords = "TDMA发射关 发射关", Command = "set-if-tx-switch:switch=off@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "SCPC发射关", Command = "set-if-tx-switch:switch=off@bid=4&pid=1&chid=1" },
                new RuleItem { Keywords = "TDMA接收开 接收开", Command = "set-if-rx-switch:switch=on@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "SCPC接收开", Command = "set-if-rx-switch:switch=on@bid=4&pid=1&chid=1" },
                new RuleItem { Keywords = "TDMA接收关 接收关", Command = "set-if-rx-switch:switch=off@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "SCPC接收关", Command = "set-if-rx-switch:switch=off@bid=4&pid=1&chid=1" },
                new RuleItem { Keywords = "TDMA路由", Command = "ipns route -n" },
                new RuleItem { Keywords = "SCPC路由", Command = "ipns3 route -n" },
                new RuleItem { Keywords = "锁定状态 锁定", Command = "get-if-rx-lockstate:@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "发射功率 TX功率", Command = "get-if-tx-curpwr:@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "接收功率 RX功率", Command = "get-if-rx-curpwr:@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "EsN0 EbN0 信噪比", Command = "get-if-rx-curesn0:@bid=3&pid=1&chid=1" },
                new RuleItem { Keywords = "LNB馈电 LNB电", Command = "get-lnb-power:@bid=3" },
                new RuleItem { Keywords = "LNB馈钟 LNB时钟 LNB钟", Command = "get-lnb-refclk:@bid=3" },
                new RuleItem { Keywords = "LNB馈电关 关LNB馈电", Command = "get-lnb-power:state=off@bid=3" },
                new RuleItem { Keywords = "LNB馈电开 开LNB馈电", Command = "get-lnb-power:state=on@bid=3" },
                new RuleItem { Keywords = "LNB馈钟关 关LNB馈钟 关LNB时钟 LNB时钟关", Command = "get-lnb-refclk:state=off@bid=3" },
                new RuleItem { Keywords = "LNB馈钟开 开LNB馈钟 开LNB时钟 LNB时钟开", Command = "get-lnb-refclk:state=on@bid=3" },
                new RuleItem { Keywords = "BUC馈电 BUC电", Command = "get-buc-power:@bid=3" },
                new RuleItem { Keywords = "BUC馈钟 BUC时钟 BUC钟", Command = "get-buc-refclk:@bid=3" },
                new RuleItem { Keywords = "BUC馈电关 关BUC馈电", Command = "get-buc-power:state=off@bid=3" },
                new RuleItem { Keywords = "BUC馈电开 开BUC馈电", Command = "get-buc-power:state=on@bid=3" },
                new RuleItem { Keywords = "BUC馈钟关 关BUC馈钟 关BUC时钟 BUC时钟关", Command = "get-buc-refclk:state=off@bid=3" },
                new RuleItem { Keywords = "BUC馈钟开 开BUC馈钟 开BUC时钟 BUC时钟开", Command = "get-buc-refclk:state=on@bid=3" },
                new RuleItem { Keywords = "告警 当前告警 当前警告", Command = "get-alm-cur" },
                new RuleItem { Keywords = "历史告警 历史警告", Command = "get-alm-his" },
                new RuleItem { Keywords = "CRC错包 CRC错误", Command = "get-fpga-reg:id=0,addr=0x6607" },
                new RuleItem { Keywords = "时隙过期", Command = "get-fpga-reg:id=0,addr=0x307" },
                new RuleItem { Keywords = "错包计数", Command = "get-fpga-reg:id=0,addr=0x1829" },
                new RuleItem { Keywords = "电子罗盘 校准罗盘", Command = "calibre-az:1" }
            };
        }
    }
}