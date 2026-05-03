using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// Phase 22 Plan 22-02 (IMP-03 / D-02 / D-03 / D-06 / D-07 / D-15): RED-phase
/// tests for the new VM properties, CT threading at parse sites, and the
/// refactored ImportSelectedAsync that delegates to IImportExecutor.
///
/// Asserts:
///  - Defaults for IsImportWriteInProgress / TotalToImport / Failures.
///  - Constructor accepts IImportExecutor as a positional parameter (D-03 wiring).
///  - ParseFileAsync + ParseFromStreamAsync thread a non-default CancellationToken
///    into IConnectionImporter.ParseAsync (D-06 fix-forward at lines 181 / 211).
///  - ImportSelectedAsync flips IsImportWriteInProgress around _executor.PrepareAsync.
///  - Failures populated from the executor's ImportPrepareResult.Failures.
///  - SaveBatch called exactly ONCE per import (D-02).
///  - Fatal executor exception suppresses SaveBatch and surfaces ErrorMessage (D-07).
///  - ImportSummary text format reflects FailedCount in 3 branches.
/// </summary>
[Trait("Category", "ImportWizardViewModelImportProgress")]
public sealed class ImportWizardViewModelImportProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- helpers ---------------------------------------------------------

    private static ImportResult BuildSimpleImportResult(int connections = 3)
    {
        var nodes = new List<ImportedNode>();
        for (int i = 0; i < connections; i++)
        {
            nodes.Add(new ImportedNode(
                Name: $"Server{i}",
                Type: ImportNodeType.Connection,
                Hostname: $"h{i}.local",
                Port: 3389,
                Username: null,
                Domain: null,
                Protocol: Protocol.Rdp,
                Description: null,
                Children: Array.Empty<ImportedNode>()));
        }
        return new ImportResult(RootNodes: nodes, TotalConnections: connections, TotalFolders: 0);
    }

    private static IConnectionImporter BuildMockImporter(ImportResult? result = null)
    {
        var importer = Substitute.For<IConnectionImporter>();
        importer.SourceName.Returns("mRemoteNG");
        importer.FileFilter.Returns("mRemoteNG Config (*.xml)|*.xml");
        importer.ParseAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(result ?? BuildSimpleImportResult()));
        return importer;
    }

    private static IImportExecutor BuildExecutorReturning(
        ImportPrepareResult result,
        Action<ImportRequest, IProgress<int>?, CancellationToken>? onCalled = null)
    {
        var exec = Substitute.For<IImportExecutor>();
        exec.PrepareAsync(Arg.Any<ImportRequest>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                onCalled?.Invoke(
                    ci.Arg<ImportRequest>(),
                    ci.ArgAt<IProgress<int>?>(1),
                    ci.ArgAt<CancellationToken>(2));
                return Task.FromResult(result);
            });
        return exec;
    }

    private static ImportPrepareResult EmptyResult(int imported = 0)
        => new(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: Array.Empty<FailedImport>(),
            ImportedCount: imported,
            SkippedCount: 0,
            RenamedCount: 0);

    private static ImportWizardViewModel MakeSubject(
        IConnectionImporter? importer = null,
        IConnectionStore? store = null,
        IEventBus? bus = null,
        IAuditLogger? audit = null,
        IImportExecutor? executor = null)
    {
        importer ??= BuildMockImporter();
        store ??= Substitute.For<IConnectionStore>();
        bus ??= Substitute.For<IEventBus>();
        audit ??= Substitute.For<IAuditLogger>();
        executor ??= BuildExecutorReturning(EmptyResult());

        store.GetAll().Returns(Array.Empty<ConnectionModel>());
        store.GetGroups().Returns(Array.Empty<ConnectionGroup>());

        return new ImportWizardViewModel(
            importers: [importer],
            store: store,
            bus: bus,
            audit: audit,
            executor: executor,
            fileBrowser: null);
    }

    private static Stream EmptyStream() => new MemoryStream(Encoding.UTF8.GetBytes("<empty/>"));

    private static async Task LoadCheckedTreeAsync(ImportWizardViewModel vm, ImportResult result)
    {
        // Ensure importer returns the requested result for ParseFromStreamAsync.
        // The importer mock is shared — re-stub via the existing import field.
        // Simplest: just call ParseFromStreamAsync; the mock will return whatever it was set up with.
        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);
    }

    // --- defaults --------------------------------------------------------

    [Fact]
    public void Defaults_IsImportWriteInProgress_False()
    {
        var vm = MakeSubject();
        vm.IsImportWriteInProgress.Should().BeFalse();
    }

    [Fact]
    public void Defaults_TotalToImport_Zero()
    {
        var vm = MakeSubject();
        vm.TotalToImport.Should().Be(0);
    }

    [Fact]
    public void Defaults_Failures_Empty()
    {
        var vm = MakeSubject();
        vm.Failures.Should().BeEmpty();
        vm.FailedCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_AcceptsIImportExecutor()
    {
        // Reflect on ctors: at least one ctor must accept IImportExecutor as a
        // positional parameter. Phase 22 (D-03) requires DI to inject the
        // executor at construction time — no setter, no service-locator.
        var ctors = typeof(ImportWizardViewModel).GetConstructors();
        ctors.Should().NotBeEmpty();
        ctors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IImportExecutor)))
            .Should().BeTrue("ImportWizardViewModel must accept IImportExecutor via DI");
    }

    // --- CT threading (D-06 fix-forward) ---------------------------------

    [Fact]
    public async Task ParseFileAsync_ThreadsCancellationTokenIntoImporter()
    {
        // Use ParseFileAsync via NextStepCommand from Step 2 with a real-ish file path.
        // NextStepCommand on Step 2 calls ParseFileAsync internally; ParseFileAsync
        // requires File.OpenRead so a real temp file is needed.
        var importer = BuildMockImporter(BuildSimpleImportResult());
        var vm = MakeSubject(importer: importer);

        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "<dummy/>");
            vm.SelectedImporter = importer;
            vm.FilePath = temp;
            await vm.ParseFileAsync_ForTesting();

            // Verify the importer received a non-default CT.
            await importer.Received().ParseAsync(
                Arg.Any<Stream>(),
                Arg.Is<CancellationToken>(ct => ct != default));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task ParseFromStreamAsync_ThreadsCancellationTokenIntoImporter()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult());
        var vm = MakeSubject(importer: importer);

        using var stream = EmptyStream();
        await vm.ParseFromStreamAsync(stream);

        await importer.Received().ParseAsync(
            Arg.Any<Stream>(),
            Arg.Is<CancellationToken>(ct => ct != default));
    }

    // --- ImportSelectedAsync delegation ----------------------------------

    [Fact]
    public async Task ImportSelectedAsync_FlipsIsImportWriteInProgressAroundExecutor()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult());

        bool capturedDuringCall = false;
        ImportWizardViewModel? capturedVm = null;
        var executor = Substitute.For<IImportExecutor>();
        executor.PrepareAsync(Arg.Any<ImportRequest>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    capturedDuringCall = capturedVm!.IsImportWriteInProgress;
                    return Task.FromResult(EmptyResult(imported: 3));
                });

        var vm = MakeSubject(importer: importer, executor: executor);
        capturedVm = vm;

        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult());
        await vm.ImportSelectedAsync_ForTesting();

        capturedDuringCall.Should().BeTrue("flag must be true while executor.PrepareAsync runs");
        vm.IsImportWriteInProgress.Should().BeFalse("flag must be cleared after import completes");
    }

    [Fact]
    public async Task ImportSelectedAsync_BulkLoadsFailures()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult(connections: 5));
        var failures = Enumerable.Range(0, 5)
            .Select(i => new FailedImport(
                ConnectionName: $"Bad{i}",
                Type: ImportFailureType.Unknown,
                Detail: $"reason {i}"))
            .ToList();
        var execResult = new ImportPrepareResult(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: failures,
            ImportedCount: 0,
            SkippedCount: 0,
            RenamedCount: 0);
        var executor = BuildExecutorReturning(execResult);

        var vm = MakeSubject(importer: importer, executor: executor);
        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult(connections: 5));
        await vm.ImportSelectedAsync_ForTesting();

        vm.Failures.Should().HaveCount(5);
        vm.FailedCount.Should().Be(5);
    }

    [Fact]
    public async Task ImportSelectedAsync_CallsSaveBatchExactlyOnce()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult());
        var executor = BuildExecutorReturning(EmptyResult(imported: 3));
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(Array.Empty<ConnectionModel>());
        store.GetGroups().Returns(Array.Empty<ConnectionGroup>());

        var vm = MakeSubject(importer: importer, store: store, executor: executor);
        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult());
        await vm.ImportSelectedAsync_ForTesting();

        store.Received(1).SaveBatch(
            Arg.Any<IEnumerable<ConnectionModel>>(),
            Arg.Any<IEnumerable<ConnectionGroup>>());
    }

    [Fact]
    public async Task ImportSelectedAsync_DoesNotCallSaveBatch_WhenExecutorThrows()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult());
        var executor = Substitute.For<IImportExecutor>();
        executor.PrepareAsync(Arg.Any<ImportRequest>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
                .Returns<Task<ImportPrepareResult>>(_ => throw new InvalidOperationException("boom"));
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(Array.Empty<ConnectionModel>());
        store.GetGroups().Returns(Array.Empty<ConnectionGroup>());

        var vm = MakeSubject(importer: importer, store: store, executor: executor);
        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult());
        await vm.ImportSelectedAsync_ForTesting();

        store.DidNotReceive().SaveBatch(
            Arg.Any<IEnumerable<ConnectionModel>>(),
            Arg.Any<IEnumerable<ConnectionGroup>>());
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.CurrentStep.Should().Be(3, "fatal executor failure leaves user on Step 3");
    }

    // --- ImportSummary copy verification ---------------------------------

    [Fact]
    public async Task ImportSummary_AppendsFailedClause_WhenFailedCountGreaterThanZero()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult(connections: 100));
        var failures = Enumerable.Range(0, 5)
            .Select(i => new FailedImport($"Bad{i}", ImportFailureType.Unknown, "reason"))
            .ToList();
        var execResult = new ImportPrepareResult(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: failures,
            ImportedCount: 95,
            SkippedCount: 0,
            RenamedCount: 0);
        var executor = BuildExecutorReturning(execResult);
        var vm = MakeSubject(importer: importer, executor: executor);

        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult(connections: 100));
        await vm.ImportSelectedAsync_ForTesting();

        vm.ImportSummary.Should().Be("Imported 95 connection(s). 0 skipped, 0 renamed, 5 failed.");
    }

    [Fact]
    public async Task ImportSummary_TotalFailure_RendersImportFailedFormat()
    {
        var importer = BuildMockImporter(BuildSimpleImportResult(connections: 100));
        var failures = Enumerable.Range(0, 5)
            .Select(i => new FailedImport($"Bad{i}", ImportFailureType.Unknown, "reason"))
            .ToList();
        var execResult = new ImportPrepareResult(
            ConnectionsToSave: Array.Empty<ConnectionModel>(),
            GroupsToSave: Array.Empty<ConnectionGroup>(),
            Failures: failures,
            ImportedCount: 0,
            SkippedCount: 0,
            RenamedCount: 0);
        var executor = BuildExecutorReturning(execResult);
        var vm = MakeSubject(importer: importer, executor: executor);

        await LoadCheckedTreeAsync(vm, BuildSimpleImportResult(connections: 100));
        await vm.ImportSelectedAsync_ForTesting();

        vm.ImportSummary.Should().Be("Import failed. 5 of 100 connections could not be saved. See log for details.");
    }
}

/// <summary>
/// Test-only access shims — the VM's import/parse helpers are <c>internal</c>;
/// these public extension methods make them callable from tests in the same
/// assembly without changing visibility.
/// </summary>
internal static class ImportWizardViewModelTestAccess
{
    public static Task ImportSelectedAsync_ForTesting(this ImportWizardViewModel vm)
        => (Task)typeof(ImportWizardViewModel)
            .GetMethod("ImportSelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, Array.Empty<object?>())!;

    public static Task ParseFileAsync_ForTesting(this ImportWizardViewModel vm)
        => (Task)typeof(ImportWizardViewModel)
            .GetMethod("ParseFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, Array.Empty<object?>())!;
}
