using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.FlowField;

namespace Ludots.Core.Navigation2D.Runtime
{
    public readonly record struct Navigation2DFlowDomainRequest(
        int OwnerId,
        Fix64Vec2 FocusCm,
        int ProfileIndex,
        int Priority);

    public readonly record struct Navigation2DFlowDomainLeaseSnapshot(
        int FlowId,
        int OwnerId,
        int ProfileIndex,
        int CenterTileX,
        int CenterTileY,
        int MinTileX,
        int MinTileY,
        int MaxTileX,
        int MaxTileY,
        int ExpireTick,
        bool Occupied);

    public sealed class Navigation2DFlowDomainPool
    {
        private readonly CrowdSurface2D _surface;
        private readonly CrowdFlow2D[] _flows;
        private readonly Navigation2DFlowStreamingConfig _baseStreaming;
        private readonly Navigation2DFlowCrowdConfig _baseCrowd;
        private readonly Navigation2DFlowDomainPoolConfig _config;
        private readonly Navigation2DFlowStreamingConfig[] _slotStreamingConfigs;
        private readonly FlowDomainProfileDefinition[] _profiles;
        private readonly Dictionary<string, int> _profileIndexById;
        private readonly Dictionary<int, int> _slotByOwnerId;
        private readonly Dictionary<int, int> _assignmentByOwnerId;
        private readonly Dictionary<int, int> _requestIndexByOwnerId;
        private readonly FlowDomainSlot[] _slots;
        private byte[] _requestMatched = Array.Empty<byte>();
        private byte[] _slotReserved = Array.Empty<byte>();
        private int[] _sortedRequestIndices = Array.Empty<int>();

        public Navigation2DFlowDomainPool(
            CrowdSurface2D surface,
            CrowdFlow2D[] flows,
            Navigation2DFlowStreamingConfig baseStreaming,
            Navigation2DFlowCrowdConfig baseCrowd,
            Navigation2DFlowDomainPoolConfig config)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _flows = flows ?? throw new ArgumentNullException(nameof(flows));
            _baseStreaming = baseStreaming ?? throw new ArgumentNullException(nameof(baseStreaming));
            _baseCrowd = baseCrowd ?? throw new ArgumentNullException(nameof(baseCrowd));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (_flows.Length <= 0)
            {
                throw new InvalidOperationException("Navigation2DFlowDomainPool requires at least one flow slot.");
            }

            _profileIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
            _slotByOwnerId = new Dictionary<int, int>(_flows.Length);
            _assignmentByOwnerId = new Dictionary<int, int>(_flows.Length);
            _requestIndexByOwnerId = new Dictionary<int, int>(Math.Max(4, _flows.Length));
            _slots = new FlowDomainSlot[_flows.Length];
            _slotStreamingConfigs = new Navigation2DFlowStreamingConfig[_flows.Length];
            _profiles = BuildProfiles(config, _profileIndexById);
            if (_config.Enabled)
            {
                if (_config.DomainCount != _flows.Length)
                {
                    throw new InvalidOperationException($"Flow domain pool requires DomainCount ({_config.DomainCount}) to match runtime flow count ({_flows.Length}).");
                }

                if (_profiles.Length <= 0)
                {
                    throw new InvalidOperationException("Flow domain pool is enabled but no explicit flow domain profiles were configured.");
                }

                if (string.IsNullOrWhiteSpace(_config.DefaultProfileId))
                {
                    throw new InvalidOperationException("Flow domain pool is enabled but DefaultProfileId is empty.");
                }

                if (!_profileIndexById.TryGetValue(_config.DefaultProfileId, out int defaultProfileIndex))
                {
                    throw new InvalidOperationException($"Flow domain pool default profile '{_config.DefaultProfileId}' is not defined.");
                }

                DefaultProfileIndex = defaultProfileIndex;
            }
            else
            {
                DefaultProfileIndex = -1;
            }

            for (int i = 0; i < _slotStreamingConfigs.Length; i++)
            {
                _slotStreamingConfigs[i] = CloneStreamingConfig(_baseStreaming);
                _flows[i].ConfigureCrowd(_baseCrowd);
            }
        }

        public bool Enabled => _config.Enabled;
        public int FlowCount => _flows.Length;
        public int DefaultProfileIndex { get; }
        public int ActiveLeaseCount { get; private set; }
        public int ActiveAssignmentCount { get; private set; }
        public int NewLeaseCountFrame { get; private set; }
        public int RecenterCountFrame { get; private set; }
        public int ReleasedLeaseCountFrame { get; private set; }
        public int UnassignedRequestCountFrame { get; private set; }

        public void ResolveAssignments(ReadOnlySpan<Navigation2DFlowDomainRequest> requests, int tick)
        {
            if (!Enabled)
            {
                _assignmentByOwnerId.Clear();
                ActiveLeaseCount = 0;
                ActiveAssignmentCount = 0;
                NewLeaseCountFrame = 0;
                RecenterCountFrame = 0;
                ReleasedLeaseCountFrame = 0;
                UnassignedRequestCountFrame = requests.Length;
                return;
            }

            EnsureRequestScratchCapacity(requests.Length);
            EnsureSlotScratchCapacity();
            EnsureSortedRequestCapacity(requests.Length);
            Array.Clear(_requestMatched, 0, requests.Length);
            Array.Clear(_slotReserved, 0, _slots.Length);
            _assignmentByOwnerId.Clear();
            _requestIndexByOwnerId.Clear();
            ActiveAssignmentCount = 0;
            NewLeaseCountFrame = 0;
            RecenterCountFrame = 0;
            ReleasedLeaseCountFrame = 0;
            UnassignedRequestCountFrame = 0;

            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                _requestIndexByOwnerId[requests[requestIndex].OwnerId] = requestIndex;
            }

            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                if (_slots[slotIndex].Occupied && _slots[slotIndex].ExpireTick < tick)
                {
                    ReleaseSlot(slotIndex);
                }
            }

            SortRequestsByPriority(requests);

            for (int sortedIndex = 0; sortedIndex < requests.Length; sortedIndex++)
            {
                int requestIndex = _sortedRequestIndices[sortedIndex];
                ref readonly Navigation2DFlowDomainRequest request = ref requests[requestIndex];
                if (!_slotByOwnerId.TryGetValue(request.OwnerId, out int slotIndex))
                {
                    continue;
                }

                ref FlowDomainSlot slot = ref _slots[slotIndex];
                if (!slot.Occupied)
                {
                    continue;
                }

                if (slot.ProfileIndex != request.ProfileIndex)
                {
                    ReleaseSlot(slotIndex);
                    continue;
                }

                bool recentered = TryRecenterSlot(slotIndex, request);
                slot.LastTouchedTick = tick;
                slot.ExpireTick = tick + _profiles[slot.ProfileIndex].HoldTicks;
                slot.Priority = request.Priority;
                _slotReserved[slotIndex] = 1;
                _requestMatched[requestIndex] = 1;
                _assignmentByOwnerId[request.OwnerId] = slotIndex;
                ActiveAssignmentCount++;
                if (recentered)
                {
                    RecenterCountFrame++;
                }
            }

            for (int sortedIndex = 0; sortedIndex < requests.Length; sortedIndex++)
            {
                int requestIndex = _sortedRequestIndices[sortedIndex];
                if (_requestMatched[requestIndex] != 0)
                {
                    continue;
                }

                ref readonly Navigation2DFlowDomainRequest request = ref requests[requestIndex];
                int slotIndex = FindAssignableSlot(request);
                if (slotIndex < 0)
                {
                    UnassignedRequestCountFrame++;
                    continue;
                }

                if (_slots[slotIndex].Occupied)
                {
                    ReleaseSlot(slotIndex);
                }

                AssignSlot(slotIndex, request, tick);
                _slotReserved[slotIndex] = 1;
                _requestMatched[requestIndex] = 1;
                _assignmentByOwnerId[request.OwnerId] = slotIndex;
                ActiveAssignmentCount++;
                NewLeaseCountFrame++;
            }

            ActiveLeaseCount = 0;
            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                if (_slots[slotIndex].Occupied)
                {
                    ActiveLeaseCount++;
                }
            }
        }

        public bool TryGetAssignedFlowId(int ownerId, out int flowId)
        {
            if (_assignmentByOwnerId.TryGetValue(ownerId, out flowId))
            {
                return true;
            }

            flowId = -1;
            return false;
        }

        public bool TryGetLeaseSnapshot(int flowId, out Navigation2DFlowDomainLeaseSnapshot snapshot)
        {
            if ((uint)flowId >= (uint)_slots.Length)
            {
                snapshot = default;
                return false;
            }

            FlowDomainSlot slot = _slots[flowId];
            snapshot = new Navigation2DFlowDomainLeaseSnapshot(
                flowId,
                slot.OwnerId,
                slot.ProfileIndex,
                slot.CenterTileX,
                slot.CenterTileY,
                slot.MinTileX,
                slot.MinTileY,
                slot.MaxTileX,
                slot.MaxTileY,
                slot.ExpireTick,
                slot.Occupied);
            return slot.Occupied;
        }

        public int ResolveProfileIndex(string profileId)
        {
            if (!_profileIndexById.TryGetValue(profileId, out int profileIndex))
            {
                throw new InvalidOperationException($"Flow domain profile '{profileId}' is not defined.");
            }

            return profileIndex;
        }

        public string BuildSummary()
        {
            return $"domains {ActiveLeaseCount}/{FlowCount} assigned {ActiveAssignmentCount} new {NewLeaseCountFrame} recenter {RecenterCountFrame} released {ReleasedLeaseCountFrame} unassigned {UnassignedRequestCountFrame}";
        }

        private static FlowDomainProfileDefinition[] BuildProfiles(
            Navigation2DFlowDomainPoolConfig config,
            Dictionary<string, int> profileIndexById)
        {
            if (config.Profiles == null || config.Profiles.Count <= 0)
            {
                return Array.Empty<FlowDomainProfileDefinition>();
            }

            var definitions = new FlowDomainProfileDefinition[config.Profiles.Count];
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                Navigation2DFlowDomainProfileConfig profile = config.Profiles[i];
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    throw new InvalidOperationException($"Flow domain profile at index {i} requires a non-empty Id.");
                }

                if (!profileIndexById.TryAdd(profile.Id, i))
                {
                    throw new InvalidOperationException($"Flow domain profile '{profile.Id}' is defined more than once.");
                }

                definitions[i] = new FlowDomainProfileDefinition(
                    i,
                    profile.Id,
                    profile.ActivationRadiusTiles,
                    profile.MaxActiveTilesPerFlow,
                    profile.UnloadGraceTicks,
                    profile.MaxPotentialCells,
                    profile.DomainWidthTiles,
                    profile.DomainHeightTiles,
                    profile.RecenterThresholdTiles,
                    profile.HoldTicks);
            }

            return definitions;
        }

        private static Navigation2DFlowStreamingConfig CloneStreamingConfig(Navigation2DFlowStreamingConfig source)
        {
            return new Navigation2DFlowStreamingConfig
            {
                Enabled = source.Enabled,
                ActivationRadiusTiles = source.ActivationRadiusTiles,
                MaxActiveTilesPerFlow = source.MaxActiveTilesPerFlow,
                UnloadGraceTicks = source.UnloadGraceTicks,
                MaxPotentialCells = source.MaxPotentialCells,
                MaxActivationWindowWidthTiles = source.MaxActivationWindowWidthTiles,
                MaxActivationWindowHeightTiles = source.MaxActivationWindowHeightTiles,
                WorldBoundsEnabled = source.WorldBoundsEnabled,
                WorldMinTileX = source.WorldMinTileX,
                WorldMinTileY = source.WorldMinTileY,
                WorldMaxTileX = source.WorldMaxTileX,
                WorldMaxTileY = source.WorldMaxTileY,
            };
        }

        private void EnsureRequestScratchCapacity(int required)
        {
            if (_requestMatched.Length >= required)
            {
                return;
            }

            int next = Math.Max(4, _requestMatched.Length);
            while (next < required)
            {
                next *= 2;
            }

            _requestMatched = new byte[next];
        }

        private void EnsureSlotScratchCapacity()
        {
            if (_slotReserved.Length < _slots.Length)
            {
                _slotReserved = new byte[_slots.Length];
            }
        }

        private void EnsureSortedRequestCapacity(int required)
        {
            if (_sortedRequestIndices.Length >= required)
            {
                return;
            }

            _sortedRequestIndices = new int[required];
        }

        private void SortRequestsByPriority(ReadOnlySpan<Navigation2DFlowDomainRequest> requests)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                _sortedRequestIndices[i] = i;
            }

            for (int i = 1; i < requests.Length; i++)
            {
                int current = _sortedRequestIndices[i];
                int j = i - 1;
                while (j >= 0 && CompareRequestOrder(requests[current], current, requests[_sortedRequestIndices[j]], _sortedRequestIndices[j]) < 0)
                {
                    _sortedRequestIndices[j + 1] = _sortedRequestIndices[j];
                    j--;
                }

                _sortedRequestIndices[j + 1] = current;
            }
        }

        private static int CompareRequestOrder(
            in Navigation2DFlowDomainRequest left,
            int leftIndex,
            in Navigation2DFlowDomainRequest right,
            int rightIndex)
        {
            int priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int ownerCompare = left.OwnerId.CompareTo(right.OwnerId);
            if (ownerCompare != 0)
            {
                return ownerCompare;
            }

            return leftIndex.CompareTo(rightIndex);
        }

        private int FindAssignableSlot(in Navigation2DFlowDomainRequest request)
        {
            int bestStaleSlot = -1;
            int bestLowerPrioritySlot = -1;

            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                if (_slotReserved[slotIndex] != 0)
                {
                    continue;
                }

                ref FlowDomainSlot slot = ref _slots[slotIndex];
                if (!slot.Occupied)
                {
                    return slotIndex;
                }

                bool ownerActiveThisTick = _requestIndexByOwnerId.ContainsKey(slot.OwnerId);
                if (!ownerActiveThisTick)
                {
                    if (bestStaleSlot < 0 || IsBetterPreemptionCandidate(slot, _slots[bestStaleSlot]))
                    {
                        bestStaleSlot = slotIndex;
                    }

                    continue;
                }

                if (slot.Priority >= request.Priority)
                {
                    continue;
                }

                if (bestLowerPrioritySlot < 0 || IsBetterPreemptionCandidate(slot, _slots[bestLowerPrioritySlot]))
                {
                    bestLowerPrioritySlot = slotIndex;
                }
            }

            if (bestStaleSlot >= 0)
            {
                return bestStaleSlot;
            }

            return bestLowerPrioritySlot;
        }

        private static bool IsBetterPreemptionCandidate(in FlowDomainSlot candidate, in FlowDomainSlot currentBest)
        {
            if (candidate.Priority != currentBest.Priority)
            {
                return candidate.Priority < currentBest.Priority;
            }

            if (candidate.ExpireTick != currentBest.ExpireTick)
            {
                return candidate.ExpireTick < currentBest.ExpireTick;
            }

            return candidate.LastTouchedTick < currentBest.LastTouchedTick;
        }

        private void AssignSlot(int slotIndex, in Navigation2DFlowDomainRequest request, int tick)
        {
            ref FlowDomainSlot slot = ref _slots[slotIndex];
            ConfigureSlot(slotIndex, request, resetTiles: true);
            slot.Occupied = true;
            slot.OwnerId = request.OwnerId;
            slot.ProfileIndex = request.ProfileIndex;
            slot.Priority = request.Priority;
            slot.LastTouchedTick = tick;
            slot.ExpireTick = tick + _profiles[request.ProfileIndex].HoldTicks;
            _slotByOwnerId[request.OwnerId] = slotIndex;
        }

        private bool TryRecenterSlot(int slotIndex, in Navigation2DFlowDomainRequest request)
        {
            ref FlowDomainSlot slot = ref _slots[slotIndex];
            FlowDomainProfileDefinition profile = _profiles[slot.ProfileIndex];
            WorldToTile(request.FocusCm, out int tileX, out int tileY);
            bool moved = Math.Abs(tileX - slot.CenterTileX) > profile.RecenterThresholdTiles ||
                         Math.Abs(tileY - slot.CenterTileY) > profile.RecenterThresholdTiles;
            if (!moved)
            {
                return false;
            }

            ConfigureSlot(slotIndex, request, resetTiles: false);
            return true;
        }

        private void ConfigureSlot(int slotIndex, in Navigation2DFlowDomainRequest request, bool resetTiles)
        {
            FlowDomainProfileDefinition profile = _profiles[request.ProfileIndex];
            ref FlowDomainSlot slot = ref _slots[slotIndex];
            WorldToTile(request.FocusCm, out int centerTileX, out int centerTileY);
            ComputeDomainBounds(profile, centerTileX, centerTileY, out int minTileX, out int minTileY, out int maxTileX, out int maxTileY);

            Navigation2DFlowStreamingConfig streaming = _slotStreamingConfigs[slotIndex];
            streaming.Enabled = _baseStreaming.Enabled;
            streaming.ActivationRadiusTiles = profile.ActivationRadiusTiles;
            streaming.MaxActiveTilesPerFlow = profile.MaxActiveTilesPerFlow;
            streaming.UnloadGraceTicks = profile.UnloadGraceTicks;
            streaming.MaxPotentialCells = profile.MaxPotentialCells;
            streaming.MaxActivationWindowWidthTiles = profile.DomainWidthTiles;
            streaming.MaxActivationWindowHeightTiles = profile.DomainHeightTiles;
            streaming.WorldBoundsEnabled = true;
            streaming.WorldMinTileX = minTileX;
            streaming.WorldMinTileY = minTileY;
            streaming.WorldMaxTileX = maxTileX;
            streaming.WorldMaxTileY = maxTileY;

            _flows[slotIndex].ConfigureStreaming(streaming);
            if (resetTiles)
            {
                _flows[slotIndex].ResetActiveTiles();
            }

            slot.CenterTileX = centerTileX;
            slot.CenterTileY = centerTileY;
            slot.MinTileX = minTileX;
            slot.MinTileY = minTileY;
            slot.MaxTileX = maxTileX;
            slot.MaxTileY = maxTileY;
        }

        private void ComputeDomainBounds(
            in FlowDomainProfileDefinition profile,
            int centerTileX,
            int centerTileY,
            out int minTileX,
            out int minTileY,
            out int maxTileX,
            out int maxTileY)
        {
            int halfWidthLow = (profile.DomainWidthTiles - 1) / 2;
            int halfWidthHigh = profile.DomainWidthTiles - 1 - halfWidthLow;
            int halfHeightLow = (profile.DomainHeightTiles - 1) / 2;
            int halfHeightHigh = profile.DomainHeightTiles - 1 - halfHeightLow;

            minTileX = centerTileX - halfWidthLow;
            maxTileX = centerTileX + halfWidthHigh;
            minTileY = centerTileY - halfHeightLow;
            maxTileY = centerTileY + halfHeightHigh;

            if (_baseStreaming.WorldBoundsEnabled)
            {
                if (minTileX < _baseStreaming.WorldMinTileX)
                {
                    maxTileX += _baseStreaming.WorldMinTileX - minTileX;
                    minTileX = _baseStreaming.WorldMinTileX;
                }

                if (maxTileX > _baseStreaming.WorldMaxTileX)
                {
                    minTileX -= maxTileX - _baseStreaming.WorldMaxTileX;
                    maxTileX = _baseStreaming.WorldMaxTileX;
                }

                if (minTileY < _baseStreaming.WorldMinTileY)
                {
                    maxTileY += _baseStreaming.WorldMinTileY - minTileY;
                    minTileY = _baseStreaming.WorldMinTileY;
                }

                if (maxTileY > _baseStreaming.WorldMaxTileY)
                {
                    minTileY -= maxTileY - _baseStreaming.WorldMaxTileY;
                    maxTileY = _baseStreaming.WorldMaxTileY;
                }

                minTileX = Math.Max(minTileX, _baseStreaming.WorldMinTileX);
                minTileY = Math.Max(minTileY, _baseStreaming.WorldMinTileY);
                maxTileX = Math.Min(maxTileX, _baseStreaming.WorldMaxTileX);
                maxTileY = Math.Min(maxTileY, _baseStreaming.WorldMaxTileY);
            }
        }

        private void WorldToTile(in Fix64Vec2 positionCm, out int tileX, out int tileY)
        {
            _surface.WorldToCell(positionCm, out int cellX, out int cellY);
            tileX = FloorDiv(cellX, _surface.TileSizeCells);
            tileY = FloorDiv(cellY, _surface.TileSizeCells);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            {
                quotient--;
            }

            return quotient;
        }

        private void ReleaseSlot(int slotIndex)
        {
            ref FlowDomainSlot slot = ref _slots[slotIndex];
            if (!slot.Occupied)
            {
                return;
            }

            _flows[slotIndex].ResetActiveTiles();
            _slotByOwnerId.Remove(slot.OwnerId);
            slot = default;
            ReleasedLeaseCountFrame++;
        }

        private readonly record struct FlowDomainProfileDefinition(
            int Index,
            string Id,
            int ActivationRadiusTiles,
            int MaxActiveTilesPerFlow,
            int UnloadGraceTicks,
            float MaxPotentialCells,
            int DomainWidthTiles,
            int DomainHeightTiles,
            int RecenterThresholdTiles,
            int HoldTicks);

        private struct FlowDomainSlot
        {
            public int OwnerId;
            public int ProfileIndex;
            public int Priority;
            public int CenterTileX;
            public int CenterTileY;
            public int MinTileX;
            public int MinTileY;
            public int MaxTileX;
            public int MaxTileY;
            public int LastTouchedTick;
            public int ExpireTick;
            public bool Occupied;
        }
    }
}
