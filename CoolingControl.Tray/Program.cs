using System.Runtime.InteropServices;

namespace CoolingControl.Tray;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "CoolingControl.Tray", out var created);
        if (!created)
            return;

        SetCurrentProcessExplicitAppUserModelID(AlertToast.AppUserModelId);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new TrayApplicationContext());
    }
}
