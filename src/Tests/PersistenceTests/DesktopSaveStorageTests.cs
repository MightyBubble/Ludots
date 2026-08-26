using System.IO;
using Ludots.Core.Persistence;
using Ludots.Platform.Abstractions;
using Ludots.Platform.Desktop;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class DesktopSaveStorageTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ludots-save-uat-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void SlotsRoundTripOnRealDisk()
    {
        var storage = new DesktopSaveStorage(_root);
        var store = new SaveSlotStore(storage);
        SaveSlotId slot = SaveSlotId.Manual("disk-a");

        store.WriteSlot(slot, CreateSnapshot(tick: 7));
        Assert.That(storage.Exists(slot.ToStorageKey()), Is.True);

        WorldSaveSnapshot restored = store.ReadSlot(slot);
        Assert.That(restored.Header.Tick, Is.EqualTo(7));
        Assert.That(restored.WorldBytes, Is.EqualTo(CreateSnapshot(tick: 7).WorldBytes));

        IReadOnlyList<Ludots.Core.Persistence.SaveSlotHeader> headers = store.ListSlots();
        Assert.That(headers.Count, Is.EqualTo(1));
        Assert.That(headers[0].Id, Is.EqualTo(slot));
        Assert.That(headers[0].Header.Tick, Is.EqualTo(7));
    }

    [Test]
    public void AtomicCommitFailureKeepsPreviousSlotOnRealDisk()
    {
        var storage = new FailingCommitDesktopStorage(_root);
        var store = new SaveSlotStore(storage);
        SaveSlotId slot = SaveSlotId.Manual("disk-b");

        store.WriteSlot(slot, CreateSnapshot(tick: 1));
        storage.FailNextCommit = true;

        Assert.Throws<SaveContextException>(() => store.WriteSlot(slot, CreateSnapshot(tick: 2)));

        WorldSaveSnapshot restored = store.ReadSlot(slot);
        Assert.That(restored.Header.Tick, Is.EqualTo(1));
        Assert.That(storage.ListFileKeys("saves/"), Is.EqualTo(new[] { slot.ToStorageKey() }));
    }

    [Test]
    public void KeysEscapingSaveRootAreRejected()
    {
        var storage = new DesktopSaveStorage(_root);

        Assert.Throws<InvalidOperationException>(() => storage.ReadAllBytes("../outside.ldsave"));
        Assert.Throws<InvalidOperationException>(() => storage.WriteAllBytes("saves/../../outside.ldsave", new byte[] { 1 }));
    }

    private static WorldSaveSnapshot CreateSnapshot(int tick)
    {
        return new WorldSaveSnapshot(
            new SaveContextHeader(
                SaveContextHeader.CurrentSchemaVersion,
                "mods",
                "registry",
                "entry",
                tick,
                new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
                "engine"),
            new System.Text.Json.Nodes.JsonObject(),
            System.Text.Encoding.UTF8.GetBytes($"world-{tick}"));
    }

    private sealed class FailingCommitDesktopStorage : ISaveStorage
    {
        private readonly DesktopSaveStorage _inner;

        public FailingCommitDesktopStorage(string root)
        {
            _inner = new DesktopSaveStorage(root);
        }

        public string DisplayRoot => _inner.DisplayRoot;

        public bool FailNextCommit { get; set; }

        public IReadOnlyList<string> ListFileKeys(string prefix) => _inner.ListFileKeys(prefix);

        public bool Exists(string key) => _inner.Exists(key);

        public byte[] ReadAllBytes(string key) => _inner.ReadAllBytes(key);

        public void WriteAllBytes(string key, byte[] bytes) => _inner.WriteAllBytes(key, bytes);

        public void CommitTempFile(string tempKey, string finalKey)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new IOException("simulated commit failure");
            }

            _inner.CommitTempFile(tempKey, finalKey);
        }

        public void Delete(string key) => _inner.Delete(key);
    }
}
