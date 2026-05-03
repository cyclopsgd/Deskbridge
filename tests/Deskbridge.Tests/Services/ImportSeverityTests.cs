using System.ComponentModel;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;
using Wpf.Ui.Controls;

namespace Deskbridge.Tests.Services;

/// <summary>
/// Phase 22 Plan 22-02 (UI-SPEC §"Severity Selection Rule"): RED-phase tests
/// for the deterministic Severity / Title rule that drives Step 4's InfoBar.
///
/// Rule:
///   FailedCount==0                       → Success / "Import Complete"
///   FailedCount>0 && ImportedCount>0     → Warning / "Import Completed with Errors"
///   FailedCount>0 && ImportedCount==0    → Error   / "Import Failed"
/// </summary>
[Trait("Category", "ImportSeverity")]
public sealed class ImportSeverityTests
{
    private static ImportWizardViewModel MakeVm()
    {
        var importer = Substitute.For<IConnectionImporter>();
        importer.SourceName.Returns("mRemoteNG");
        importer.FileFilter.Returns("*.xml");
        var store = Substitute.For<IConnectionStore>();
        store.GetAll().Returns(Array.Empty<ConnectionModel>());
        store.GetGroups().Returns(Array.Empty<ConnectionGroup>());
        var executor = Substitute.For<IImportExecutor>();
        return new ImportWizardViewModel(
            importers: [importer],
            store: store,
            bus: Substitute.For<IEventBus>(),
            audit: Substitute.For<IAuditLogger>(),
            executor: executor,
            fileBrowser: null);
    }

    [Fact]
    public void ImportSeverity_NoFailures_ReturnsSuccess()
    {
        var vm = MakeVm();
        vm.ImportedCount = 5;
        vm.ImportSeverity.Should().Be(InfoBarSeverity.Success);
    }

    [Fact]
    public void ImportSeverity_PartialFailure_ReturnsWarning()
    {
        var vm = MakeVm();
        vm.ImportedCount = 5;
        vm.Failures.Add(new FailedImport("X", ImportFailureType.Unknown, "boom"));
        vm.ImportSeverity.Should().Be(InfoBarSeverity.Warning);
    }

    [Fact]
    public void ImportSeverity_TotalFailure_ReturnsError()
    {
        var vm = MakeVm();
        vm.ImportedCount = 0;
        vm.Failures.Add(new FailedImport("X", ImportFailureType.Unknown, "boom"));
        vm.ImportSeverity.Should().Be(InfoBarSeverity.Error);
    }

    [Fact]
    public void ImportTitleText_Success_ReturnsImportComplete()
    {
        var vm = MakeVm();
        vm.ImportedCount = 5;
        vm.ImportTitleText.Should().Be("Import Complete");
    }

    [Fact]
    public void ImportTitleText_PartialFailure_ReturnsImportCompletedWithErrors()
    {
        var vm = MakeVm();
        vm.ImportedCount = 5;
        vm.Failures.Add(new FailedImport("X", ImportFailureType.Unknown, "boom"));
        vm.ImportTitleText.Should().Be("Import Completed with Errors");
    }

    [Fact]
    public void ImportTitleText_TotalFailure_ReturnsImportFailed()
    {
        var vm = MakeVm();
        vm.ImportedCount = 0;
        vm.Failures.Add(new FailedImport("X", ImportFailureType.Unknown, "boom"));
        vm.ImportTitleText.Should().Be("Import Failed");
    }

    [Fact]
    public void PropertyChanged_FiresImportSeverityAndTitleText_WhenFailuresGrows()
    {
        var vm = MakeVm();
        vm.ImportedCount = 1;

        var fired = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.Failures.Add(new FailedImport("X", ImportFailureType.Unknown, "boom"));

        fired.Should().Contain(nameof(ImportWizardViewModel.ImportSeverity));
        fired.Should().Contain(nameof(ImportWizardViewModel.ImportTitleText));
    }
}
