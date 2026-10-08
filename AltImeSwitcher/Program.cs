namespace AltImeSwitcher;

static class Program
{
    private const string MutexName = @"Local\AltImeSwitcher";

    [STAThread]
    static void Main()
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "AltImeSwitcher is already running.",
                    "AltImeSwitcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            AppLog.Write($"Starting AltImeSwitcher {typeof(Program).Assembly.GetName().Version}.");
            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            AppLog.WriteDirect(ex.ToString());
            MessageBox.Show(
                $"AltImeSwitcher encountered an error.\n\n{ex.Message}\n\nLog:\n{AppLog.LogPath}",
                "AltImeSwitcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
