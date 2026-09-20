using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;

namespace Ludots.Core.UI.ProductionOverview
{
    /// <summary>
    /// Installs and looks up production overview profiles (WPK-4). Missing command panel source,
    /// queue source kind, worker match refs, or profile ids fail fast at install.
    /// </summary>
    public sealed class ProductionOverviewProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly IEntityCommandPanelSourceRegistry? _commandPanelSources;
        private readonly OrderTypeRegistry? _orderTypes;
        private readonly Func<string, bool>? _isDisplayTokenRegistered;
        private ProductionOverviewProfile?[] _profiles = new ProductionOverviewProfile?[8];

        public ProductionOverviewProfileRegistry(
            StringIntRegistry profileIdRegistry,
            IEntityCommandPanelSourceRegistry? commandPanelSources = null,
            OrderTypeRegistry? orderTypes = null,
            Func<string, bool>? isDisplayTokenRegistered = null)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _commandPanelSources = commandPanelSources;
            _orderTypes = orderTypes;
            _isDisplayTokenRegistered = isDisplayTokenRegistered;
        }

        public StringIntRegistry ProfileIdRegistry => _profileIds;

        public void Install(ProductionOverviewProfilesConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            ProductionOverviewProfileConfigLoader.Validate(config, nameof(ProductionOverviewProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        public bool TryGet(string profileId, out ProductionOverviewProfile profile)
        {
            profile = null!;
            if (!_profileIds.TryGetId(profileId, out int id) || !IsInstalled(id))
            {
                return false;
            }

            profile = _profiles[id]!;
            return true;
        }

        public ProductionOverviewProfile Require(string profileId)
        {
            if (!TryGet(profileId, out ProductionOverviewProfile profile))
            {
                throw new InvalidOperationException($"Production overview profile '{profileId}' is not installed.");
            }

            return profile;
        }

        public IReadOnlyList<ProductionOverviewProfile> CopyInstalled()
        {
            var list = new List<ProductionOverviewProfile>();
            for (int i = 1; i < _profiles.Length; i++)
            {
                if (_profiles[i] != null)
                {
                    list.Add(_profiles[i]!);
                }
            }

            return list;
        }

        private void InstallProfile(ProductionOverviewProfileDefinition definition)
        {
            ProductionOverviewSourceKind sourceKind = ParseSourceKind(definition.Id, definition.SourceKind);
            ProductionQueueSourceKind queueSourceKind = ParseQueueSourceKind(definition.Id, definition.QueueSourceKind);
            ValidateSourceRequirements(definition, sourceKind);
            ValidateCommandPanelSource(definition);
            ValidateQueueSource(definition, queueSourceKind);

            var buckets = new List<ProductionWorkerBucket>(definition.WorkerBuckets?.Count ?? 0);
            if (definition.WorkerBuckets != null)
            {
                if (definition.WorkerBuckets.Count > 0 && string.IsNullOrWhiteSpace(definition.WorkerCollectionKey))
                {
                    throw new InvalidOperationException(
                        $"Production overview profile '{definition.Id}' declares workerBuckets but missing workerCollectionKey.");
                }

                for (int i = 0; i < definition.WorkerBuckets.Count; i++)
                {
                    buckets.Add(ValidateAndCreateBucket(definition.Id, definition.WorkerBuckets[i]));
                }

                buckets.Sort(static (a, b) =>
                {
                    int cmp = a.SortOrder.CompareTo(b.SortOrder);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.BucketId, b.BucketId);
                });
            }

            int id = _profileIds.Register(definition.Id);
            if (id >= _profiles.Length)
            {
                Array.Resize(ref _profiles, Math.Max(id + 1, _profiles.Length * 2));
            }

            if (_profiles[id] != null)
            {
                throw new InvalidOperationException($"Production overview profile '{definition.Id}' is already installed.");
            }

            _profiles[id] = new ProductionOverviewProfile(
                definition.Id,
                sourceKind,
                definition.SourceRef ?? string.Empty,
                definition.CommandPanelSourceId,
                queueSourceKind,
                definition.WorkerCollectionKey ?? string.Empty,
                definition.Topic ?? string.Empty,
                buckets);
        }

        private void ValidateSourceRequirements(
            ProductionOverviewProfileDefinition definition,
            ProductionOverviewSourceKind sourceKind)
        {
            if (sourceKind is ProductionOverviewSourceKind.EntityCollection
                or ProductionOverviewSourceKind.ControlPlaneView
                or ProductionOverviewSourceKind.SolePossessedRep)
            {
                if (string.IsNullOrWhiteSpace(definition.SourceRef))
                {
                    throw new InvalidOperationException(
                        $"Production overview profile '{definition.Id}' sourceKind '{definition.SourceKind}' requires sourceRef (collection/query key).");
                }
            }
        }

        private void ValidateCommandPanelSource(ProductionOverviewProfileDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.CommandPanelSourceId))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{definition.Id}' requires commandPanelSourceId.");
            }

            if (_commandPanelSources != null &&
                !_commandPanelSources.TryGet(definition.CommandPanelSourceId, out _))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{definition.Id}' references unknown command panel source '{definition.CommandPanelSourceId}'.");
            }
        }

        private void ValidateQueueSource(
            ProductionOverviewProfileDefinition definition,
            ProductionQueueSourceKind queueSourceKind)
        {
            if (queueSourceKind == ProductionQueueSourceKind.OrderBuffer && _orderTypes == null)
            {
                // OrderBuffer projection can still run without registry for stage labels via type id,
                // but install-time orderType worker buckets need it. Queue-only profiles are allowed.
            }
        }

        private ProductionWorkerBucket ValidateAndCreateBucket(
            string profileId,
            ProductionWorkerBucketDefinition definition)
        {
            ProductionWorkerMatchKind matchKind = ParseMatchKind(profileId, definition.BucketId, definition.MatchKind);
            string matchRef = definition.MatchRef?.Trim() ?? string.Empty;

            if (_isDisplayTokenRegistered != null && !_isDisplayTokenRegistered(definition.DisplayTokenId))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' references unknown display token '{definition.DisplayTokenId}'.");
            }

            switch (matchKind)
            {
                case ProductionWorkerMatchKind.Tag:
                    RequireMatchRef(profileId, definition.BucketId, matchRef);
                    if (TagRegistry.GetId(matchRef) == TagRegistry.InvalidId)
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' references unknown tag '{matchRef}'.");
                    }

                    break;

                case ProductionWorkerMatchKind.OrderType:
                    RequireMatchRef(profileId, definition.BucketId, matchRef);
                    if (_orderTypes == null)
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' requires OrderTypeRegistry for orderType '{matchRef}'.");
                    }

                    if (!_orderTypes.TryGetId(matchRef, out _))
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' references unknown order type '{matchRef}'.");
                    }

                    break;

                case ProductionWorkerMatchKind.AttributePositive:
                    RequireMatchRef(profileId, definition.BucketId, matchRef);
                    if (AttributeRegistry.GetId(matchRef) == AttributeRegistry.InvalidId)
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' references unknown attribute '{matchRef}'.");
                    }

                    break;

                case ProductionWorkerMatchKind.Idle:
                    if (!string.IsNullOrWhiteSpace(matchRef))
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' idle matchKind must not declare matchRef.");
                    }

                    break;

                default:
                    throw new InvalidOperationException(
                        $"Production overview profile '{profileId}' worker bucket '{definition.BucketId}' has unsupported matchKind '{definition.MatchKind}'.");
            }

            return new ProductionWorkerBucket(
                definition.BucketId,
                definition.DisplayTokenId,
                matchKind,
                matchRef,
                definition.SortOrder);
        }

        private static void RequireMatchRef(string profileId, string bucketId, string matchRef)
        {
            if (string.IsNullOrWhiteSpace(matchRef))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profileId}' worker bucket '{bucketId}' requires matchRef.");
            }
        }

        private static ProductionOverviewSourceKind ParseSourceKind(string profileId, string value)
        {
            return value switch
            {
                ProductionOverviewSourceKindIds.SolePossessedRep => ProductionOverviewSourceKind.SolePossessedRep,
                ProductionOverviewSourceKindIds.ExplicitEntity => ProductionOverviewSourceKind.ExplicitEntity,
                ProductionOverviewSourceKindIds.EntityCollection => ProductionOverviewSourceKind.EntityCollection,
                ProductionOverviewSourceKindIds.ControlPlaneView => ProductionOverviewSourceKind.ControlPlaneView,
                _ => throw new InvalidOperationException(
                    $"Production overview profile '{profileId}' has unknown sourceKind '{value}'.")
            };
        }

        private static ProductionQueueSourceKind ParseQueueSourceKind(string profileId, string value)
        {
            return value switch
            {
                ProductionQueueSourceKindIds.CommandPanelSupplemental => ProductionQueueSourceKind.CommandPanelSupplemental,
                ProductionQueueSourceKindIds.OrderBuffer => ProductionQueueSourceKind.OrderBuffer,
                _ => throw new InvalidOperationException(
                    $"Production overview profile '{profileId}' has unknown queueSourceKind '{value}'.")
            };
        }

        private static ProductionWorkerMatchKind ParseMatchKind(string profileId, string bucketId, string value)
        {
            return value switch
            {
                ProductionWorkerMatchKindIds.Tag => ProductionWorkerMatchKind.Tag,
                ProductionWorkerMatchKindIds.OrderType => ProductionWorkerMatchKind.OrderType,
                ProductionWorkerMatchKindIds.AttributePositive => ProductionWorkerMatchKind.AttributePositive,
                ProductionWorkerMatchKindIds.Idle => ProductionWorkerMatchKind.Idle,
                _ => throw new InvalidOperationException(
                    $"Production overview profile '{profileId}' worker bucket '{bucketId}' has unknown matchKind '{value}'.")
            };
        }
    }
}
