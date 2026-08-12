using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.UI.ProductionOverview
{
    /// <summary>
    /// How production queue rows are read. Always a projection over existing command/status/queue
    /// or OrderBuffer — never a parallel production store.
    /// </summary>
    public enum ProductionQueueSourceKind : byte
    {
        /// <summary>
        /// <see cref="Ludots.Core.UI.EntityCommandPanels.IEntityCommandPanelSupplementalSource"/>
        /// status + queue items from the declared command panel source.
        /// </summary>
        CommandPanelSupplemental = 0,

        /// <summary>
        /// Direct <c>OrderBuffer</c> projection on collection members (stage/label from order type keys).
        /// </summary>
        OrderBuffer = 1
    }

    /// <summary>How a worker overview bucket matches an entity.</summary>
    public enum ProductionWorkerMatchKind : byte
    {
        /// <summary>Entity has the declared gameplay tag.</summary>
        Tag = 0,

        /// <summary>Active OrderBuffer order type key matches.</summary>
        OrderType = 1,

        /// <summary>Attribute current value is greater than zero.</summary>
        AttributePositive = 2,

        /// <summary>
        /// No active/queued/pending order and none of the other declared non-idle buckets match.
        /// Must be last in evaluation order among buckets that share a collection.
        /// </summary>
        Idle = 3
    }

    /// <summary>JSON root for <c>UI/production_overview_profiles.json</c>.</summary>
    public sealed class ProductionOverviewProfilesConfig
    {
        public List<ProductionOverviewProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One production/worker/queue overview profile. Stable ids only — queue and worker semantics
    /// are data-driven references over existing command/status/queue and entity collections.
    /// </summary>
    public sealed class ProductionOverviewProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string SourceRef { get; set; } = string.Empty;
        public string CommandPanelSourceId { get; set; } = string.Empty;
        public string QueueSourceKind { get; set; } = string.Empty;
        public string WorkerCollectionKey { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public List<ProductionWorkerBucketDefinition> WorkerBuckets { get; set; }
    }

    /// <summary>One worker overview bucket declaration.</summary>
    public sealed class ProductionWorkerBucketDefinition
    {
        public string BucketId { get; set; } = string.Empty;
        public string DisplayTokenId { get; set; } = string.Empty;
        public string MatchKind { get; set; } = string.Empty;
        public string MatchRef { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    /// <summary>Installed production overview profile (validated, immutable).</summary>
    public sealed class ProductionOverviewProfile
    {
        public ProductionOverviewProfile(
            string id,
            ProductionOverviewSourceKind sourceKind,
            string sourceRef,
            string commandPanelSourceId,
            ProductionQueueSourceKind queueSourceKind,
            string workerCollectionKey,
            string topic,
            IReadOnlyList<ProductionWorkerBucket> workerBuckets)
        {
            Id = RequireId(id, nameof(id));
            SourceKind = sourceKind;
            SourceRef = sourceRef?.Trim() ?? string.Empty;
            CommandPanelSourceId = RequireId(commandPanelSourceId, nameof(commandPanelSourceId));
            QueueSourceKind = queueSourceKind;
            WorkerCollectionKey = workerCollectionKey?.Trim() ?? string.Empty;
            Topic = topic?.Trim() ?? string.Empty;
            WorkerBuckets = workerBuckets ?? Array.Empty<ProductionWorkerBucket>();
        }

        public string Id { get; }
        public ProductionOverviewSourceKind SourceKind { get; }
        public string SourceRef { get; }
        public string CommandPanelSourceId { get; }
        public ProductionQueueSourceKind QueueSourceKind { get; }
        public string WorkerCollectionKey { get; }
        public string Topic { get; }
        public IReadOnlyList<ProductionWorkerBucket> WorkerBuckets { get; }

        private static string RequireId(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{paramName} is required.", paramName);
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
            }

            return trimmed;
        }
    }

    public sealed class ProductionWorkerBucket
    {
        public ProductionWorkerBucket(
            string bucketId,
            string displayTokenId,
            ProductionWorkerMatchKind matchKind,
            string matchRef,
            int sortOrder)
        {
            BucketId = RequireId(bucketId, nameof(bucketId));
            DisplayTokenId = RequireId(displayTokenId, nameof(displayTokenId));
            MatchKind = matchKind;
            MatchRef = matchRef?.Trim() ?? string.Empty;
            SortOrder = sortOrder;

            if (matchKind != ProductionWorkerMatchKind.Idle && string.IsNullOrWhiteSpace(MatchRef))
            {
                throw new ArgumentException(
                    $"Worker bucket '{BucketId}' matchKind '{matchKind}' requires matchRef.",
                    nameof(matchRef));
            }
        }

        public string BucketId { get; }
        public string DisplayTokenId { get; }
        public ProductionWorkerMatchKind MatchKind { get; }
        public string MatchRef { get; }
        public int SortOrder { get; }

        private static string RequireId(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{paramName} is required.", paramName);
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
            }

            return trimmed;
        }
    }

    /// <summary>
    /// Candidate source kinds for production/worker overview. Mirrors CommandDeck collection ownership
    /// so the same command-source / control-plane collections can feed both panels.
    /// </summary>
    public enum ProductionOverviewSourceKind : byte
    {
        SolePossessedRep = 0,
        ExplicitEntity = 1,
        EntityCollection = 2,
        ControlPlaneView = 3
    }

    /// <summary>Explicit binding inputs for one production overview projection.</summary>
    public readonly struct ProductionOverviewBindingContext
    {
        public ProductionOverviewBindingContext(
            Entity solePossessedRep,
            Entity focusedEntity,
            Entity collectionOwner,
            string instanceKey)
        {
            SolePossessedRep = solePossessedRep;
            FocusedEntity = focusedEntity;
            CollectionOwner = collectionOwner;
            InstanceKey = instanceKey ?? string.Empty;
        }

        public Entity SolePossessedRep { get; }
        public Entity FocusedEntity { get; }
        public Entity CollectionOwner { get; }
        public string InstanceKey { get; }
    }

    /// <summary>One status/progress row projected from command panel status.</summary>
    public readonly struct ProductionOverviewStatusRow
    {
        public ProductionOverviewStatusRow(
            int ownerEntityId,
            int ownerVersion,
            string label,
            string detail,
            short progressPermille,
            string accentColorHex,
            string blockedReason)
        {
            OwnerEntityId = ownerEntityId;
            OwnerVersion = ownerVersion;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            ProgressPermille = progressPermille;
            AccentColorHex = accentColorHex ?? string.Empty;
            BlockedReason = blockedReason ?? string.Empty;
        }

        public int OwnerEntityId { get; }
        public int OwnerVersion { get; }
        public string Label { get; }
        public string Detail { get; }
        public short ProgressPermille { get; }
        public string AccentColorHex { get; }
        public string BlockedReason { get; }
    }

    /// <summary>One queue item projected from command panel queue or OrderBuffer.</summary>
    public readonly struct ProductionOverviewQueueItem
    {
        public ProductionOverviewQueueItem(
            int ownerEntityId,
            int ownerVersion,
            string stage,
            string label,
            string detail,
            short progressPermille,
            string accentColorHex,
            string blockedReason)
        {
            OwnerEntityId = ownerEntityId;
            OwnerVersion = ownerVersion;
            Stage = stage ?? string.Empty;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            ProgressPermille = progressPermille;
            AccentColorHex = accentColorHex ?? string.Empty;
            BlockedReason = blockedReason ?? string.Empty;
        }

        public int OwnerEntityId { get; }
        public int OwnerVersion { get; }
        public string Stage { get; }
        public string Label { get; }
        public string Detail { get; }
        public short ProgressPermille { get; }
        public string AccentColorHex { get; }
        public string BlockedReason { get; }
    }

    /// <summary>One worker overview bucket count.</summary>
    public readonly struct ProductionOverviewWorkerRow
    {
        public ProductionOverviewWorkerRow(string bucketId, string displayTokenId, int count, int sortOrder)
        {
            BucketId = bucketId ?? string.Empty;
            DisplayTokenId = displayTokenId ?? string.Empty;
            Count = count;
            SortOrder = sortOrder;
        }

        public string BucketId { get; }
        public string DisplayTokenId { get; }
        public int Count { get; }
        public int SortOrder { get; }
    }

    /// <summary>DataPlane payload contract for one production overview revision.</summary>
    public sealed class ProductionOverviewSnapshot
    {
        public ProductionOverviewSnapshot(
            string profileId,
            int ownerEntityId,
            int ownerVersion,
            uint revision,
            IReadOnlyList<ProductionOverviewStatusRow> rows,
            IReadOnlyList<ProductionOverviewQueueItem> queueItems,
            IReadOnlyList<ProductionOverviewWorkerRow> workerRows,
            IReadOnlyList<string> blockedReasons)
        {
            ProfileId = profileId ?? string.Empty;
            OwnerEntityId = ownerEntityId;
            OwnerVersion = ownerVersion;
            Revision = revision;
            Rows = rows ?? Array.Empty<ProductionOverviewStatusRow>();
            QueueItems = queueItems ?? Array.Empty<ProductionOverviewQueueItem>();
            WorkerRows = workerRows ?? Array.Empty<ProductionOverviewWorkerRow>();
            BlockedReasons = blockedReasons ?? Array.Empty<string>();
        }

        public string ProfileId { get; }
        public int OwnerEntityId { get; }
        public int OwnerVersion { get; }
        public uint Revision { get; }
        public IReadOnlyList<ProductionOverviewStatusRow> Rows { get; }
        public IReadOnlyList<ProductionOverviewQueueItem> QueueItems { get; }
        public IReadOnlyList<ProductionOverviewWorkerRow> WorkerRows { get; }
        public IReadOnlyList<string> BlockedReasons { get; }
    }

    public static class ProductionOverviewSourceKindIds
    {
        public const string SolePossessedRep = "solePossessedRep";
        public const string ExplicitEntity = "explicitEntity";
        public const string EntityCollection = "entityCollection";
        public const string ControlPlaneView = "controlPlaneView";
    }

    public static class ProductionQueueSourceKindIds
    {
        public const string CommandPanelSupplemental = "commandPanelSupplemental";
        public const string OrderBuffer = "orderBuffer";
    }

    public static class ProductionWorkerMatchKindIds
    {
        public const string Tag = "tag";
        public const string OrderType = "orderType";
        public const string AttributePositive = "attributePositive";
        public const string Idle = "idle";
    }
}
