using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxPreviewDocumentStoreTests
{
    [Test]
    public void SameNameFromDifferentFiles_DoesNotShareUnsavedSnapshots()
    {
        var store = new VfxPreviewDocumentStore();
        var file = new object(); var other = new object(); var owner = new object();
        var snapshot = new VfxPreviewSnapshot([1, 2], "vfx/same.xml", _ => null);
        store.Publish(owner, file, snapshot);
        Assert.That(store.TryGet(other, out _), Is.False);
        Assert.That(store.TryGet(file, out var actual), Is.True);
        Assert.That(actual, Is.SameAs(snapshot));
        store.Remove(owner);
        Assert.That(store.TryGet(file, out _), Is.False);
    }

    [Test]
    public void InvalidUnsavedInput_BlocksStaleSavedPreview_AndClosingRestoresAnotherEditorsSnapshot()
    {
        var store = new VfxPreviewDocumentStore();
        var file = new object(); var first = new object(); var second = new object();
        var snapshot = new VfxPreviewSnapshot([1], "vfx/test.xml", _ => null);
        store.Publish(first, file, snapshot); store.Publish(second, file, null);
        Assert.That(store.TryGet(file, out var invalid), Is.True);
        Assert.That(invalid, Is.Null);
        store.Remove(second);
        Assert.That(store.TryGet(file, out var restored), Is.True);
        Assert.That(restored, Is.SameAs(snapshot));
    }

    [Test]
    public void SaveAs_NotifiesBothPreviousAndNewFileIdentities()
    {
        var store = new VfxPreviewDocumentStore();
        var previous = new object(); var next = new object(); var owner = new object();
        store.Publish(owner, previous, new([1], "old.xml", _ => null));
        var notifications = new List<object>(); store.Changed += notifications.Add;
        store.Publish(owner, next, new([2], "new.xml", _ => null));
        Assert.That(notifications, Is.EqualTo(new[] { previous, next }));
        Assert.That(store.TryGet(previous, out _), Is.False);
        Assert.That(store.TryGet(next, out var snapshot), Is.True);
        Assert.That(snapshot!.GameBytes, Is.EqualTo(new byte[] { 2 }));
    }
}
