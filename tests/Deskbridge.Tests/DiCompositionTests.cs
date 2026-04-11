using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Deskbridge.Tests;

public sealed class DiCompositionTests
{
    [Fact]
    public void AllCoreServices_ResolveWithoutThrowing()
    {
        var services = new ServiceCollection();

        // Mirror the exact registrations from App.xaml.cs ConfigureServices
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
        services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
        services.AddSingleton<IConnectionQuery, ConnectionQueryService>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBus>().Should().NotBeNull();
        provider.GetRequiredService<INotificationService>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IDisconnectPipeline>().Should().NotBeNull();
        provider.GetRequiredService<IConnectionQuery>().Should().NotBeNull();
    }
}
