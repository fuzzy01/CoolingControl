using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CoolingControl.Tray;

internal static class AlertToast
{
    public const string AppUserModelId = "CoolingControl.Tray";
    public const string Tag = "cc-alert";
    public const string UpdateTag = "cc-update";
    public const string RestartTag = "cc-restart";
    public const string Group = "CoolingControl";

    public static void Show(string message)
    {
        ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(Create(message));
    }

    public static void ShowUpdate(string message)
    {
        ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(CreateUpdate(message));
    }

    public static void ShowRestart(string message)
    {
        ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(CreateRestart(message));
    }

    internal static ToastNotification Create(string message) => Create(message, Tag);

    internal static ToastNotification CreateUpdate(string message) => Create(message, UpdateTag);

    internal static ToastNotification CreateRestart(string message) => Create(message, RestartTag);

    private static ToastNotification Create(string message, string tag)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\">" +
            "<text>CoolingControl</text><text>" + SecurityElement.Escape(message) + "</text>" +
            "</binding></visual></toast>");
        return new ToastNotification(xml)
        {
            Tag = tag,
            Group = Group
        };
    }
}
