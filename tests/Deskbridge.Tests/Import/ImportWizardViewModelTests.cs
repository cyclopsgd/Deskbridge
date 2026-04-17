using System.IO;
using System.Text;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;

namespace Deskbridge.Tests.Import;

/// <summary>
/// Phase 7 Plan 07-04 Task 2 (MIG-02/MIG-03/MIG-05): Unit tests for ImportWizardViewModel.
/// 14 tests covering 4-step flow, parse triggering, checkbox cascade, credential mode,
/// event publication, audit logging, and duplicate detection.
///
/// No real file system or WPF dispatcher is used — parse tests inject MemoryStream
/// via ParseFromStreamAsync; store mock captures Save calls for assertion.
/// </summary>
public class ImportWizardViewModelTests
{
    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// Standard ImportResult: 2 containers, 3 RDP connections total.
    /// Hierarchy: Container A -> ConnA1, ConnA2; Container B -> ConnB1.
    /// </summary>
    private static ImportResult BuildStandardImportResult()
    {
        var containerA = new ImportedNode(
            Name: "Container A",
            Type: ImportNodeType.Container,
            Hostname: null,
            Port: 0,
            Username: null,
            Domain: null,
            Protocol: Protocol.Rdp,
            Description: null,
            Children: [
                new ImportedNode("Server A1", ImportNodeType.Connection, "a1.local", 3389, "admin", "DOM", Protocol.Rdp, null, []),
                new ImportedNode("Server A2", ImportNodeType.Connection, "a2.local", 3389, "admin", "DOM", Protocol.Rdp, null, [])
            ]);

        var containerB = new ImportedNode(
            Name: "Container B",
            Type: ImportNodeType.Container,
            Hostname: null,
            Port: 0,
            Username: null,
            Domain: null,
            Protocol: Protocol.Rdp,
            Description: null,
            Children: [
                new ImportedNode("Server B1", ImportNodeType.Connection, "b1.local", 3389, "user", null, Protocol.Rdp, null, [])
            ]);

        return new ImportResult(
            RootNodes: [containerA, containerB],
            TotalConnections: 3,
            TotalFolders: 2);
    }

    private static IConnectionImporter BuildMockImporter(ImportResult result)
    {
        var importer = Substitute.For<IConnectionImporter>();
        importer.SourceName.Returns("mRemoteNG");
        importer.FileFilter.Returns("mRemoteNG Config (*.xml)|*.xml");
        importer.ParseAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(result));
        return importer;
    }

    private static IConnectionImporter BuildFailingImporter(string errorMessage)
    {
        var importer = Substitute.For<IConnectionImporter>();
        importer.SourceName.Returns("mRemoteNG");
        importer.FileFilter.Returns("mRemoteNG Config (*.xml)|*.xml");
        importer.ParseAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns<Task<ImportResult>>(_ => throw new ImportException(errorMessage));
        return importer;
    }

    private static ImportWizardViewModel BuildViewModel(
        IConnectionImporter importer,
        IConnectionStore? store = null,
        IEventBus? bus = null,
        IAuditLogger? audit = null)
    {
        store ??= Substitute.For<IConnectionStore>();
        bus ??= Substitute.For<IEventBus>();
        audit ??= Substitute.For<IAuditLogger>();

        store.GetAll().Returns([]);
        store.GetGroups().Returns([]);

        return new ImportWizardViewModel(
            importers: [importer],
            store: store,
            bus: bus,
            audit: audit,
            fileBrowser: null);
    }

    private static Stream EmptyStream() => new MemoryStream(Encoding.UTF8.GetBytes("<empty/>"));

    // ---------------------------------------------------------------- Test 1

    // Test 1: Initial state is step 1 with AvailableImporters populated
    [Fact]
    public void InitialState_IsStep1_WithAvailableImporters()
    {
        var importer = BuildMockImporter(BuildStandardImportResult());
        var vm = BuildViewModel(importer);

        vm.CurrentStep.Should().Be(1);
        vm.AvailableImporters.Should().HaveCount(1);
        vm.AvailableImporters[0].Should().BeSameAs(importer);
        vm.SelectedImporter.Should().BeSameAs(importer); // auto-selected first importer
    }

    // ---------------------------------------------------------------- Test 2

    // Test 2: NextStep from step 1 with no importer selected does not advance
    [Fact]
    public async Task NextStep_Step1_NoImporterSelected_DoesNotAdvance()
    {
        var importer = BuildMockImporter(BuildStandardImportResult());
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns([]);
        store.GetGroups().Returns([]);
        var vm = new ImportWizardViewModel(
            importers: [importer],
            store: store,
            bus: Substitute.For<IEventBus>(),
            audit: Substitute.For<IAuditLogger>(),
            fileBrowser: null);

        // Force no importer selected
        vm.SelectedImporter = null;

        await vm.NextStepCommand.ExecuteAsync(null);

        vm.CurrentStep.Should().Be(1);
    }

    // ---------------------------------------------------------------- Test 3

    // Test 3: NextStep from step 1 with importer selected advances to step 2
    [Fact]
    public async Task NextStep_Step1_ImporterSelected_AdvancesToStep2()
    {
        var importer = BuildMockImporter(BuildStandardImportResult());
        var vm = BuildViewModel(importer);

        vm.SelectedImporter = importer;
        await vm.NextStepCommand.ExecuteAsync(null);

        vm.CurrentStep.Should().Be(2);
    }

    // ---------------------------------------------------------------- Test 4

    // Test 4: NextStep from step 2 with no file selected does not advance
    [Fact]
    public async Task NextStep_Step2_NoFileSelected_DoesNotAdvance()
    {
        var importer = BuildMockImporter(BuildStandardImportResult());
        var vm = BuildViewModel(importer);

        // Advance to step 2
        await vm.NextStepCommand.ExecuteAsync(null);
        vm.CurrentStep.Should().Be(2);

        // Ensure no file path set
        vm.FilePath = null;

        await vm.NextStepCommand.ExecuteAsync(null);

        vm.CurrentStep.Should().Be(2);
    }

    // ---------------------------------------------------------------- Test 5

    // Test 5: NextStep from step 2 with valid file (via ParseFromStreamAsync) advances to step 3
    [Fact]
    public async Task ParseFromStream_ValidResult_AdvancesToStep3()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var vm = BuildViewModel(importer);

        // Start at step 2 for context
        await vm.NextStepCommand.ExecuteAsync(null); // 1 -> 2
        vm.FilePath = "/fake/path/confCons.xml"; // set a file path (bypassed by ParseFromStream)

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        vm.CurrentStep.Should().Be(3);
        vm.ParseResult.Should().NotBeNull();
        vm.ParseResult!.TotalConnections.Should().Be(3);
        vm.ParseResult.TotalFolders.Should().Be(2);
    }

    // ---------------------------------------------------------------- Test 6

    // Test 6: ParseAsync failure sets ErrorMessage and stays on step 2
    [Fact]
    public async Task ParseFromStream_ImportException_SetsErrorMessageAndStaysOnStep2()
    {
        const string errorMsg = "File is encrypted. Export unencrypted first.";
        var importer = BuildFailingImporter(errorMsg);
        var vm = BuildViewModel(importer);

        await vm.NextStepCommand.ExecuteAsync(null); // 1 -> 2
        vm.FilePath = "/fake/path/confCons.xml";

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        vm.CurrentStep.Should().Be(2);
        vm.ErrorMessage.Should().Be(errorMsg);
    }

    // ---------------------------------------------------------------- Test 7

    // Test 7: PreviewItems populated with correct tree structure after parse
    [Fact]
    public async Task ParseFromStream_ValidResult_BuildsPreviewTreeWithCorrectStructure()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var vm = BuildViewModel(importer);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        vm.PreviewItems.Should().HaveCount(2, "two root containers");

        var containerA = vm.PreviewItems[0];
        containerA.Name.Should().Be("Container A");
        containerA.Type.Should().Be(ImportNodeType.Container);
        containerA.Children.Should().HaveCount(2);
        containerA.Children[0].Name.Should().Be("Server A1");
        containerA.Children[0].Type.Should().Be(ImportNodeType.Connection);
        containerA.Children[0].Hostname.Should().Be("a1.local");

        var containerB = vm.PreviewItems[1];
        containerB.Name.Should().Be("Container B");
        containerB.Children.Should().HaveCount(1);
        containerB.Children[0].Name.Should().Be("Server B1");
    }

    // ---------------------------------------------------------------- Test 8

    // Test 8: Unchecking a folder unchecks all children (cascade)
    [Fact]
    public async Task UncheckFolder_CascadesUncheckedToAllChildren()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var vm = BuildViewModel(importer);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        var containerA = vm.PreviewItems[0];
        // Verify children are checked by default before unchecking parent
        containerA.IsChecked.Should().BeTrue();
        containerA.Children.Should().AllSatisfy(c => c.IsChecked.Should().BeTrue());

        // Uncheck the folder
        containerA.IsChecked = false;

        // All children should be unchecked
        containerA.Children.Should().AllSatisfy(c =>
            c.IsChecked.Should().BeFalse("unchecking parent folder cascades to children"));
    }

    // ---------------------------------------------------------------- Test 9

    // Test 9: NextStep from step 3 imports selected connections to store
    [Fact]
    public async Task ImportSelected_CallsStoreSaveForEachCheckedConnection()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns([]);
        store.GetGroups().Returns([]);
        var vm = BuildViewModel(importer, store: store);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        await vm.ImportSelectedAsync();

        // 3 connections all checked by default -> store.Save called 3 times
        store.Received(3).Save(Arg.Any<ConnectionModel>());
    }

    // ---------------------------------------------------------------- Test 10

    // Test 10: Import sets CredentialMode.Prompt on all imported connections (MIG-03)
    [Fact]
    public async Task ImportSelected_AllConnections_HaveCredentialModePrompt()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var capturedConnections = new List<ConnectionModel>();
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns([]);
        store.GetGroups().Returns([]);
        store.When(s => s.Save(Arg.Any<ConnectionModel>()))
             .Do(ci => capturedConnections.Add(ci.Arg<ConnectionModel>()));
        var vm = BuildViewModel(importer, store: store);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);
        await vm.ImportSelectedAsync();

        capturedConnections.Should().NotBeEmpty();
        capturedConnections.Should().AllSatisfy(c =>
            c.CredentialMode.Should().Be(CredentialMode.Prompt,
                "MIG-03: no passwords imported, all connections use CredentialMode.Prompt"));
    }

    // ---------------------------------------------------------------- Test 11

    // Test 11: Import publishes ConnectionImportedEvent with correct count and source name
    [Fact]
    public async Task ImportSelected_PublishesConnectionImportedEventWithCorrectCountAndSource()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var bus = Substitute.For<IEventBus>();
        var vm = BuildViewModel(importer, bus: bus);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);
        await vm.ImportSelectedAsync();

        bus.Received(1).Publish(Arg.Is<ConnectionImportedEvent>(e =>
            e.Count == 3 && e.Source == "mRemoteNG"));
    }

    // ---------------------------------------------------------------- Test 12

    // Test 12: Import writes audit log entry with ConnectionsImported action
    [Fact]
    public async Task ImportSelected_WritesAuditLogWithConnectionsImportedAction()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);
        var audit = Substitute.For<IAuditLogger>();
        var vm = BuildViewModel(importer, audit: audit);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);
        await vm.ImportSelectedAsync();

        await audit.Received(1).LogAsync(
            Arg.Is<AuditRecord>(r =>
                r.Type == AuditAction.ConnectionsImported.ToString()),
            Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- Test 13

    // Test 13: Duplicate hostname detected produces auto-rename (DuplicateItems populated)
    [Fact]
    public async Task ImportSelected_DuplicateHostname_AutoRenamesAndIncrementsRenamedCount()
    {
        var importResult = BuildStandardImportResult();
        var importer = BuildMockImporter(importResult);

        // Pre-populate store with a connection that matches "a1.local"
        var existingConnection = new ConnectionModel
        {
            Id = Guid.NewGuid(),
            Name = "Existing Server",
            Hostname = "a1.local",
            Port = 3389,
            Protocol = Protocol.Rdp,
            CredentialMode = CredentialMode.Own
        };

        // Build store directly — do NOT use BuildViewModel helper because it
        // would call store.GetAll().Returns([]) and overwrite the duplicate seed.
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(new List<ConnectionModel> { existingConnection });
        store.GetGroups().Returns(new List<ConnectionGroup>());

        var vm = new ImportWizardViewModel(
            importers: [importer],
            store: store,
            bus: Substitute.For<IEventBus>(),
            audit: Substitute.For<IAuditLogger>(),
            fileBrowser: null);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);
        await vm.ImportSelectedAsync();

        // One connection (a1.local) was a duplicate and should have been auto-renamed
        vm.RenamedCount.Should().Be(1, "a1.local matches an existing connection hostname");
        // Total imported still includes the renamed one
        vm.ImportedCount.Should().Be(3);
    }

    // ---------------------------------------------------------------- Test 14

    // Test 14: PreviousStep decrements current step
    [Fact]
    public async Task PreviousStep_DecrementsCurrentStep()
    {
        var importer = BuildMockImporter(BuildStandardImportResult());
        var vm = BuildViewModel(importer);

        // Advance to step 2
        await vm.NextStepCommand.ExecuteAsync(null);
        vm.CurrentStep.Should().Be(2);

        // Go back
        vm.PreviousStepCommand.Execute(null);

        vm.CurrentStep.Should().Be(1);
    }
}
