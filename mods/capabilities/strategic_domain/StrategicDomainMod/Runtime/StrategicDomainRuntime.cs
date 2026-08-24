using System;
using System.Collections.Generic;
using Arch.Core;
using StrategicDomainMod.Components;

namespace StrategicDomainMod.Runtime
{
    public sealed class StrategicDomainRuntime
    {
        private readonly World _world;
        private readonly Dictionary<int, Entity> _settlementsByKey = new();
        private readonly Dictionary<int, Entity> _nodesByKey = new();
        private readonly Dictionary<int, Entity> _forcesByKey = new();
        private readonly List<(int From, int To)> _edges = new();
        private readonly Dictionary<int, int> _nodeSubnet = new();
        private readonly Dictionary<int, float> _subnetCapacity = new();
        private readonly Dictionary<int, float> _subnetDemand = new();
        private int _viewerFaction = 1;
        private int _graceTicksRemaining;
        private bool _networkSplit;

        public StrategicDomainRuntime(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int GraceTicksRemaining => _graceTicksRemaining;
        public bool NetworkSplit => _networkSplit;
        public IReadOnlyDictionary<int, int> NodeSubnet => _nodeSubnet;
        public int ViewerFaction
        {
            get => _viewerFaction;
            set => _viewerFaction = value;
        }

        public Entity RegisterSettlement(
            int settlementKey,
            int factionOwner,
            float wallMax,
            float garrisonMax,
            int residentHeroKey = 0)
        {
            if (_settlementsByKey.ContainsKey(settlementKey))
            {
                throw new InvalidOperationException($"Settlement key '{settlementKey}' already registered.");
            }

            Entity entity = _world.Create(
                new SettlementIdentityCm { SettlementKey = settlementKey, FactionOwner = factionOwner },
                new SettlementDefenseCm
                {
                    WallDurability = wallMax,
                    WallDurabilityMax = wallMax,
                    GarrisonPool = garrisonMax,
                    GarrisonPoolMax = garrisonMax,
                    ControlState = SettlementControlState.Intact,
                },
                new SettlementGovernanceCm
                {
                    ResidentHeroKey = residentHeroKey,
                    ProductionOutput = 1f,
                });
            _settlementsByKey[settlementKey] = entity;
            return entity;
        }

        public Entity RegisterSupplyNode(
            int nodeKey,
            int settlementKey,
            bool providesSupply,
            bool isHub,
            float capacity,
            float demandWeight)
        {
            if (_nodesByKey.ContainsKey(nodeKey))
            {
                throw new InvalidOperationException($"Supply node '{nodeKey}' already registered.");
            }

            Entity entity = _world.Create(new SupplyNodeCm
            {
                NodeKey = nodeKey,
                SettlementKey = settlementKey,
                ProvidesSupply = providesSupply,
                IsHub = isHub,
                SupplyCapacity = capacity,
                DemandWeight = demandWeight,
            });
            _nodesByKey[nodeKey] = entity;
            return entity;
        }

        public void Connect(int fromNodeKey, int toNodeKey)
        {
            EnsureNode(fromNodeKey);
            EnsureNode(toNodeKey);
            _edges.Add((fromNodeKey, toNodeKey));
            _edges.Add((toNodeKey, fromNodeKey));
            RecalculateSubnets();
        }

        public Entity RegisterForce(
            int forceKey,
            int factionOwner,
            int nodeKey,
            float strength,
            bool hasSiegeCapability,
            bool isLogistics)
        {
            RecalculateSubnets();
            _nodeSubnet.TryGetValue(nodeKey, out int subnet);
            Entity entity = _world.Create(new FieldForceCm
            {
                ForceKey = forceKey,
                FactionOwner = factionOwner,
                SubnetKey = subnet,
                Strength = strength,
                HasSiegeCapability = hasSiegeCapability,
                IsLogistics = isLogistics,
            });
            _forcesByKey[forceKey] = entity;
            RecalculateDemand();
            return entity;
        }

        public bool TryGetSettlement(int settlementKey, out Entity entity) =>
            _settlementsByKey.TryGetValue(settlementKey, out entity);

        public SettlementDefenseCm GetDefense(int settlementKey) =>
            _world.Get<SettlementDefenseCm>(RequireSettlement(settlementKey));

        public SettlementIdentityCm GetIdentity(int settlementKey) =>
            _world.Get<SettlementIdentityCm>(RequireSettlement(settlementKey));

        public SettlementGovernanceCm GetGovernance(int settlementKey) =>
            _world.Get<SettlementGovernanceCm>(RequireSettlement(settlementKey));

        public void TransferSettlementOwner(int settlementKey, int newOwner)
        {
            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementIdentityCm identity = ref _world.Get<SettlementIdentityCm>(settlement);
            identity.FactionOwner = newOwner;
            RecalculateSubnets();
        }

        public void ApplyGarrisonDamage(int settlementKey, float amount)
        {
            if (amount <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementDefenseCm defense = ref _world.Get<SettlementDefenseCm>(settlement);
            defense.GarrisonPool = Math.Max(0f, defense.GarrisonPool - amount);
            if (defense.GarrisonPool <= 0f && defense.ControlState == SettlementControlState.Intact)
            {
                defense.ControlState = SettlementControlState.Capturable;
            }
        }

        public void ApplyWallDamage(int settlementKey, float amount, bool attackerHasSiege)
        {
            if (amount <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (!attackerHasSiege)
            {
                throw new InvalidOperationException(
                    "Wall damage requires siege-capable force.");
            }

            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementDefenseCm defense = ref _world.Get<SettlementDefenseCm>(settlement);
            defense.WallDurability = Math.Max(0f, defense.WallDurability - amount);
            if (defense.WallDurability <= 0f && defense.ControlState == SettlementControlState.Intact)
            {
                defense.ControlState = SettlementControlState.Ruined;
            }
        }

        public void CommitTroopsTakeover(int settlementKey, int newOwner, float troopCommitment, bool logisticsDeployed)
        {
            if (troopCommitment <= 0f)
            {
                throw new InvalidOperationException("troopCommitment must be positive.");
            }

            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementDefenseCm defense = ref _world.Get<SettlementDefenseCm>(settlement);
            if (defense.ControlState == SettlementControlState.Intact)
            {
                throw new InvalidOperationException(
                    $"Settlement '{settlementKey}' is not breached.");
            }

            if (defense.ControlState == SettlementControlState.Ruined && !logisticsDeployed)
            {
                throw new InvalidOperationException(
                    $"Settlement '{settlementKey}' is ruined and requires logistics deploy before takeover.");
            }

            ref SettlementIdentityCm identity = ref _world.Get<SettlementIdentityCm>(settlement);
            ref SettlementGovernanceCm governance = ref _world.Get<SettlementGovernanceCm>(settlement);
            identity.FactionOwner = newOwner;
            if (governance.ResidentHeroKey != 0)
            {
                governance.CaptiveHeroKey = governance.ResidentHeroKey;
                governance.ResidentHeroKey = 0;
            }

            defense.GarrisonPool = troopCommitment;
            defense.ControlState = SettlementControlState.Intact;
            RecalculateSubnets();
        }

        public void LiftSiege(int settlementKey)
        {
            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementDefenseCm defense = ref _world.Get<SettlementDefenseCm>(settlement);
            if (defense.ControlState == SettlementControlState.Intact)
            {
                throw new InvalidOperationException(
                    $"Settlement '{settlementKey}' is not under breach; siege lift rejected.");
            }

            // Lifting siege restores intact control without ownership transfer.
            defense.ControlState = SettlementControlState.Intact;
            if (defense.GarrisonPool <= 0f)
            {
                defense.GarrisonPool = Math.Max(1f, defense.GarrisonPoolMax * 0.25f);
            }

            if (defense.WallDurability <= 0f)
            {
                defense.WallDurability = Math.Max(1f, defense.WallDurabilityMax * 0.25f);
            }
        }

        public void AppointGovernor(int settlementKey, int heroKey)
        {
            if (heroKey == 0)
            {
                throw new InvalidOperationException("heroKey is required.");
            }

            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementGovernanceCm governance = ref _world.Get<SettlementGovernanceCm>(settlement);
            governance.GovernorHeroKey = heroKey;
            governance.RelationModifier = 1f;
            governance.ProductionOutput = 1f + governance.RelationModifier;
        }

        public void DisposeCaptive(int settlementKey, string action)
        {
            Entity settlement = RequireSettlement(settlementKey);
            ref SettlementGovernanceCm governance = ref _world.Get<SettlementGovernanceCm>(settlement);
            if (governance.CaptiveHeroKey == 0)
            {
                throw new InvalidOperationException($"Settlement '{settlementKey}' has no captive.");
            }

            if (action is not ("recruit" or "release" or "execute"))
            {
                throw new InvalidOperationException($"Unknown captive action '{action}'.");
            }

            governance.CaptiveHeroKey = 0;
        }

        public void BeginGrace(int ticks)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }

            _graceTicksRemaining = ticks;
        }

        public void TickGrace()
        {
            if (_graceTicksRemaining > 0)
            {
                _graceTicksRemaining--;
            }
        }

        public bool IsSubnetOverCapacity(int subnetKey)
        {
            RequireSubnet(subnetKey, out float capacity, out float demand);
            return demand > capacity;
        }

        public float GetSubnetCapacity(int subnetKey)
        {
            RequireSubnet(subnetKey, out float capacity, out _);
            return capacity;
        }

        public float GetSubnetDemand(int subnetKey)
        {
            RequireSubnet(subnetKey, out _, out float demand);
            return demand;
        }

        private void RequireSubnet(int subnetKey, out float capacity, out float demand)
        {
            if (!_subnetCapacity.TryGetValue(subnetKey, out capacity))
            {
                throw new InvalidOperationException($"Unknown supply subnet '{subnetKey}'.");
            }

            if (!_subnetDemand.TryGetValue(subnetKey, out demand))
            {
                throw new InvalidOperationException($"Unknown supply subnet '{subnetKey}'.");
            }
        }

        public void RecalculateSubnets()
        {
            _nodeSubnet.Clear();
            _subnetCapacity.Clear();
            var adjacency = new Dictionary<int, List<int>>();
            foreach (int nodeKey in _nodesByKey.Keys)
            {
                adjacency[nodeKey] = new List<int>();
            }

            for (int i = 0; i < _edges.Count; i++)
            {
                (int from, int to) = _edges[i];
                if (CanTraverse(from, to))
                {
                    adjacency[from].Add(to);
                }
            }

            int nextSubnet = 1;
            var visited = new HashSet<int>();
            foreach (int nodeKey in _nodesByKey.Keys)
            {
                if (!visited.Add(nodeKey))
                {
                    continue;
                }

                var queue = new Queue<int>();
                queue.Enqueue(nodeKey);
                float capacity = 0f;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    _nodeSubnet[current] = nextSubnet;
                    SupplyNodeCm node = _world.Get<SupplyNodeCm>(_nodesByKey[current]);
                    if (node.ProvidesSupply)
                    {
                        capacity += node.SupplyCapacity;
                    }

                    List<int> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        if (visited.Add(neighbors[i]))
                        {
                            queue.Enqueue(neighbors[i]);
                        }
                    }
                }

                _subnetCapacity[nextSubnet] = capacity;
                nextSubnet++;
            }

            _networkSplit = nextSubnet > 2;
            RecalculateDemand();
        }

        private void RecalculateDemand()
        {
            _subnetDemand.Clear();
            foreach (int subnetKey in _subnetCapacity.Keys)
            {
                _subnetDemand[subnetKey] = 0f;
            }

            foreach (KeyValuePair<int, Entity> pair in _forcesByKey)
            {
                if (!_world.IsAlive(pair.Value) || !_world.Has<FieldForceCm>(pair.Value))
                {
                    continue;
                }

                ref FieldForceCm force = ref _world.Get<FieldForceCm>(pair.Value);
                _subnetDemand.TryGetValue(force.SubnetKey, out float demand);
                _subnetDemand[force.SubnetKey] = demand + force.Strength;
            }
        }

        private bool CanTraverse(int from, int to)
        {
            SupplyNodeCm fromNode = _world.Get<SupplyNodeCm>(_nodesByKey[from]);
            SupplyNodeCm toNode = _world.Get<SupplyNodeCm>(_nodesByKey[to]);
            if (!fromNode.IsHub && !toNode.IsHub)
            {
                return NodeUsableByViewer(fromNode) && NodeUsableByViewer(toNode);
            }

            SupplyNodeCm hub = fromNode.IsHub ? fromNode : toNode;
            return NodeOwnedByViewer(hub.SettlementKey);
        }

        private bool NodeUsableByViewer(SupplyNodeCm node)
        {
            if (node.SettlementKey == 0)
            {
                return true;
            }

            return NodeOwnedByViewer(node.SettlementKey) || !node.IsHub;
        }

        private bool NodeOwnedByViewer(int settlementKey)
        {
            if (settlementKey == 0)
            {
                return true;
            }

            if (!_settlementsByKey.TryGetValue(settlementKey, out Entity entity))
            {
                throw new InvalidOperationException($"Unknown settlement '{settlementKey}'.");
            }

            return _world.Get<SettlementIdentityCm>(entity).FactionOwner == _viewerFaction;
        }

        private Entity RequireSettlement(int settlementKey)
        {
            if (!_settlementsByKey.TryGetValue(settlementKey, out Entity entity) || !_world.IsAlive(entity))
            {
                throw new InvalidOperationException($"Unknown settlement '{settlementKey}'.");
            }

            return entity;
        }

        private void EnsureNode(int nodeKey)
        {
            if (!_nodesByKey.ContainsKey(nodeKey))
            {
                throw new InvalidOperationException($"Unknown supply node '{nodeKey}'.");
            }
        }
    }
}
