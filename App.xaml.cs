using System.Configuration;
using System.Data;
using System.Windows;

namespace backgroundControl
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DispatcherUnhandledException += (_, e) =>
            {
                var log = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(log,
                    $"[{DateTime.Now:HH:mm:ss}] {e.Exception}{Environment.NewLine}");
                e.Handled = true;
            };
    }

}

}
