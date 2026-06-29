using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Terminal.Wpf;
using System.Windows.Input;
using System.Windows.Interop;
using System.Linq;
using System.Windows.Media;
using backgroundControl.Tools;
using System.Text;
using System.Text.RegularExpressions;

namespace backgroundControl
{
    public partial class SerialPortControl : System.Windows.Controls.UserControl, IDisposable
    {
        private SerialPort? _serialPort;
        private bool _disposed;
        private SerialTerminalConnection? _terminalConnection;
        public string TabHeader { get; set; }
        private readonly object _logLock = new();
        private StringBuilder _allTerminalOutput = new StringBuilder();
        private StringBuilder _cleanOutput = new StringBuilder();

        private const int WM_DEVICECHANGE = 0x0219;

        public SerialPortControl()
        {
            InitializeComponent();
            InitSerialOption();
            RefreshPortList();
            Loaded += (_, _) =>
            {
                backgroundControl.Tools.TerminalFontZoom.Instance.Register(TerminalControl, Dispatcher);
                if (PresentationSource.FromVisual(this) is HwndSource hwnd)
                    hwnd.AddHook(WndProc);
            };
        }

        private void RefreshPortList()
        {
            var ports = SerialPort.GetPortNames();
            string? previous = Cmb_PortName.SelectedItem?.ToString();
            Cmb_PortName.Items.Clear();
            foreach (var p in ports) Cmb_PortName.Items.Add(p);
            if (previous != null && ports.Contains(previous))
                Cmb_PortName.SelectedItem = previous;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
                Dispatcher.InvokeAsync(RefreshPortList);
            return IntPtr.Zero;
        }

        // 所有用户输入通过此方法发送到串口，不再本地回显
        private void SendToSerial(string data)
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Write(data);
        }

        private void TerminalControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return;

            e.Handled = true;

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool caps = Keyboard.IsKeyToggled(Key.CapsLock);

            // 功能键处理
            switch (e.Key)
            {
                case Key.Escape:
                    SendToSerial("\x1b");
                    break;
                case Key.Tab:
                    SendToSerial("\t");
                    break;
                case Key.Up:
                    SendToSerial("\x1b[A");
                    break;
                case Key.Down:
                    SendToSerial("\x1b[B");
                    break;
                case Key.Left:
                    SendToSerial("\x1b[D");
                    break;
                case Key.Right:
                    SendToSerial("\x1b[C");
                    break;
                case Key.Back:
                    SendToSerial("\b");
                    break;
                case Key.Enter:
                    SendToSerial("\r\n");
                    break;
                case Key.Space:
                    SendToSerial(" ");
                    break;
                default:
                    {
                        char? c = GetCharFromKey(e.Key, shift, caps);
                        if (c.HasValue)
                            SendToSerial(c.Value.ToString());
                    }
                    break;
            }
        }

        private char? GetCharFromKey(Key key, bool shift, bool caps)
        {
            // 字母
            if (key >= Key.A && key <= Key.Z)
            {
                char baseCh = (char)('a' + (key - Key.A));
                bool upper = shift ^ caps;
                return upper ? char.ToUpper(baseCh) : baseCh;
            }

            // 数字及符号（略，请保留原代码）
            switch (key)
            {
                case Key.D0: return shift ? ')' : '0';
                case Key.D1: return shift ? '!' : '1';
                case Key.D2: return shift ? '@' : '2';
                case Key.D3: return shift ? '#' : '3';
                case Key.D4: return shift ? '$' : '4';
                case Key.D5: return shift ? '%' : '5';
                case Key.D6: return shift ? '^' : '6';
                case Key.D7: return shift ? '&' : '7';
                case Key.D8: return shift ? '*' : '8';
                case Key.D9: return shift ? '(' : '9';
                case Key.NumPad0: return '0';
                case Key.NumPad1: return '1';
                case Key.NumPad2: return '2';
                case Key.NumPad3: return '3';
                case Key.NumPad4: return '4';
                case Key.NumPad5: return '5';
                case Key.NumPad6: return '6';
                case Key.NumPad7: return '7';
                case Key.NumPad8: return '8';
                case Key.NumPad9: return '9';
                case Key.OemComma: return shift ? '<' : ',';
                case Key.OemPeriod: return shift ? '>' : '.';
                case Key.OemSemicolon: return shift ? ':' : ';';
                case Key.OemQuotes: return shift ? '"' : '\'';
                case Key.OemOpenBrackets: return shift ? '{' : '[';
                case Key.OemCloseBrackets: return shift ? '}' : ']';
                case Key.OemMinus: return shift ? '_' : '-';
                case Key.OemPlus: return shift ? '+' : '=';
                case Key.OemBackslash: return shift ? '|' : '\\';
                case Key.OemQuestion: return shift ? '?' : '/';
                default: return null;
            }
        }

        private void InitSerialOption()
        {
            Cmb_BaudRate.Items.Add(4800); Cmb_BaudRate.Items.Add(9600); Cmb_BaudRate.Items.Add(19200); Cmb_BaudRate.Items.Add(38400); Cmb_BaudRate.Items.Add(57600); Cmb_BaudRate.Items.Add(115200);
            Cmb_BaudRate.SelectedItem = 115200;
            Cmb_DataBits.Items.Add(5); Cmb_DataBits.Items.Add(6); Cmb_DataBits.Items.Add(7); Cmb_DataBits.Items.Add(8); Cmb_DataBits.SelectedItem = 8;
            Cmb_StopBits.Items.Add(StopBits.One); Cmb_StopBits.Items.Add(StopBits.OnePointFive); Cmb_StopBits.Items.Add(StopBits.Two); Cmb_StopBits.SelectedItem = StopBits.One;
            Cmb_Parity.Items.Add(Parity.None); Cmb_Parity.Items.Add(Parity.Odd); Cmb_Parity.Items.Add(Parity.Even); Cmb_Parity.Items.Add(Parity.Mark); Cmb_Parity.Items.Add(Parity.Space);
            Cmb_Parity.SelectedItem = Parity.None;
        }

        private void Btn_Open_Click(object sender, RoutedEventArgs e)
        {
            if (Cmb_PortName.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("请选择串口号");
                return;
            }

            try
            {
                string portName = Cmb_PortName.SelectedItem.ToString();
                int baud = int.Parse(Cmb_BaudRate.SelectedItem.ToString());
                Parity parity = (Parity)Cmb_Parity.SelectedItem;
                int dataBits = int.Parse(Cmb_DataBits.SelectedItem.ToString());
                StopBits stopBits = (StopBits)Cmb_StopBits.SelectedItem;

                _serialPort = new SerialPort(portName, baud, parity, dataBits, stopBits);
                _serialPort.NewLine = "\r\n";
                _serialPort.Handshake = Handshake.None;
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                // 创建终端连接（仅用于显示）
                _terminalConnection = new SerialTerminalConnection(_serialPort);
                var proxy = new HighlightTerminalConnection(_terminalConnection);
                proxy.OnRawOutput = text => { lock (_logLock) {
                    _allTerminalOutput.Append(text);
                    _cleanOutput.Append(AnsiStripper.Strip(text));
                } };
                TerminalControl.Connection = proxy;

                // 发送初始换行唤醒设备
                _serialPort.Write("\r\n");

                Txt_Status.Text = "✅ 串口已打开";
                Txt_Status.Foreground = System.Windows.Media.Brushes.Green;
                Btn_Open.IsEnabled = false;
                Btn_Close.IsEnabled = true;
                // 自动改标题
                Dispatcher.Invoke(() =>
                {
                    TabHeader = $"串口 {portName}";
                    if (DataContext is TabItemViewModel tabItem)
                    {
                        tabItem.Header = TabHeader;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开失败：{ex.Message}");
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _serialPort.ReadExisting();
                Dispatcher.Invoke(() =>
                {
                    // 仅显示接收到的数据，不再进行其他处理
                    _terminalConnection?.Write(data);
                });
            }
            catch { }
        }

        private void Btn_Close_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string processed;
                lock (_logLock)
                    processed = _cleanOutput.ToString().Replace("\n", Environment.NewLine);

                if (string.IsNullOrWhiteSpace(processed))
                {
                    System.Windows.MessageBox.Show("暂无日志可复制", "提示");
                    return;
                }

                System.Windows.Clipboard.SetText(processed, System.Windows.TextDataFormat.UnicodeText);
                System.Windows.MessageBox.Show($"✅ 已复制 {processed.Length} 字符串口日志！", "成功");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "错误");
            }
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }

                Dispatcher.Invoke(() =>
                {
                    Txt_Status.Text = "❌ 串口已关闭";
                    Txt_Status.Foreground = System.Windows.Media.Brushes.Red;
                    Btn_Open.IsEnabled = true;
                    Btn_Close.IsEnabled = false;
                    TerminalControl.Connection = null;
                });
            }
            catch { }
        }

        public void Dispose()
        {
            CloseSerialPort();
        }
    }

    public class SerialTerminalConnection : ITerminalConnection
    {
        private readonly SerialPort _serialPort;
        public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

        public SerialTerminalConnection(SerialPort serialPort)
        {
            _serialPort = serialPort;
        }

        // 用于显示从串口接收到的数据
        public void Write(string text) => TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));

        // 用于将用户输入发送到串口（不触发显示）
        public void WriteInput(string data)
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Write(data);
        }

        public void Start() { }
        public void Close() { }
        public void Resize(uint rows, uint columns) { }
    }
}