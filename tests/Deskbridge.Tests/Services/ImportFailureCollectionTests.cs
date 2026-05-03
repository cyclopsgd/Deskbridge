using Deskbridge.Core.Models;
using FluentAssertions;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 Plan 22-01 (D-13/D-14/D-15): RED-phase tests for the new domain
/// records added to <c>ImportModels.cs</c>. These tests assert the shape of
/// <see cref="FailedImport"/>, <see cref="ImportFailureType"/>,
/// <see cref="ImportPrepareResult"/>, and <see cref="DuplicateResolution"/>
/// before the types exist (Task 2 lands the type declarations).
/// </summary>
[Trait("Category", "ImportFailureCollection")]
public sealed class ImportFailureCollectionTests
{
    [Fact]
    public void FailedImport_PositionalProperties_Bind()
    {
        var failure = new FailedImport(
            ConnectionName: "Web Server 01",
            Type: ImportFailureType.MalformedXml,
            Detail: "Port attribute missing");

        failure.ConnectionName.Should().Be("Web Server 01");
        failure.Type.Should().Be(ImportFailureType.MalformedXml);
        failure.Detail.Should().Be("Port attribute missing");
    }

    [Fact]
    public void FailedImport_Equals_ByValue()
    {
        var a = new FailedImport("conn-a", ImportFailureType.Duplicate, "exists");
        var b = new FailedImport("conn-a", ImportFailureType.Duplicate, "exists");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Theory]
    [InlineData(ImportFailureType.Duplicate)]
    [InlineData(ImportFailureType.MalformedXml)]
    [InlineData(ImportFailureType.ValidationError)]
    [InlineData(ImportFailureType.Unknown)]
    public void ImportFailureType_RoundTrip(ImportFailureType type)
    {
        var failure = new FailedImport("x", type, "y");
        failure.Type.Should().Be(type);
    }

    [Fact]
    public void ImportFailureType_DeclaresExactlyFourMembers_InContractOrder()
    {
        // D-14 locks the enum order: Duplicate, MalformedXml, ValidationError, Unknown.
        // The integer values are an additive contract — Serilog filters and
        // future open-enum extensions append to the end.
        ((int)ImportFailureType.Duplicate).Should().Be(0);
        ((int)ImportFailureType.MalformedXml).Should().Be(1);
        ((int)ImportFailureType.ValidationError).Should().Be(2);
        ((int)ImportFailureType.Unknown).Should().Be(3);

        Enum.GetValues<ImportFailureType>().Should().HaveCount(4);
    }

    [Fact]
    public void ImportPrepareResult_EmptyFailures_RoundTrips()
    {
        var result = new ImportPrepareResult(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: Array.Empty<FailedImport>(),
            ImportedCount: 0,
            SkippedCount: 0,
            RenamedCount: 0);

        result.Failures.Should().BeEmpty();
        result.ConnectionsToSave.Should().BeEmpty();
        result.GroupsToSave.Should().BeEmpty();
        result.ImportedCount.Should().Be(0);
    }

    [Fact]
    public void ImportPrepareResult_CountsAreIndependentOfFailures()
    {
        // Verifies D-13/D-15: ImportedCount/SkippedCount/RenamedCount are
        // independent ints — Failures.Count is NOT one of them.
        var failures = new[]
        {
            new FailedImport("a", ImportFailureType.Unknown, "boom"),
            new FailedImport("b", ImportFailureType.ValidationError, "bad port"),
        };

        var result = new ImportPrepareResult(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: failures,
            ImportedCount: 7,
            SkippedCount: 3,
            RenamedCount: 2);

        result.Failures.Should().HaveCount(2);
        result.ImportedCount.Should().Be(7);
        result.SkippedCount.Should().Be(3);
        result.RenamedCount.Should().Be(2);
        // Failures.Count NOT folded into the counters.
        (result.ImportedCount + result.SkippedCount + result.RenamedCount)
            .Should().Be(12);
    }

    [Fact]
    public void DuplicateResolution_PositionalProperties_Bind()
    {
        var resolution = new DuplicateResolution(
            Hostname: "web01.prod.local",
            Action: DuplicateAction.Skip);

        resolution.Hostname.Should().Be("web01.prod.local");
        resolution.Action.Should().Be(DuplicateAction.Skip);
    }

    [Theory]
    [InlineData(DuplicateAction.Skip)]
    [InlineData(DuplicateAction.Overwrite)]
    [InlineData(DuplicateAction.Rename)]
    public void DuplicateResolution_AllActions_RoundTrip(DuplicateAction action)
    {
        var resolution = new DuplicateResolution("host", action);
        resolution.Action.Should().Be(action);
    }
}
