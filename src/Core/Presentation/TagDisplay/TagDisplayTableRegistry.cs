using System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.TagDisplay
{
    /// <summary>
    /// Dense tagId→presentationTokenId tables for panel/state display.
    /// Hot path is O(1) array index; no string work at lookup time.
    /// </summary>
    public sealed class TagDisplayTableRegistry
    {
        public const string UnknownTableError = "GAS.TAG_DISPLAY.ERR.UnknownTable";
        public const string MappingMissingError = "GAS.TAG_DISPLAY.ERR.MappingMissing";
        public const string EmptyMaskError = "GAS.TAG_DISPLAY.ERR.EmptyMask";
        public const string FrozenError = "GAS.TAG_DISPLAY.ERR.Frozen";

        private readonly StringIntRegistry _tableIds;
        private GameplayTagContainer[] _masks;
        private int[][] _tokenByTagId;
        private bool _frozen;

        public TagDisplayTableRegistry(int initialTableCapacity = 8)
        {
            _tableIds = new StringIntRegistry(
                capacity: Math.Max(4, initialTableCapacity),
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            _masks = new GameplayTagContainer[Math.Max(4, initialTableCapacity)];
            _tokenByTagId = new int[Math.Max(4, initialTableCapacity)][];
        }

        public bool IsFrozen => _frozen;

        public int RegisterTable(string tableId, in GameplayTagContainer mask, ReadOnlySpan<(int TagId, int TokenId)> entries)
        {
            if (_frozen)
            {
                throw new InvalidOperationException(FrozenError);
            }

            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("tableId is required.", nameof(tableId));
            }

            if (mask.IsEmpty)
            {
                throw new InvalidOperationException($"{EmptyMaskError}: table '{tableId}' mask is empty.");
            }

            if (_tableIds.TryGetId(tableId, out _))
            {
                throw new InvalidOperationException(
                    $"Tag display table '{tableId}' is already registered.");
            }

            int id = _tableIds.Register(tableId);
            EnsureTableSlot(id);

            _masks[id] = mask;
            int[] tokens = new int[GameplayTagContainer.MAX_TAG_ID + 1];
            for (int i = 0; i < entries.Length; i++)
            {
                int tagId = entries[i].TagId;
                int tokenId = entries[i].TokenId;
                if (tagId <= 0 || tagId > GameplayTagContainer.MAX_TAG_ID)
                {
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Invalid tagId {tagId} in table '{tableId}'.");
                }

                if (tokenId <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Invalid tokenId {tokenId} for tag {tagId} in table '{tableId}'.");
                }

                if (!mask.HasTag(tagId))
                {
                    throw new InvalidOperationException(
                        $"Tag display table '{tableId}' entry tagId {tagId} is not covered by maskTags.");
                }

                if (tokens[tagId] != 0)
                {
                    throw new InvalidOperationException(
                        $"Tag display table '{tableId}' has duplicate mapping for tagId {tagId}.");
                }

                tokens[tagId] = tokenId;
            }

            _tokenByTagId[id] = tokens;
            return id;
        }

        public void Freeze() => _frozen = true;

        public int GetTableId(string tableId)
        {
            if (!_tableIds.TryGetId(tableId, out int id) || id <= 0)
            {
                throw new InvalidOperationException($"{UnknownTableError}: '{tableId}'.");
            }

            return id;
        }

        public bool TryGetTableId(string tableId, out int id) => _tableIds.TryGetId(tableId, out id) && id > 0;

        public GameplayTagContainer GetMask(int tableId)
        {
            RequireTable(tableId);
            return _masks[tableId];
        }

        public int LookupToken(int tableId, int tagId)
        {
            RequireTable(tableId);
            if (tagId <= 0 || tagId > GameplayTagContainer.MAX_TAG_ID)
            {
                throw new InvalidOperationException(
                    $"{MappingMissingError}: tableId={tableId} tagId={tagId}.");
            }

            int tokenId = _tokenByTagId[tableId][tagId];
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"{MappingMissingError}: tableId={tableId} tagId={tagId}.");
            }

            return tokenId;
        }

        private void RequireTable(int tableId)
        {
            if (tableId <= 0 ||
                tableId >= _tokenByTagId.Length ||
                _tokenByTagId[tableId] == null)
            {
                throw new InvalidOperationException($"{UnknownTableError}: id={tableId}.");
            }
        }

        private void EnsureTableSlot(int id)
        {
            if (id < _tokenByTagId.Length)
            {
                return;
            }

            int newSize = Math.Max(_tokenByTagId.Length * 2, id + 1);
            Array.Resize(ref _masks, newSize);
            Array.Resize(ref _tokenByTagId, newSize);
        }
    }

    public enum TagSelectPolicy : byte
    {
        /// <summary>Intersection must contain exactly one tag; otherwise throw.</summary>
        RequireOne = 0,

        /// <summary>Zero → tagId 0; multiple still throws.</summary>
        AllowNone = 1,

        /// <summary>Multiple → lowest tag id (non-UI / debug).</summary>
        LowestId = 2,
    }
}
