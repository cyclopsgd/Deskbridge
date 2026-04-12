using System.Threading;
using Xunit;

namespace Deskbridge.Tests.Fixtures;

/// <summary>
/// xUnit v3 collection definition that binds tests to the <see cref="StaCollectionFixture"/>.
/// RDP ActiveX tests require STA apartment affinity (RDP-ACTIVEX-PITFALLS §6).
///
/// xUnit v3 workers default to MTA on Windows. The fixture does NOT mutate the current thread's
/// apartment state (that is not allowed after the first COM call). Instead, tests that must run
/// on STA call <c>Skip.IfNot(fixture.IsSta(), "STA required")</c> at the top of each test body,
/// and the test host is launched from an STA thread when needed
/// (e.g. <c>DOTNET_TEST_APARTMENT_STATE=STA</c> or using xUnit's STA scheduler).
/// </summary>
[CollectionDefinition("RDP-STA")]
public class RdpStaCollection : ICollectionFixture<StaCollectionFixture>
{
    // Marker type — xUnit v3 discovers the collection via its name.
}

/// <summary>
/// Fixture shared across all tests in the <c>RDP-STA</c> collection.
/// Exposes an apartment-state check that individual tests use to Skip when not on STA.
/// </summary>
public sealed class StaCollectionFixture
{
    public StaCollectionFixture()
    {
        // Intentionally empty. Per RDP-ACTIVEX-PITFALLS §6 we cannot change apartment state
        // after the runtime has pumped any COM calls. Tests assert apartment state at entry.
    }

    /// <summary>Returns true if the current thread is STA.</summary>
    public bool IsSta() => Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;
}
