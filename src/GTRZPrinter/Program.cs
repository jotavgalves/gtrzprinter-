using System.Text;

namespace GTRZPrinter;

internal static class Program
{
    private static Mutex _mutex;
    private static EventWaitHandle _showEvent;

    [STAThread]
    private static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();

        const string mutexName = @"Global\GTRZ_Printer_2";
        const string showEventName = @"Global\GTRZ_Printer_Show_2";

        bool created;
        _mutex = new Mutex(true, mutexName, out created);
        if (!created)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(showEventName);
                existing.Set();
            }
            catch { }
            return;
        }

        bool eventCreated;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName, out eventCreated);

        try
        {
            var config = AppConfig.Load();
            ApplyCommandLine(args, config);
            config.Version = "2.0.1";
            config.Save();

            if (HasArg(args, "--provision-client") && config.Mode == "client")
                ProvisionClient(config);

            using var form = new MainForm(config);
            var showThread = new Thread(() =>
            {
                while (!form.IsDisposed)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        if (form.IsDisposed) break;
                        form.BeginInvoke(new Action(form.RestoreFromTray));
                    }
                    catch { break; }
                }
            }) { IsBackground = true, Name = "GTRZ.ShowExisting" };
            showThread.Start();

            Application.Run(form);
        }
        catch (Exception ex)
        {
            try { Log.Error("FATAL " + ex); } catch { }
            MessageBox.Show(
                "O GTRZ Printer encontrou um erro fatal.\r\n\r\n" + ex.Message +
                "\r\n\r\nLog: " + Log.FilePath,
                "GTRZ Printer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try { _showEvent?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
        }
    }

    private static void ApplyCommandLine(string[] args, AppConfig config)
    {
        if (HasArg(args, "--server")) config.Mode = "server";
        if (HasArg(args, "--client")) config.Mode = "client";
        if (HasArg(args, "--auto")) config.Mode = "auto";

        foreach (var arg in args ?? Array.Empty<string>())
        {
            if (arg.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                var mode = arg.Substring("--mode=".Length).Trim().ToLowerInvariant();
                if (mode is "server" or "client" or "auto") config.Mode = mode;
            }
            else if (arg.StartsWith("--server-address=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--server-address=".Length).Trim();
                if (!string.IsNullOrWhiteSpace(value)) config.ServerAddress = value;
            }
        }
    }

    private static bool HasArg(string[] args, string value) =>
        (args ?? Array.Empty<string>()).Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    private static void ProvisionClient(AppConfig config)
    {
        try
        {
            var found = DiscoveryService.Discover(config.DiscoveryPort, 2500);
            if (!string.IsNullOrWhiteSpace(found) && found != AppConfig.LocalIp())
            {
                config.ServerAddress = found;
                config.Save();
                Log.Info("CLI: servidor descoberto em " + found);
            }

            if (config.ServerAddress == AppConfig.LocalIp())
            {
                Log.Warn("CLI: endereço do servidor aponta para o próprio cliente; provisionamento IPP adiado.");
                return;
            }

            if (!Proc.TcpOpen(config.ServerAddress, config.IppPort, 2500, out var error))
            {
                Log.Warn("CLI: servidor IPP ainda não respondeu em " + config.ServerAddress + ":" + config.IppPort + " - " + error);
                return;
            }

            if (!Proc.PrinterInstalled(config))
            {
                Proc.InstallIpp(config);
                Log.Info("CLI: GTRZ POS-80 instalada no cliente.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("CLI: provisionamento do cliente não concluído: " + ex.Message);
        }
    }
}
