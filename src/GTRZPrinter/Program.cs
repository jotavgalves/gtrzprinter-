using System.Text;

namespace GTRZPrinter;

internal static class Program
{
    private static Mutex _mutex;
    private static EventWaitHandle _showEvent;

    [STAThread]
    private static void Main()
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
            config.Version = "2.0.0";
            config.Save();

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
}
