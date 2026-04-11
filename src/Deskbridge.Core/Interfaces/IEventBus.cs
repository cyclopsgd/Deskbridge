namespace Deskbridge.Core.Interfaces;

/// <summary>
/// Central event bus for cross-component communication.
/// Backed by WeakReferenceMessenger. Handlers run on the publisher's thread --
/// UI-bound handlers must marshal to Dispatcher themselves.
/// </summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : class;
    void Subscribe<TEvent>(object recipient, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(object recipient) where TEvent : class;
}
