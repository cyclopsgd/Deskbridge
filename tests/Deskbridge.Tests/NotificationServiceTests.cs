using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public void Show_PublishesNotificationEvent()
    {
        var bus = new EventBus();
        var service = new NotificationService(bus);
        var recipient = new object();
        NotificationEvent? received = null;

        bus.Subscribe<NotificationEvent>(recipient, e => received = e);

        service.Show("Test Title", "Test message", NotificationLevel.Info);

        received.Should().NotBeNull();
        received!.Title.Should().Be("Test Title");
        received.Message.Should().Be("Test message");
        received.Level.Should().Be(NotificationLevel.Info);

        bus.Unsubscribe<NotificationEvent>(recipient);
    }

    [Fact]
    public void Show_AddsToRecentHistory()
    {
        var bus = new EventBus();
        var service = new NotificationService(bus);

        service.Show("Title", "Message", NotificationLevel.Warning);

        service.Recent.Should().ContainSingle();
        service.Recent[0].Title.Should().Be("Title");
        service.Recent[0].Message.Should().Be("Message");
        service.Recent[0].Level.Should().Be(NotificationLevel.Warning);
    }

    [Fact]
    public void Show_RaisesNotificationRaisedEvent()
    {
        var bus = new EventBus();
        var service = new NotificationService(bus);
        Notification? raised = null;

        service.NotificationRaised += (_, n) => raised = n;

        service.Show("Event Title", "Event message", NotificationLevel.Success);

        raised.Should().NotBeNull();
        raised!.Title.Should().Be("Event Title");
        raised.Level.Should().Be(NotificationLevel.Success);
    }

    [Fact]
    public void ShowError_SetsErrorLevel()
    {
        var bus = new EventBus();
        var service = new NotificationService(bus);
        var exception = new InvalidOperationException("Something broke");

        service.ShowError("Error Title", "Details", exception);

        service.Recent.Should().ContainSingle();
        service.Recent[0].Level.Should().Be(NotificationLevel.Error);
        service.Recent[0].Message.Should().Contain("Something broke");
    }

    [Fact]
    public void RecentHistory_LimitedTo50()
    {
        var bus = new EventBus();
        var service = new NotificationService(bus);

        for (int i = 0; i < 51; i++)
        {
            service.Show($"Title {i}", $"Message {i}");
        }

        service.Recent.Should().HaveCount(50);
        // First notification should have been evicted (FIFO)
        service.Recent.Should().NotContain(n => n.Title == "Title 0");
        service.Recent[0].Title.Should().Be("Title 1");
    }
}
