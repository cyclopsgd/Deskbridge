using Deskbridge.Core.Interfaces;

namespace Deskbridge.Core.Services;

public sealed class EventBus : IEventBus
{
    // Own a private messenger rather than the process-wide WeakReferenceMessenger.Default.
    // EventBus is a DI singleton (App.xaml.cs), so every app component shares this one
    // instance and therefore this one messenger — delivery is unchanged in production.
    // Using .Default leaked across boundaries: xUnit runs test classes in parallel and each
    // `new EventBus()` shared Default, so an event published in one test was delivered to a
    // live view-model in another test running concurrently, enumerating its Tabs while that
    // test mutated them -> intermittent "Collection was modified" failures. A private
    // messenger isolates each EventBus instance and removes reliance on global mutable state.
    private readonly IMessenger _messenger = new WeakReferenceMessenger();

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
