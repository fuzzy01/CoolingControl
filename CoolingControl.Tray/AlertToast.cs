using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CoolingControl.Tray;

internal static class AlertToast
{
    public const string AppUserModelId = "CoolingControl.Tray";
    public const string Tag = "cc-alert";
    public const string Group = "CoolingControl";

    public static void Show(string message)
    {
        ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(Create(message));
    }

    internal static ToastNotification Create(string message)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\">" +
            "<text>CoolingControl</text><text>" + SecurityElement.Escape(message) + "</text>" +
            "</binding></visual></toast>");
        return new ToastNotification(xml)
        {
            Tag = Tag,
            Group = Group
        };
    }
}
