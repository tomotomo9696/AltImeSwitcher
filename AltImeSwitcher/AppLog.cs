namespace AltImeSwitcher;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AltImeSwitcher",
        "AltImeSwitcher.log");

    public static void Write(string message)
    {
        // Never block the low-level hook on disk I/O or propagate a logging failure.
        ThreadPool.QueueUserWorkItem(_ => WriteDirect(message));
    }

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void WriteDirect(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_048_576)
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
