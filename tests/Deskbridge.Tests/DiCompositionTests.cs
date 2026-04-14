using System.Windows.Threading;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Pipeline;
using Deskbridge.Core.Services;
using Deskbridge.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui;

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

    /// <summary>
    /// Phase 5 D-01: <see cref="ITabHostManager"/> is registered as a singleton.
    /// Two resolutions return the SAME instance so event-bus subscriptions are
    /// process-lifetime (Pitfall 7).
    /// </summary>
    [Collection("RDP-STA")]
    public sealed class TabHostManagerDiTests
    {
        private readonly StaCollectionFixture _fixture;
        public TabHostManagerDiTests(StaCollectionFixture fixture) => _fixture = fixture;

        [Fact]
        public void ITabHostManager_IsRegistered_AsSingleton()
        {
            _ = _fixture;
            StaRunner.Run(() =>
            {
                using var provider = BuildProviderWithTabHostManager();

                var first = provider.GetRequiredService<ITabHostManager>();
                var second = provider.GetRequiredService<ITabHostManager>();

                ReferenceEquals(first, second).Should().BeTrue(
                    "Phase 5 D-01: ITabHostManager must be a singleton so its coordinator subscriptions are process-lifetime");
            });
        }

        /// <summary>
        /// No circular dependency: the publisher-side switch-to-existing fix in Plan 01
        /// (ConnectionTreeViewModel depends on ITabHostManager — not the other way) broke
        /// the cycle that would have arisen if both singletons injected each other.
        /// </summary>
        [Fact]
        public void FullComposition_DoesNotThrow_CircularDependency()
        {
            _ = _fixture;
            StaRunner.Run(() =>
            {
                using var provider = BuildProviderWithTabHostManager();

                var tabHost = provider.GetRequiredService<ITabHostManager>();
                var coord = provider.GetRequiredService<IConnectionCoordinator>();

                tabHost.Should().NotBeNull();
                coord.Should().NotBeNull();
            });
        }

        private static ServiceProvider BuildProviderWithTabHostManager()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IConnectionPipeline, ConnectionPipeline>();
            services.AddSingleton<IDisconnectPipeline, DisconnectPipeline>();
            services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IConnectionCoordinator, ConnectionCoordinator>();
            services.AddSingleton<ITabHostManager, TabHostManager>();
            // Provide the dispatcher so TabHostManager resolves cleanly under test.
            services.AddSingleton(Dispatcher.CurrentDispatcher);
            return services.BuildServiceProvider();
        }
    }
}
