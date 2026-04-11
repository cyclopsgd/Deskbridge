namespace Deskbridge.Core.Interfaces;

public enum NotificationLevel { Info, Success, Warning, Error }

public record Notification(string Title, string Message, NotificationLevel Level, DateTime Timestamp);

public interface INotificationService
{
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null);
    void ShowError(string title, string message, Exception? exception = null);
    IReadOnlyList<Notification> Recent { get; }
    event EventHandler<Notification> NotificationRaised;
}
