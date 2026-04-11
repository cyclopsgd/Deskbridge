using Deskbridge.Core.Events;
using Deskbridge.Core.Models;
using Deskbridge.Core.Services;

namespace Deskbridge.Tests;

public sealed class EventBusTests
{
    [Fact]
    public void Publish_InvokesSubscribedHandler()
    {
        var bus = new EventBus();
        var recipient = new object();
        ConnectionRequestedEvent? received = null;

        bus.Subscribe<ConnectionRequestedEvent>(recipient, e => received = e);

        var connection = new ConnectionModel { Name = "test-server" };
        var evt = new ConnectionRequestedEvent(connection);
        bus.Publish(evt);

        received.Should().NotBeNull();
        received!.Connection.Name.Should().Be("test-server");

        bus.Unsubscribe<ConnectionRequestedEvent>(recipient);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var bus = new EventBus();
        var recipient = new object();
        int callCount = 0;

        bus.Subscribe<ConnectionRequestedEvent>(recipient, _ => callCount++);

        bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));
        callCount.Should().Be(1);

        bus.Unsubscribe<ConnectionRequestedEvent>(recipient);

        bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));
        callCount.Should().Be(1);
    }

    [Fact]
    public void MultipleSubscribers_AllReceive()
    {
        var bus = new EventBus();
        var recipient1 = new object();
        var recipient2 = new object();
        int count1 = 0;
        int count2 = 0;

        bus.Subscribe<ConnectionRequestedEvent>(recipient1, _ => count1++);
        bus.Subscribe<ConnectionRequestedEvent>(recipient2, _ => count2++);

        bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));

        count1.Should().Be(1);
        count2.Should().Be(1);

        bus.Unsubscribe<ConnectionRequestedEvent>(recipient1);
        bus.Unsubscribe<ConnectionRequestedEvent>(recipient2);
    }

    [Fact]
    public void WeakReference_AllowsGarbageCollection()
    {
        var bus = new EventBus();

        // Subscribe with an object that will go out of scope
        SubscribeInScope(bus);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Publishing after GC should not throw
        var act = () => bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));
        act.Should().NotThrow();
    }

    private static void SubscribeInScope(EventBus bus)
    {
        var recipient = new object();
        bus.Subscribe<ConnectionRequestedEvent>(recipient, _ => { });
        // recipient goes out of scope here
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var bus = new EventBus();

        var act = () => bus.Publish(new ConnectionRequestedEvent(new ConnectionModel()));
        act.Should().NotThrow();
    }
}
