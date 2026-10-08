namespace AltImeSwitcher;

internal static class ApplicationIcon
{
    public static Icon Load(Size? size = null)
    {
        using var stream = typeof(ApplicationIcon).Assembly.GetManifestResourceStream("AltImeSwitcher.AppIcon")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var icon = new Icon(stream, size ?? SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
