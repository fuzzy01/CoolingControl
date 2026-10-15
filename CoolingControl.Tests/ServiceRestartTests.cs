using System.Text;
using CoolingControl.Tray;
using Xunit;

namespace CoolingControl.Tests;

public class ServiceRestartTests
{
    [Fact]
    public void Arguments_WaitUntilStoppedThenStart()
    {
        Assert.Equal("Restart service", ServiceRestart.MenuText);
        Assert.Equal("Restarting service…", ServiceRestart.RestartingText);
        Assert.DoesNotContain("&", ServiceRestart.Arguments);
        Assert.EndsWith(
            Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
            ServiceRestart.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Environment.SystemDirectory, ServiceRestart.FileName, StringComparison.OrdinalIgnoreCase);

        var encoded = ServiceRestart.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Equal(ServiceRestart.Script, script);
        Assert.Contains("Stop-Service -Name $name -Force", script);
        Assert.Contains("if ($svc.Status -eq 'Stopped') { break }", script);
        Assert.Contains("Start-Service -Name $name", script);
        Assert.Contains("$name = 'CoolingControl'", script);
    }

    [Fact]
    public void CreateRestart_UsesItsOwnTag()
    {
        var toast = AlertToast.CreateRestart(ServiceRestart.FailureMessage);

        Assert.Equal(AlertToast.RestartTag, toast.Tag);
        Assert.Equal("cc-restart", toast.Tag);
        Assert.Equal(AlertToast.Group, toast.Group);
        Assert.Equal("CoolingControl", toast.Group);
        Assert.NotEqual(AlertToast.Tag, toast.Tag);
        Assert.NotEqual(AlertToast.UpdateTag, toast.Tag);
    }
}
