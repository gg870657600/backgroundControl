// 启动 FTP 服务，然后用 raw socket 模拟客户端测：USER/PASS/PWD/LIST/RETR/STOR/DELE
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using backgroundControl.Tools;

class FtpSmokeTest
{
    static async Task<int> Main()
    {
        // 1. 准备测试目录 + 文件
        var testRoot = Path.Combine(Path.GetTempPath(), "FtpTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(testRoot);
        File.WriteAllText(Path.Combine(testRoot, "hello.txt"), "hello ftp world\n");
        Console.WriteLine($"测试目录: {testRoot}");

        // 2. 启动服务
        var cfg = new FtpConfig
        {
            Port          = 0,  // 系统分配
            RootDir       = testRoot,
            PassiveStart  = 51000,
            PassiveEnd    = 51010,
            AllowAnonymous = true,
        };
        var host = new FtpServerHost(cfg);
        host.OnLog += m => Console.WriteLine($"[server] {m}");

        // 找可用端口
        int port = FindFreePort();
        cfg.Port = port;
        host.Start();
        await Task.Delay(100);
        Console.WriteLine($"FTP 服务已启动: 端口 {port}");

        // 3. 测试用例
        int pass = 0, fail = 0;
        async Task Run(string name, Func<Task<bool>> test)
        {
            try
            {
                if (await test()) { Console.WriteLine($"  ✓ {name}"); pass++; }
                else              { Console.WriteLine($"  ✗ {name}"); fail++; }
            }
            catch (Exception ex) { Console.WriteLine($"  ✗ {name} (异常: {ex.Message})"); fail++; }
        }

        // ============= 测试 =============
        using var conn = new TcpClient();
        await conn.ConnectAsync("127.0.0.1", port);
        var ns = conn.GetStream();
        var reader = new StreamReader(ns, Encoding.ASCII);
        var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true };

        async Task<string> ReadResp() => (await reader.ReadLineAsync()) ?? "";

        // 读取欢迎语
        var welcome = await ReadResp();
        Console.WriteLine($"[client] 收到: {welcome}");

        await Run("USER anonymous", async () =>
        {
            await writer.WriteLineAsync("USER anonymous");
            var r = await ReadResp();
            return r.StartsWith("230");
        });

        await Run("PWD", async () =>
        {
            await writer.WriteLineAsync("PWD");
            var r = await ReadResp();
            return r.StartsWith("257") && r.Contains("/");
        });

        await Run("TYPE I", async () =>
        {
            await writer.WriteLineAsync("TYPE I");
            var r = await ReadResp();
            return r.StartsWith("200");
        });

        await Run("SIZE hello.txt", async () =>
        {
            await writer.WriteLineAsync("SIZE hello.txt");
            var r = await ReadResp();
            return r.StartsWith("213");
        });

        await Run("RETR hello.txt (via PASV)", async () =>
        {
            // 1. PASV → 拿到数据端口
            await writer.WriteLineAsync("PASV");
            var p = await ReadResp();
            var match = System.Text.RegularExpressions.Regex.Match(p, @"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)");
            if (!match.Success) { Console.WriteLine($"    PASV parse fail: {p}"); return false; }
            int dataPort = int.Parse(match.Groups[5].Value) * 256 + int.Parse(match.Groups[6].Value);

            // 2. 先连数据端口（必须在 RETR 之前）
            var dc = new TcpClient();
            await dc.ConnectAsync("127.0.0.1", dataPort);
            var dns = dc.GetStream();

            // 3. 发 RETR
            await writer.WriteLineAsync("RETR hello.txt");
            var r1 = await ReadResp();
            Console.WriteLine($"    [r1] {r1}");
            if (!r1.StartsWith("150")) { dc.Close(); return false; }

            var buf = new byte[1024];
            var ms = new MemoryStream();
            int n;
            while ((n = await dns.ReadAsync(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
            var content = Encoding.UTF8.GetString(ms.ToArray());
            Console.WriteLine($"    [data] {content.Length} bytes: {content.Replace("\n","\\n")}");
            dc.Close();
            var r2 = await ReadResp();
            Console.WriteLine($"    [r2] {r2}");
            return r2.StartsWith("226") && content.Contains("hello ftp world");
        });

        await Run("STOR uploaded.txt (via PASV)", async () =>
        {
            await writer.WriteLineAsync("PASV");
            var p = await ReadResp();
            var match = System.Text.RegularExpressions.Regex.Match(p, @"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)");
            if (!match.Success) return false;
            int dataPort = int.Parse(match.Groups[5].Value) * 256 + int.Parse(match.Groups[6].Value);

            var dc = new TcpClient();
            await dc.ConnectAsync("127.0.0.1", dataPort);
            var dns = dc.GetStream();

            await writer.WriteLineAsync("STOR uploaded.txt");
            var r1 = await ReadResp();
            if (!r1.StartsWith("150")) { dc.Close(); return false; }

            var bytes = Encoding.UTF8.GetBytes("uploaded content\n");
            await dns.WriteAsync(bytes);
            dc.Close();
            var r2 = await ReadResp();
            var path = Path.Combine(testRoot, "uploaded.txt");
            return r2.StartsWith("226") && File.Exists(path) && File.ReadAllText(path).Contains("uploaded content");
        });

        await Run("LIST", async () =>
        {
            await writer.WriteLineAsync("PASV");
            var p = await ReadResp();
            var match = System.Text.RegularExpressions.Regex.Match(p, @"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)");
            if (!match.Success) return false;
            int dataPort = int.Parse(match.Groups[5].Value) * 256 + int.Parse(match.Groups[6].Value);

            var dc = new TcpClient();
            await dc.ConnectAsync("127.0.0.1", dataPort);
            var dns = dc.GetStream();

            await writer.WriteLineAsync("LIST");
            var r1 = await ReadResp();
            if (!r1.StartsWith("150")) { dc.Close(); return false; }

            using var sr = new StreamReader(dns, Encoding.UTF8);
            var listing = await sr.ReadToEndAsync();
            dc.Close();
            var r2 = await ReadResp();
            return r2.StartsWith("226") && listing.Contains("hello.txt") && listing.Contains("uploaded.txt");
        });

        await Run("DELE uploaded.txt", async () =>
        {
            await writer.WriteLineAsync("DELE uploaded.txt");
            var r = await ReadResp();
            return r.StartsWith("250") && !File.Exists(Path.Combine(testRoot, "uploaded.txt"));
        });

        await Run("QUIT", async () =>
        {
            await writer.WriteLineAsync("QUIT");
            var r = await ReadResp();
            return r.StartsWith("221");
        });

        // 4. 收尾
        host.Stop();
        host.Dispose();
        try { Directory.Delete(testRoot, recursive: true); } catch { }

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }

    static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
