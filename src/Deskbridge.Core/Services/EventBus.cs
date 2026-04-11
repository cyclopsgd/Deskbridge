using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Services;

public sealed class EventBus : IEventBus
{
    private readonly IMessenger _messenger = WeakReferenceMessenger.Default;

    public void Publish<TEvent>(TEvent message) where TEvent : class
    {
        _messenger.Send(message);
    }

    public void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class
    {
        _messenger.Register<TEvent>(recipient, (r, m) => handler(m));
    }

    public void Unsubscribe<TEvent>(object recipient) where TEvent : class
    {
        _messenger.Unregister<TEvent>(recipient);
    }
}
