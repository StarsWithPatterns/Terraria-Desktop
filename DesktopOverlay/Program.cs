namespace DesktopOverlay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\TerrariaDesktopOverlay", out bool firstInstance);
        if (!firstInstance)
            return; // Already running; look for the icon in the system tray.

        var settings = Settings.Load();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out int port) && port is > 0 and < 65536)
                settings.Port = port;
        }
        settings.Scale = Math.Clamp(settings.Scale, 1, 4);

        // Physical pixels on every monitor, so the character lines up exactly with the taskbar.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm(settings));
    }
}
