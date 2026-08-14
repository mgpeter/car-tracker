using CarTracker.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The id is the authorisation, so this is where the confirm gate is actually asserted.
/// </summary>
/// <remarks>
/// An earlier revision of the spec matched a client-supplied id against a block in the client-supplied
/// transcript. These tests are the difference: the tool name and the owner live here, and a request cannot
/// supply either.
/// </remarks>
public sealed class PendingWriteStoreTests
{
    private static PendingWriteStore NewStore() => new(new MemoryCache(new MemoryCacheOptions()));

    private static ICurrentUserAccessor As(int ownerId)
    {
        var accessor = new CurrentUserAccessor();
        accessor.SetOwner(ownerId);
        return accessor;
    }

    [Fact]
    public void A_draft_is_confirmable_by_the_owner_it_was_proposed_to()
    {
        var store = NewStore();
        var id = store.Remember(new PendingWriteRecord(7, "call-1", "add_service", "BT53 AKJ"));

        var found = store.Find(id, As(7));

        Assert.NotNull(found);
        Assert.Equal("add_service", found!.Tool);
        Assert.Equal("call-1", found.ToolCallId);
    }

    [Fact]
    public void Another_owners_draft_does_not_exist()
    {
        // Not a distinct refusal: it presents exactly as an expired or invented id, the same way a cross-owner
        // vehicle presents as not found. Telling the two apart would confirm that the id is real.
        var store = NewStore();
        var id = store.Remember(new PendingWriteRecord(7, "call-1", "delete_service", null));

        Assert.Null(store.Find(id, As(8)));
    }

    [Fact]
    public void An_invented_id_does_not_exist()
    {
        Assert.Null(NewStore().Find("pw_0000000000000000", As(7)));
    }

    [Fact]
    public void An_answered_draft_cannot_be_answered_twice()
    {
        var store = NewStore();
        var id = store.Remember(new PendingWriteRecord(7, "call-1", "add_service", null));

        store.Forget(id);

        Assert.Null(store.Find(id, As(7)));
    }

    [Fact]
    public void A_signed_out_caller_holds_no_drafts()
    {
        // BypassOwnership is the default on a context nothing has pinned — a background job, a test. It must not
        // read as "every owner": a draft belongs to the person who was shown it.
        var store = NewStore();
        var id = store.Remember(new PendingWriteRecord(7, "call-1", "add_service", null));

        Assert.Null(store.Find(id, new CurrentUserAccessor()));
    }

    [Fact]
    public void Ids_are_opaque_and_do_not_repeat()
    {
        var store = NewStore();
        var record = new PendingWriteRecord(7, "call-1", "add_service", null);

        var ids = Enumerable.Range(0, 50).Select(_ => store.Remember(record)).ToList();

        Assert.Equal(50, ids.Distinct().Count());
        Assert.All(ids, id => Assert.StartsWith("pw_", id));

        // Nothing about how many drafts this deployment has proposed, which is an invitation to try the
        // neighbouring ones.
        Assert.All(ids, id => Assert.DoesNotContain("call-1", id));
    }
}
