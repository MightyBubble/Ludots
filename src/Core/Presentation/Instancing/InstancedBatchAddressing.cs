using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Instancing
{
    public readonly struct InstancedBatchOwnerId : IEquatable<InstancedBatchOwnerId>
    {
        public InstancedBatchOwnerId(int value) => Value = value;
        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(InstancedBatchOwnerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is InstancedBatchOwnerId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct InstancedBatchGroupId : IEquatable<InstancedBatchGroupId>
    {
        public InstancedBatchGroupId(int value) => Value = value;
        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(InstancedBatchGroupId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is InstancedBatchGroupId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct InstancedBatchBucketId : IEquatable<InstancedBatchBucketId>
    {
        public InstancedBatchBucketId(int value) => Value = value;
        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(InstancedBatchBucketId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is InstancedBatchBucketId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct InstancedBatchSpanId : IEquatable<InstancedBatchSpanId>
    {
        public InstancedBatchSpanId(int value) => Value = value;
        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(InstancedBatchSpanId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is InstancedBatchSpanId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct InstancedBatchAddress : IEquatable<InstancedBatchAddress>
    {
        public InstancedBatchAddress(
            int batchId,
            InstancedBatchOwnerId owner,
            InstancedBatchGroupId group,
            InstancedBatchBucketId bucket,
            InstancedBatchSpanId span)
        {
            BatchId = batchId;
            Owner = owner;
            Group = group;
            Bucket = bucket;
            Span = span;
        }

        public int BatchId { get; }
        public InstancedBatchOwnerId Owner { get; }
        public InstancedBatchGroupId Group { get; }
        public InstancedBatchBucketId Bucket { get; }
        public InstancedBatchSpanId Span { get; }
        public bool IsValid => BatchId > 0 && Owner.IsValid && Group.IsValid && Bucket.IsValid && Span.IsValid;

        public bool Equals(InstancedBatchAddress other)
        {
            return BatchId == other.BatchId &&
                   Owner.Equals(other.Owner) &&
                   Group.Equals(other.Group) &&
                   Bucket.Equals(other.Bucket) &&
                   Span.Equals(other.Span);
        }

        public override bool Equals(object? obj) => obj is InstancedBatchAddress other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(BatchId, Owner, Group, Bucket, Span);
    }

    public readonly struct InstancedBatchAddressGroupInput
    {
        public InstancedBatchAddressGroupInput(string groupId, string bucketId, string spanId)
        {
            GroupId = groupId ?? string.Empty;
            BucketId = bucketId ?? string.Empty;
            SpanId = spanId ?? string.Empty;
        }

        public string GroupId { get; }
        public string BucketId { get; }
        public string SpanId { get; }
    }

    public sealed class InstancedBatchAddressTable
    {
        private readonly Dictionary<string, InstancedBatchGroupId> _groups;
        private readonly Dictionary<string, InstancedBatchBucketId> _buckets;
        private readonly Dictionary<string, InstancedBatchSpanId> _spans;
        private readonly Dictionary<AddressKey, InstancedBatchAddress> _addresses;
        private readonly InstancedBatchAddressGroup[] _orderedGroups;

        private InstancedBatchAddressTable(
            int batchId,
            string ownerStableId,
            InstancedBatchOwnerId owner,
            Dictionary<string, InstancedBatchGroupId> groups,
            Dictionary<string, InstancedBatchBucketId> buckets,
            Dictionary<string, InstancedBatchSpanId> spans,
            Dictionary<AddressKey, InstancedBatchAddress> addresses,
            InstancedBatchAddressGroup[] orderedGroups)
        {
            BatchId = batchId;
            OwnerStableId = ownerStableId;
            Owner = owner;
            _groups = groups;
            _buckets = buckets;
            _spans = spans;
            _addresses = addresses;
            _orderedGroups = orderedGroups;
        }

        public int BatchId { get; }
        public string OwnerStableId { get; }
        public InstancedBatchOwnerId Owner { get; }
        public int GroupCount => _orderedGroups.Length;
        public ReadOnlySpan<InstancedBatchAddressGroup> Groups => _orderedGroups;

        public static InstancedBatchAddressTable Build(
            int batchId,
            string ownerStableId,
            ReadOnlySpan<InstancedBatchAddressGroupInput> groupInputs)
        {
            if (batchId <= 0)
            {
                throw new InvalidOperationException("Instanced batch address table requires a positive batch id.");
            }

            RequireCanonicalId(ownerStableId, "instanced batch ownerStableId");
            if (groupInputs.Length == 0)
            {
                throw new InvalidOperationException("Instanced batch address table requires at least one group.");
            }

            var groups = new Dictionary<string, InstancedBatchGroupId>(groupInputs.Length, StringComparer.Ordinal);
            var buckets = new Dictionary<string, InstancedBatchBucketId>(groupInputs.Length, StringComparer.Ordinal);
            var spans = new Dictionary<string, InstancedBatchSpanId>(groupInputs.Length, StringComparer.Ordinal);
            var addresses = new Dictionary<AddressKey, InstancedBatchAddress>(groupInputs.Length);
            var ordered = new InstancedBatchAddressGroup[groupInputs.Length];
            var owner = new InstancedBatchOwnerId(1);
            for (int i = 0; i < groupInputs.Length; i++)
            {
                InstancedBatchAddressGroupInput input = groupInputs[i];
                RequireCanonicalId(input.GroupId, $"instanced batch group[{i}].id");
                RequireCanonicalId(input.BucketId, $"instanced batch group[{i}].bucketId");
                RequireCanonicalId(input.SpanId, $"instanced batch group[{i}].instanceSpanId");

                if (groups.ContainsKey(input.GroupId))
                {
                    throw new InvalidOperationException($"Instanced batch group id '{input.GroupId}' is duplicated.");
                }

                if (buckets.ContainsKey(input.BucketId))
                {
                    throw new InvalidOperationException($"Instanced batch bucket id '{input.BucketId}' is duplicated.");
                }

                if (spans.ContainsKey(input.SpanId))
                {
                    throw new InvalidOperationException($"Instanced batch instance span id '{input.SpanId}' is duplicated.");
                }

                var groupId = new InstancedBatchGroupId(i + 1);
                var bucketId = new InstancedBatchBucketId(i + 1);
                var spanId = new InstancedBatchSpanId(i + 1);
                groups.Add(input.GroupId, groupId);
                buckets.Add(input.BucketId, bucketId);
                spans.Add(input.SpanId, spanId);
                addresses.Add(
                    new AddressKey(input.GroupId, input.BucketId, input.SpanId),
                    new InstancedBatchAddress(batchId, owner, groupId, bucketId, spanId));
                ordered[i] = new InstancedBatchAddressGroup(input.GroupId, input.BucketId, input.SpanId, groupId, bucketId, spanId);
            }

            return new InstancedBatchAddressTable(
                batchId,
                ownerStableId,
                owner,
                groups,
                buckets,
                spans,
                addresses,
                ordered);
        }

        public bool TryResolve(
            string groupId,
            string bucketId,
            string spanId,
            out InstancedBatchAddress address)
        {
            if (_addresses.TryGetValue(new AddressKey(groupId ?? string.Empty, bucketId ?? string.Empty, spanId ?? string.Empty), out address))
            {
                return true;
            }

            address = default;
            return false;
        }

        public InstancedBatchAddress Resolve(string groupId, string bucketId, string spanId)
        {
            if (!TryResolve(groupId, bucketId, spanId, out InstancedBatchAddress address))
            {
                throw new InvalidOperationException(
                    $"Instanced batch address target group='{groupId}' bucket='{bucketId}' span='{spanId}' is unknown.");
            }

            return address;
        }

        internal static void RequireCanonicalId(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must be a non-empty id.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }
        }

        private readonly struct AddressKey : IEquatable<AddressKey>
        {
            private readonly string _groupId;
            private readonly string _bucketId;
            private readonly string _spanId;

            public AddressKey(string groupId, string bucketId, string spanId)
            {
                _groupId = groupId;
                _bucketId = bucketId;
                _spanId = spanId;
            }

            public bool Equals(AddressKey other)
            {
                return string.Equals(_groupId, other._groupId, StringComparison.Ordinal) &&
                       string.Equals(_bucketId, other._bucketId, StringComparison.Ordinal) &&
                       string.Equals(_spanId, other._spanId, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj) => obj is AddressKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(_groupId),
                StringComparer.Ordinal.GetHashCode(_bucketId),
                StringComparer.Ordinal.GetHashCode(_spanId));
        }
    }

    public readonly struct InstancedBatchAddressGroup
    {
        public InstancedBatchAddressGroup(
            string groupKey,
            string bucketKey,
            string spanKey,
            InstancedBatchGroupId group,
            InstancedBatchBucketId bucket,
            InstancedBatchSpanId span)
        {
            GroupKey = groupKey;
            BucketKey = bucketKey;
            SpanKey = spanKey;
            Group = group;
            Bucket = bucket;
            Span = span;
        }

        public string GroupKey { get; }
        public string BucketKey { get; }
        public string SpanKey { get; }
        public InstancedBatchGroupId Group { get; }
        public InstancedBatchBucketId Bucket { get; }
        public InstancedBatchSpanId Span { get; }
    }
}
