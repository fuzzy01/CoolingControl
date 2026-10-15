using System.Text;

namespace CoolingControl.Tray;

internal static class ServiceRestart
{
    public const string ServiceName = "CoolingControl";
    public const string MenuText = "Restart service";
    public const string RestartingText = "Restarting service…";
    public const string FailureMessage = "Could not restart the CoolingControl service.";

    public const string Script =
        """
        $name = 'CoolingControl'
        $log = Join-Path $env:TEMP 'CoolingControl\restart-service.log'
        try {
            $svc = Get-Service -Name $name
            if ($svc.Status -ne 'Stopped') {
                try { Stop-Service -Name $name -Force -ErrorAction Stop } catch { }
                $deadline = (Get-Date).AddSeconds(40)
                do {
                    $svc.Refresh()
                    if ($svc.Status -eq 'Stopped') { break }
                    Start-Sleep -Seconds 1
                } while ((Get-Date) -lt $deadline)
            }
            Start-Service -Name $name
        } catch {
            New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
            "$(Get-Date -Format o) $($_.Exception.Message)" | Out-File -FilePath $log -Append
            exit 1
        }
        """;

    public static string FileName =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string Arguments =>
        "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
}
