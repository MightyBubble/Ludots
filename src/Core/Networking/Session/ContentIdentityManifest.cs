using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Ludots.Core.Networking.Session
{
    /// <summary>
    /// Gameplay-relevant content categories that participate in the handshake identity.
    /// Category digests are path-independent: they hash ids/relative paths and bytes, never host-local roots.
    /// </summary>
    public enum ContentIdentityCategory : byte
    {
        None = 0,
        Protocol = 1,
        Build = 2,
        ModAssemblies = 3,
        ModAssets = 4,
        Config = 5,
        Maps = 6,
        Registries = 7,
        ReplicationSchema = 8,
    }

    public readonly struct ContentIdentityItem : IEquatable<ContentIdentityItem>
    {
        public ContentIdentityItem(ContentIdentityCategory category, string key, ContentFingerprint digest)
        {
            if (category == ContentIdentityCategory.None)
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Content identity item key is required.", nameof(key));
            }

            if (digest.IsEmpty)
            {
                throw new ArgumentException("Content identity item digest must be non-empty.", nameof(digest));
            }

            Category = category;
            Key = key;
            Digest = digest;
        }

        public ContentIdentityCategory Category { get; }
        public string Key { get; }
        public ContentFingerprint Digest { get; }

        public bool Equals(ContentIdentityItem other) =>
            Category == other.Category &&
            string.Equals(Key, other.Key, StringComparison.Ordinal) &&
            Digest == other.Digest;

        public override bool Equals(object? obj) => obj is ContentIdentityItem other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Category, Key, Digest);
    }

    public readonly struct ContentMismatchDetail : IEquatable<ContentMismatchDetail>
    {
        public ContentMismatchDetail(ContentIdentityCategory category, string itemKey)
        {
            if (category == ContentIdentityCategory.None)
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }

            Category = category;
            ItemKey = itemKey ?? string.Empty;
        }

        public ContentIdentityCategory Category { get; }
        public string ItemKey { get; }
        public bool HasItemKey => !string.IsNullOrEmpty(ItemKey);

        public bool Equals(ContentMismatchDetail other) =>
            Category == other.Category &&
            string.Equals(ItemKey, other.ItemKey, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ContentMismatchDetail other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Category, ItemKey);

        public override string ToString() =>
            HasItemKey ? $"{Category}:{ItemKey}" : Category.ToString();
    }

    /// <summary>
    /// Canonical multi-category content identity. Aggregate fingerprint is SHA-256 over the ordered
    /// category digest table; it is identical across install roots when relative content is identical.
    /// </summary>
    public sealed class ContentIdentityManifest
    {
        public const int CategoryCount = 8;

        private static readonly ContentIdentityCategory[] CategoryOrder =
        {
            ContentIdentityCategory.Protocol,
            ContentIdentityCategory.Build,
            ContentIdentityCategory.ModAssemblies,
            ContentIdentityCategory.ModAssets,
            ContentIdentityCategory.Config,
            ContentIdentityCategory.Maps,
            ContentIdentityCategory.Registries,
            ContentIdentityCategory.ReplicationSchema,
        };

        private readonly ContentFingerprint[] _categoryDigests;
        private readonly ContentIdentityItem[] _items;

        private ContentIdentityManifest(ContentFingerprint aggregate, ContentFingerprint[] categoryDigests, ContentIdentityItem[] items)
        {
            Aggregate = aggregate;
            _categoryDigests = categoryDigests;
            _items = items;
        }

        public ContentFingerprint Aggregate { get; }

        public ReadOnlySpan<ContentFingerprint> CategoryDigests => _categoryDigests;

        public ReadOnlySpan<ContentIdentityItem> Items => _items;

        public ContentFingerprint GetCategoryDigest(ContentIdentityCategory category)
        {
            int index = IndexOf(category);
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }

            return _categoryDigests[index];
        }

        public static ContentIdentityManifest Create(IReadOnlyList<ContentIdentityItem> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items.Count == 0)
            {
                throw new ArgumentException("Content identity requires at least one item.", nameof(items));
            }

            var sorted = new ContentIdentityItem[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                sorted[i] = items[i];
            }

            Array.Sort(sorted, CompareItems);
            for (int i = 1; i < sorted.Length; i++)
            {
                if (CompareItems(sorted[i - 1], sorted[i]) == 0)
                {
                    throw new InvalidOperationException(
                        $"Duplicate content identity item '{sorted[i].Category}:{sorted[i].Key}'.");
                }
            }

            var categoryDigests = new ContentFingerprint[CategoryCount];
            var builder = new StringBuilder(256);
            for (int categoryIndex = 0; categoryIndex < CategoryOrder.Length; categoryIndex++)
            {
                ContentIdentityCategory category = CategoryOrder[categoryIndex];
                builder.Clear();
                builder.Append("ludots.content.category.v1\n");
                builder.Append((byte)category).Append('\n');
                int itemCount = 0;
                for (int i = 0; i < sorted.Length; i++)
                {
                    if (sorted[i].Category != category)
                    {
                        continue;
                    }

                    builder.Append(sorted[i].Key).Append('=').Append(sorted[i].Digest.ToHexString()).Append('\n');
                    itemCount++;
                }

                if (itemCount == 0)
                {
                    throw new InvalidOperationException(
                        $"Content identity category '{category}' has no items.");
                }

                categoryDigests[categoryIndex] = ContentFingerprintBuilder.FromCanonicalBytes(
                    Encoding.UTF8.GetBytes(builder.ToString()));
            }

            builder.Clear();
            builder.Append("ludots.content.aggregate.v1\n");
            for (int i = 0; i < categoryDigests.Length; i++)
            {
                builder.Append((byte)CategoryOrder[i]).Append('=').Append(categoryDigests[i].ToHexString()).Append('\n');
            }

            ContentFingerprint aggregate = ContentFingerprintBuilder.FromCanonicalBytes(
                Encoding.UTF8.GetBytes(builder.ToString()));
            return new ContentIdentityManifest(aggregate, categoryDigests, sorted);
        }

        public static bool TryDiff(
            ContentIdentityManifest left,
            ContentIdentityManifest right,
            out ContentMismatchDetail detail)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            if (left.Aggregate == right.Aggregate)
            {
                detail = default;
                return false;
            }

            for (int categoryIndex = 0; categoryIndex < CategoryOrder.Length; categoryIndex++)
            {
                if (left._categoryDigests[categoryIndex] == right._categoryDigests[categoryIndex])
                {
                    continue;
                }

                ContentIdentityCategory category = CategoryOrder[categoryIndex];
                if (TryFindFirstItemMismatch(left._items, right._items, category, out string itemKey))
                {
                    detail = new ContentMismatchDetail(category, itemKey);
                    return true;
                }

                detail = new ContentMismatchDetail(category, string.Empty);
                return true;
            }

            detail = new ContentMismatchDetail(ContentIdentityCategory.Protocol, string.Empty);
            return true;
        }

        public static ContentIdentityCategory CategoryAt(int index)
        {
            if ((uint)index >= (uint)CategoryOrder.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return CategoryOrder[index];
        }

        public static int IndexOf(ContentIdentityCategory category)
        {
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                if (CategoryOrder[i] == category)
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsKnownCategory(ContentIdentityCategory category) =>
            IndexOf(category) >= 0;

        public static ulong HashItemKey(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey))
            {
                return 0;
            }

            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(itemKey), digest);
            return BitConverter.ToUInt64(digest);
        }

        private static bool TryFindFirstItemMismatch(
            ContentIdentityItem[] left,
            ContentIdentityItem[] right,
            ContentIdentityCategory category,
            out string itemKey)
        {
            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length || rightIndex < right.Length)
            {
                while (leftIndex < left.Length && left[leftIndex].Category != category)
                {
                    leftIndex++;
                }

                while (rightIndex < right.Length && right[rightIndex].Category != category)
                {
                    rightIndex++;
                }

                if (leftIndex >= left.Length && rightIndex >= right.Length)
                {
                    break;
                }

                if (leftIndex >= left.Length)
                {
                    itemKey = right[rightIndex].Key;
                    return true;
                }

                if (rightIndex >= right.Length)
                {
                    itemKey = left[leftIndex].Key;
                    return true;
                }

                int keyCompare = string.CompareOrdinal(left[leftIndex].Key, right[rightIndex].Key);
                if (keyCompare < 0)
                {
                    itemKey = left[leftIndex].Key;
                    return true;
                }

                if (keyCompare > 0)
                {
                    itemKey = right[rightIndex].Key;
                    return true;
                }

                if (left[leftIndex].Digest != right[rightIndex].Digest)
                {
                    itemKey = left[leftIndex].Key;
                    return true;
                }

                leftIndex++;
                rightIndex++;
            }

            itemKey = string.Empty;
            return false;
        }

        private static int CompareItems(ContentIdentityItem left, ContentIdentityItem right)
        {
            int byCategory = ((byte)left.Category).CompareTo((byte)right.Category);
            if (byCategory != 0)
            {
                return byCategory;
            }

            return string.CompareOrdinal(left.Key, right.Key);
        }
    }
}
