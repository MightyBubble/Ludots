using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Persistence
{
    public readonly record struct SaveSlotId(string Kind, string Name)
    {
        public static SaveSlotId Manual(string name)
        {
            return new SaveSlotId("manual", name);
        }

        public static SaveSlotId Autosave(string name)
        {
            return new SaveSlotId("autosave", name);
        }

        public string Value => $"{Kind}/{Name}";

        public string ToStorageKey()
        {
            ValidateToken(Kind, nameof(Kind));
            ValidateToken(Name, nameof(Name));
            return $"saves/{Kind}/{Name}.ldsave";
        }

        public static bool TryParseStorageKey(string key, out SaveSlotId id)
        {
            id = default;
            const string prefix = "saves/";
            const string extension = ".ldsave";
            if (key == null ||
                !key.StartsWith(prefix, StringComparison.Ordinal) ||
                !key.EndsWith(extension, StringComparison.Ordinal))
            {
                return false;
            }

            string body = key.Substring(prefix.Length, key.Length - prefix.Length - extension.Length);
            string[] parts = body.Split('/');
            if (parts.Length != 2 ||
                !IsValidToken(parts[0]) ||
                !IsValidToken(parts[1]))
            {
                return false;
            }

            id = new SaveSlotId(parts[0], parts[1]);
            return true;
        }

        private static void ValidateToken(string value, string field)
        {
            if (!IsValidToken(value))
            {
                throw new SaveContextException($"Save slot {field} '{value}' is invalid.");
            }
        }

        private static bool IsValidToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' ||
                    c == '_')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }

    public sealed record SaveSlotHeader(SaveSlotId Id, SaveContextHeader Header);

    public sealed class SaveSlotStore
    {
        private const string SlotPrefix = "saves/";

        private readonly ISaveStorage _storage;
        private readonly SaveContainerCodec _codec;

        public SaveSlotStore(ISaveStorage storage)
            : this(storage, new SaveContainerCodec())
        {
        }

        public SaveSlotStore(ISaveStorage storage, SaveContainerCodec codec)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public void WriteSlot(SaveSlotId id, WorldSaveSnapshot snapshot)
        {
            string finalKey = id.ToStorageKey();
            string tempKey = $"{finalKey}.tmp-{Guid.NewGuid():N}";
            try
            {
                _storage.WriteAllBytes(tempKey, _codec.Encode(snapshot));
                _storage.CommitTempFile(tempKey, finalKey);
            }
            catch (Exception ex) when (ex is not SaveContextException)
            {
                throw new SaveContextException($"Save slot '{id.Value}' write failed: {ex.Message}");
            }
            finally
            {
                _storage.Delete(tempKey);
            }
        }

        public WorldSaveSnapshot ReadSlot(SaveSlotId id)
        {
            string key = id.ToStorageKey();
            if (!_storage.Exists(key))
            {
                throw new SaveContextException($"Save slot '{id.Value}' does not exist.");
            }

            return _codec.Decode(_storage.ReadAllBytes(key));
        }

        public void DeleteSlot(SaveSlotId id)
        {
            _storage.Delete(id.ToStorageKey());
        }

        public IReadOnlyList<SaveSlotHeader> ListSlots()
        {
            var headers = new List<SaveSlotHeader>();
            foreach (string key in _storage.ListFileKeys(SlotPrefix).OrderBy(key => key, StringComparer.Ordinal))
            {
                if (!SaveSlotId.TryParseStorageKey(key, out SaveSlotId id))
                {
                    continue;
                }

                headers.Add(new SaveSlotHeader(id, _codec.ReadHeader(_storage.ReadAllBytes(key))));
            }

            return headers;
        }
    }

    public sealed class AutosaveSlotPolicy
    {
        private long _nextSequence = 1;

        public AutosaveSlotPolicy(int retentionCount)
        {
            if (retentionCount <= 0) throw new ArgumentOutOfRangeException(nameof(retentionCount));
            RetentionCount = retentionCount;
        }

        public int RetentionCount { get; }

        public SaveSlotId WriteAutosave(SaveSlotStore store, WorldSaveSnapshot snapshot)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            EnsureNextSequence(store);
            SaveSlotId id = SaveSlotId.Autosave(_nextSequence.ToString("D10"));
            _nextSequence++;
            store.WriteSlot(id, snapshot);
            Prune(store);
            return id;
        }

        private void EnsureNextSequence(SaveSlotStore store)
        {
            long max = 0;
            foreach (SaveSlotHeader header in store.ListSlots())
            {
                if (!string.Equals(header.Id.Kind, "autosave", StringComparison.Ordinal))
                {
                    continue;
                }

                if (long.TryParse(header.Id.Name, out long sequence) && sequence > max)
                {
                    max = sequence;
                }
            }

            if (_nextSequence <= max)
            {
                _nextSequence = max + 1;
            }
        }

        private void Prune(SaveSlotStore store)
        {
            SaveSlotHeader[] autosaves = store.ListSlots()
                .Where(header => string.Equals(header.Id.Kind, "autosave", StringComparison.Ordinal))
                .OrderByDescending(header => header.Header.CreatedUtc)
                .ThenByDescending(header => header.Id.Name, StringComparer.Ordinal)
                .ToArray();

            for (int i = RetentionCount; i < autosaves.Length; i++)
            {
                store.DeleteSlot(autosaves[i].Id);
            }
        }
    }
}
