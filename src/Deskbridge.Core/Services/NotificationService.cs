using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IEventBus _eventBus;
    private readonly List<Notification> _recent = [];
    private readonly object _lock = new();

    public NotificationService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public IReadOnlyList<Notification> Recent
    {
        get { lock (_lock) { return _recent.ToList(); } }
    }

    public event EventHandler<Notification>? NotificationRaised;

    public void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null)
    {
        var notification = new Notification(title, message, level, DateTime.UtcNow);
        AddRecent(notification);
        NotificationRaised?.Invoke(this, notification);
        _eventBus.Publish(new NotificationEvent(title, message, level));
    }

    public void ShowError(string title, string message, Exception? exception = null)
    {
        var detail = exception is not null ? $"{message}: {exception.Message}" : message;
        Show(title, detail, NotificationLevel.Error);
    }

    private void AddRecent(Notification notification)
    {
        lock (_lock)
        {
            _recent.Add(notification);
            if (_recent.Count > 50)
                _recent.RemoveAt(0);
        }
    }
}
