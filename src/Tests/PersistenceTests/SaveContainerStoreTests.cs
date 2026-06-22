using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.Persistence;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class SaveContainerStoreTests
{
    [Test]
    public void ContainerCodecRoundTripsSnapshotSections()
    {
        WorldSaveSnapshot snapshot = CreateSnapshot("entry", tick: 12);
        var codec = new SaveContainerCodec();

        byte[] bytes = codec.Encode(snapshot);
        WorldSaveSnapshot decoded = codec.Decode(bytes);

        Assert.That(decoded.Header, Is.EqualTo(snapshot.Header));
        Assert.That(decoded.Domains.ToJsonString(), Is.EqualTo(snapshot.Domains.ToJsonString()));
        Assert.That(decoded.WorldBytes, Is.EqualTo(snapshot.WorldBytes));
    }

    [Test]
    public void ContainerCodecReadsHeaderWithoutTouchingWorldBlob()
    {
        WorldSaveSnapshot snapshot = CreateSnapshot("entry", tick: 21);
        var codec = new SaveContainerCodec();

        byte[] bytes = codec.Encode(snapshot);
        CorruptWorldBytes(bytes);

        SaveContextHeader header = codec.ReadHeader(bytes);

        Assert.That(header, Is.EqualTo(snapshot.Header));
        Assert.Throws<SaveContextException>(() => codec.Decode(bytes));
    }

    [Test]
    public void SlotStoreKeepsExistingSaveWhenAtomicCommitFails()
    {
        var storage = new MemorySaveStorage();
        var store = new SaveSlotStore(storage);
        SaveSlotId slot = SaveSlotId.Manual("slot-a");

        store.WriteSlot(slot, CreateSnapshot("entry", tick: 1));
        storage.FailNextCommit = true;

        Assert.Throws<SaveContextException>(() => store.WriteSlot(slot, CreateSnapshot("entry", tick: 2)));

        WorldSaveSnapshot restored = store.ReadSlot(slot);
        Assert.That(restored.Header.Tick, Is.EqualTo(1));
    }

    [Test]
    public void SlotStoreListsHeadersWithoutReadingWorldBlob()
    {
        var storage = new MemorySaveStorage();
        var store = new SaveSlotStore(storage);
        SaveSlotId manual = SaveSlotId.Manual("alpha");
        SaveSlotId autosave = SaveSlotId.Autosave("0003");

        store.WriteSlot(manual, CreateSnapshot("entry", tick: 4));
        store.WriteSlot(autosave, CreateSnapshot("entry", tick: 5));
        storage.CorruptWorldBytes(manual);

        SaveSlotHeader[] headers = store.ListSlots().OrderBy(slot => slot.Id.Value, StringComparer.Ordinal).ToArray();

        Assert.That(headers.Select(header => header.Id), Is.EqualTo(new[] { autosave, manual }));
        Assert.That(headers.Select(header => header.Header.Tick), Is.EqualTo(new[] { 5, 4 }));
        Assert.Throws<SaveContextException>(() => store.ReadSlot(manual));
    }

    [Test]
    public void AutosaveRingRetainsNewestAutosavesWithoutDeletingManualSlots()
    {
        var storage = new MemorySaveStorage();
        var store = new SaveSlotStore(storage);
        var autosaves = new AutosaveSlotPolicy(retentionCount: 2);

        store.WriteSlot(SaveSlotId.Manual("checkpoint"), CreateSnapshot("entry", tick: 100));
        autosaves.WriteAutosave(store, CreateSnapshot("entry", tick: 1));
        autosaves.WriteAutosave(store, CreateSnapshot("entry", tick: 2));
        autosaves.WriteAutosave(store, CreateSnapshot("entry", tick: 3));

        SaveSlotHeader[] headers = store.ListSlots().OrderBy(slot => slot.Header.Tick).ToArray();

        Assert.That(
            headers.Select(header => header.Id),
            Is.EqualTo(new[]
            {
                SaveSlotId.Autosave("0000000002"),
                SaveSlotId.Autosave("0000000003"),
                SaveSlotId.Manual("checkpoint")
            }));
        Assert.That(store.ReadSlot(SaveSlotId.Manual("checkpoint")).Header.Tick, Is.EqualTo(100));
    }

    private static WorldSaveSnapshot CreateSnapshot(string mapId, int tick)
    {
        return new WorldSaveSnapshot(
            new SaveContextHeader(
                SaveContextHeader.CurrentSchemaVersion,
                "mods",
                "registry",
                mapId,
                tick,
                new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero),
                "engine"),
            new JsonObject
            {
                ["gameSession"] = new JsonObject
                {
                    ["currentTick"] = tick
                }
            },
            Encoding.UTF8.GetBytes($"world-{tick}"));
    }

    private static void CorruptWorldBytes(byte[] bytes)
    {
        byte[] needle = Encoding.UTF8.GetBytes("world-21");
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                bytes[i] ^= 0x7F;
                return;
            }
        }

        Assert.Fail("Encoded world bytes marker not found.");
    }

    private sealed class MemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _tempFiles = new(StringComparer.Ordinal);

        public bool FailNextCommit { get; set; }

        public IReadOnlyList<string> ListFileKeys(string prefix)
        {
            return _files.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }

        public bool Exists(string key)
        {
            return _files.ContainsKey(key);
        }

        public byte[] ReadAllBytes(string key)
        {
            return _files.TryGetValue(key, out byte[]? bytes)
                ? bytes.ToArray()
                : throw new FileNotFoundException(key);
        }

        public void WriteAllBytes(string key, byte[] bytes)
        {
            _files[key] = bytes.ToArray();
            _tempFiles.Add(key);
        }

        public void CommitTempFile(string tempKey, string finalKey)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new IOException("commit failed");
            }

            _files[finalKey] = ReadAllBytes(tempKey);
            _files.Remove(tempKey);
            _tempFiles.Remove(tempKey);
        }

        public void Delete(string key)
        {
            _files.Remove(key);
            _tempFiles.Remove(key);
        }

        public void CorruptWorldBytes(SaveSlotId id)
        {
            byte[] bytes = _files[id.ToStorageKey()].ToArray();
            byte[] marker = Encoding.UTF8.GetBytes("world-4");
            for (int i = 0; i <= bytes.Length - marker.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                {
                    if (bytes[i + j] != marker[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    bytes[i] ^= 0x7F;
                    _files[id.ToStorageKey()] = bytes;
                    return;
                }
            }

            Assert.Fail("Encoded world bytes marker not found.");
        }
    }
}
