using System.IO;
using Deskbridge.Core.Events;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;
using Deskbridge.Protocols.Rdp;
using Deskbridge.Tests.Fakes;
using Deskbridge.ViewModels;
using FluentAssertions;
using NSubstitute;
using Wpf.Ui;

namespace Deskbridge.Tests.ViewModels;

/// <summary>
/// W1 (audit) — bulk edit must be all-or-nothing. When the atomic <see cref="IConnectionStore.SaveBatch"/>
/// throws, the live in-memory <see cref="ConnectionModel"/>s (the store's backing references returned by
/// <c>GetById</c>) must be left completely untouched, matching the dialog's "No changes were saved" message.
/// </summary>
public sealed class BulkEditTransactionTests
{
    private static (ConnectionTreeViewModel sut, IConnectionStore store, IEventBus bus) BuildSut()
    {
        var store = Substitute.For<IConnectionStore>();
        var query = Substitute.For<IConnectionQuery>();
        var creds = Substitute.For<ICredentialService>();
        var dialogs = Substitute.For<IContentDialogService>();
        var snackbar = Substitute.For<ISnackbarService>();
        var provider = Substitute.For<IServiceProvider>();
        var bus = Substitute.For<IEventBus>();
        var tab = Substitute.For<ITabHostManager>();
        var windowState = Substitute.For<IWindowStateService>();

        var sut = new ConnectionTreeViewModel(
            store, query, creds, dialogs, snackbar, provider, bus, tab,
            new AirspaceSwapper(), new FakeDebouncer(), windowState);

        return (sut, store, bus);
    }

    [Fact]
    public void BulkEdit_SaveBatchThrows_ModelsUnchanged()
    {
        var (sut, store, bus) = BuildSut();

        // Two live store models (as GetById would return backing-list references).
        var live = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "A", Hostname = "alpha.local", Domain = "CORP", Port = 3389 },
            new() { Id = Guid.NewGuid(), Name = "B", Hostname = "bravo.local", Domain = "CORP", Port = 3389 },
        };

        // Enable the Domain field with a new value — this is what the user wants to write.
        var vm = new BulkEditViewModel(live);
        vm.DomainField.IsEnabled = true;
        vm.DomainField.Value = "NEWDOMAIN";

        // Persistence fails.
        store.When(s => s.SaveBatch(Arg.Any<IEnumerable<ConnectionModel>>(), Arg.Any<IEnumerable<ConnectionGroup>>()))
             .Do(_ => throw new IOException("disk full"));

        var ok = sut.TryCommitBulkEdit(vm, live);

        ok.Should().BeFalse("a SaveBatch failure vetoes the commit");
        // All-or-nothing: the live models must be exactly as before (no partial mutation).
        live[0].Domain.Should().Be("CORP");
        live[1].Domain.Should().Be("CORP");
        // The tree-refresh event must NOT be published on failure.
        bus.DidNotReceive().Publish(Arg.Any<ConnectionDataChangedEvent>());
    }

    [Fact]
    public void BulkEdit_SaveBatchSucceeds_PersistsEditsAndNotifies()
    {
        var (sut, store, bus) = BuildSut();

        var live = new List<ConnectionModel>
        {
            new() { Id = Guid.NewGuid(), Name = "A", Hostname = "alpha.local", Domain = "CORP", Port = 3389 },
            new() { Id = Guid.NewGuid(), Name = "B", Hostname = "bravo.local", Domain = "CORP", Port = 3389 },
        };

        var vm = new BulkEditViewModel(live);
        vm.DomainField.IsEnabled = true;
        vm.DomainField.Value = "NEWDOMAIN";

        List<ConnectionModel>? committed = null;
        store.When(s => s.SaveBatch(Arg.Any<IEnumerable<ConnectionModel>>(), Arg.Any<IEnumerable<ConnectionGroup>>()))
             .Do(ci =>
             {
                 // Simulate SaveBatch's upsert-by-Id: the committed clones become the store's backing
                 // objects, so a subsequent GetById returns the edited clone (not the original live model).
                 committed = ci.Arg<IEnumerable<ConnectionModel>>().ToList();
                 foreach (var m in committed)
                     store.GetById(m.Id).Returns(m);
             });

        var ok = sut.TryCommitBulkEdit(vm, live);

        ok.Should().BeTrue();
        // The committed objects carry the edit...
        committed.Should().NotBeNull();
        committed!.Should().AllSatisfy(m => m.Domain.Should().Be("NEWDOMAIN"));
        // ...and preserve the same Ids (upsert-by-Id replaces the store entries).
        committed.Select(m => m.Id).Should().BeEquivalentTo(live.Select(m => m.Id));
        // The clones (not the untouched live models) were committed: what the store now returns by Id
        // shows the new value, and is a different instance from the original live model.
        foreach (var original in live)
        {
            var fromStore = store.GetById(original.Id);
            fromStore.Should().NotBeNull();
            fromStore!.Domain.Should().Be("NEWDOMAIN");
            fromStore.Should().NotBeSameAs(original, "the store committed the caller-owned clone, not the live model");
        }
        // The live models the caller still holds were never mutated (edits went to clones).
        live.Should().AllSatisfy(m => m.Domain.Should().Be("CORP"));
        bus.Received(1).Publish(Arg.Any<ConnectionDataChangedEvent>());
    }
}
