using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using backgroundControl.Tools;
using Microsoft.Terminal.Wpf;

namespace backgroundControl.Views;

public partial class TelnetControl : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Thread? _readerThread;
    private volatile bool _isRunning;
    private TelnetTerminalConnection? _terminalConnection;

    public TelnetControl()
    {
        InitializeComponent();
        Loaded += (_, _) => backgroundControl.Tools.TerminalFontZoom.Instance.Register(TerminalControl, Dispatcher);
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

    public async void ConnectWithCredentials(string host, int port, string username, string password)
    {
        HostTextBox.Text = host;
        PortTextBox.Text = port.ToString();
        UserTextBox.Text = username;
        PasswordBox.Password = password;
        await DoConnectAsync(host, port, username, password);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var host = HostTextBox.Text.Trim();
        if (!int.TryParse(PortTextBox.Text, out var port)) port = 23;
        var username = UserTextBox.Text.Trim();
        var password = PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password : PasswordVisibleBox.Text;
        await DoConnectAsync(host, port, username, password);
    }

    private async Task DoConnectAsync(string host, int port, string username, string password)
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(host, port);
            _stream = _tcpClient.GetStream();

            _terminalConnection = new TelnetTerminalConnection(this);
            TerminalControl.Connection = new HighlightTerminalConnection(_terminalConnection);

            _isRunning = true;
            _readerThread = new Thread(ReaderLoop) { IsBackground = true };
            _readerThread.Start();

            if (!string.IsNullOrEmpty(username))
                await AutoLoginAsync(username, password);

            SshHistoryManager.RecordConnection(host, port, username, password, "Telnet");

            var tab = DataContext as TabItemViewModel;
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "✅ 已连接";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
                if (tab != null)
                    tab.Header = $"Telnet - {host}:{port}";
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"连接失败: {ex.Message}");
        }
    }

    private async Task AutoLoginAsync(string username, string password)
    {
        await Task.Delay(500);
        var loginBytes = Encoding.ASCII.GetBytes($"{username}\r\n");
        await _stream!.WriteAsync(loginBytes, 0, loginBytes.Length);
        await Task.Delay(300);
        var passBytes = Encoding.ASCII.GetBytes($"{password}\r\n");
        await _stream!.WriteAsync(passBytes, 0, passBytes.Length);
    }

    private void ReaderLoop()
    {
        var buffer = new byte[4096];
        var output = new StringBuilder();
        var negotiation = new List<byte>();

        while (_isRunning && _tcpClient!.Connected)
        {
            try
            {
                int bytesRead = _stream!.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                output.Clear();
                int i = 0;
                while (i < bytesRead)
                {
                    if (buffer[i] == 0xFF)
                    {
                        HandleTelnetNegotiation(buffer, ref i, bytesRead);
                    }
                    else
                    {
                        output.Append((char)buffer[i]);
                        i++;
                    }
                }

                if (output.Length > 0)
                    _terminalConnection?.Write(output.ToString());
            }
            catch
            {
                break;
            }
        }

        Dispatcher.Invoke(() => HandleDisconnected());
    }

    private void HandleTelnetNegotiation(byte[] buffer, ref int i, int count)
    {
        if (i + 2 >= count) { i++; return; }

        byte cmd = buffer[i + 1];
        byte option = buffer[i + 2];

        if (cmd == 250)
        {
            i += 3;
            while (i < count)
            {
                if (buffer[i] == 0xFF && i + 1 < count && buffer[i + 1] == 240)
                {
                    i += 2;
                    return;
                }
                i++;
            }
            return;
        }

        i += 3;

        if (cmd == 251)
            _stream?.Write(new byte[] { 0xFF, 252, option }, 0, 3);
        else if (cmd == 253)
            _stream?.Write(new byte[] { 0xFF, 254, option }, 0, 3);
    }

    private void TerminalControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_stream == null || !_isRunning) return;

        e.Handled = true;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool caps = Keyboard.IsKeyToggled(Key.CapsLock);

        switch (e.Key)
        {
            case Key.Escape:      Send("\x1b"); break;
            case Key.Tab:         Send("\t"); break;
            case Key.Enter:       Send("\r\n"); break;
            case Key.Space:       Send(" "); break;
            case Key.Back:        Send("\b"); break;
            case Key.Up:          Send("\x1b[A"); break;
            case Key.Down:        Send("\x1b[B"); break;
            case Key.Left:        Send("\x1b[D"); break;
            case Key.Right:       Send("\x1b[C"); break;
            default:
                char? c = GetCharFromKey(e.Key, shift, caps);
                if (c.HasValue) Send(c.Value.ToString());
                break;
        }
    }

    private void Send(string data)
    {
        try
        {
            var bytes = Encoding.ASCII.GetBytes(data);
            _stream?.Write(bytes, 0, bytes.Length);
        }
        catch { }
    }

    private char? GetCharFromKey(Key key, bool shift, bool caps)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            char baseCh = (char)('a' + (key - Key.A));
            return (shift ^ caps) ? char.ToUpper(baseCh) : baseCh;
        }

        return (key) switch
        {
            Key.D0 => shift ? ')' : '0', Key.D1 => shift ? '!' : '1',
            Key.D2 => shift ? '@' : '2', Key.D3 => shift ? '#' : '3',
            Key.D4 => shift ? '$' : '4', Key.D5 => shift ? '%' : '5',
            Key.D6 => shift ? '^' : '6', Key.D7 => shift ? '&' : '7',
            Key.D8 => shift ? '*' : '8', Key.D9 => shift ? '(' : '9',
            Key.OemTilde => shift ? '~' : '`',
            Key.OemMinus => shift ? '_' : '-',
            Key.OemPlus => shift ? '+' : '=',
            Key.OemOpenBrackets => shift ? '{' : '[',
            Key.OemCloseBrackets => shift ? '}' : ']',
            Key.OemBackslash => shift ? '|' : '\\',
            Key.OemSemicolon => shift ? ':' : ';',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemComma => shift ? '<' : ',',
            Key.OemPeriod => shift ? '>' : '.',
            Key.OemQuestion => shift ? '?' : '/',
            _ => null
        };
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        CloseConnection();
    }

    private void CloseConnection()
    {
        _isRunning = false;
        try { _stream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _readerThread?.Join(1000);

        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "❌ 已断开";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        });
    }

    private void HandleDisconnected()
    {
        _isRunning = false;
        StatusText.Text = "❌ 连接断开";
        StatusText.Foreground = System.Windows.Media.Brushes.Red;
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
    }

    public void Dispose()
    {
        CloseConnection();
    }

    public class TelnetTerminalConnection : ITerminalConnection
    {
        private readonly TelnetControl _owner;
        public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

        public TelnetTerminalConnection(TelnetControl owner)
        {
            _owner = owner;
        }

        public void Write(string text)
        {
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
        }

        public void WriteInput(string data)
        {
            _owner.Send(data);
        }

        public void Start() { }
        public void Close() { }
        public void Resize(uint rows, uint columns) { }
    }
}
