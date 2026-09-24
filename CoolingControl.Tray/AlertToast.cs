using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CoolingControl.Tray;

internal static class AlertToast
{
    public const string AppUserModelId = "CoolingControl.Tray";

    public static void Show(string message)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\">" +
            "<text>CoolingControl</text><text>" + SecurityElement.Escape(message) + "</text>" +
            "</binding></visual></toast>");
        ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(new ToastNotification(xml));
    }
}
