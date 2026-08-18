using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.Diagnostics;

namespace CityRallyWebUiShowcaseMod.Systems
{
    /// <summary>
    /// 驻军复合动作执行器（配置驱动）：
    ///   - 右键城池（stored target Kind=Entity）→ 平民进驻 / 英雄就任太守
    ///   - 右键地板（stored target Kind=Point）→ 平民出城走向目标 / 太守插旗引导后出城
    /// 角色由标签区分（Role.CityRally.Peasant / GovernorCandidate / Governor），
    /// 立旗能力经 PlantFlag 能力的 requiredAll 标签门控解锁。
    /// </summary>
    public sealed class CityRallyGarrisonSystem : ISystem<float>
    {
        private const string PeasantRoleTagName = "Role.CityRally.Peasant";
        private const string GovernorCandidateTagName = "Role.CityRally.GovernorCandidate";
        private const string GovernorTagName = "Role.CityRally.Governor";
        private const string PlantingTagName = "Status.CityRally.Planting";

        private static readonly QueryDescription RoleActorQuery = new QueryDescription()
            .WithAll<Name, Team, WorldPositionCm, CommandSourceSelectableTag>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly OrderQueue _orders;

        private int _peasantRoleTagId;
        private int _governorCandidateTagId;
        private int _governorTagId;
        private int _plantingTagId;
        private int _setSpawnTargetOrderTypeId;
        private int _castAbilityOrderTypeId;
        private int _moveToOrderTypeId;
        private int _spawnTargetKindKey;
        private int _spawnTargetPositionKey;
        private int _spawnTargetEntityKey;
        private int _spawnTargetHexQKey;
        private int _spawnTargetHexRKey;

        public CityRallyGarrisonSystem(GameEngine engine, OrderQueue orders)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsCityRallyMapActive())
            {
                return;
            }

            EnsureResolvedIds();
            ProcessGarrisonCommands();
        }

        private void EnsureResolvedIds()
        {
            _peasantRoleTagId = TagRegistry.GetId(PeasantRoleTagName);
            _governorCandidateTagId = TagRegistry.GetId(GovernorCandidateTagName);
            _governorTagId = TagRegistry.GetId(GovernorTagName);
            _plantingTagId = TagRegistry.GetId(PlantingTagName);
            if (_plantingTagId <= 0)
            {
                _plantingTagId = TagRegistry.Register(PlantingTagName);
            }

            var orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("CityRallyGarrisonSystem requires OrderTypeRegistry.");
            _setSpawnTargetOrderTypeId = orderTypes.GetId("setCityRallySpawnTarget");
            _castAbilityOrderTypeId = orderTypes.GetId("castAbility");
            _moveToOrderTypeId = orderTypes.GetId("moveTo");

            var orderType = orderTypes.Get(_setSpawnTargetOrderTypeId);
            var storedKeys = orderType.PersistentStoredTargetKeys;
            _spawnTargetKindKey = storedKeys.TargetKindKey;
            _spawnTargetPositionKey = storedKeys.TargetPositionKey;
            _spawnTargetEntityKey = storedKeys.TargetEntityKey;
            _spawnTargetHexQKey = storedKeys.HexQKey;
            _spawnTargetHexRKey = storedKeys.HexRKey;
        }

        private void ProcessGarrisonCommands()
        {
            var commandActors = new List<Entity>(16);
            _world.Query(in RoleActorQuery, (Entity entity, ref Name _, ref Team _, ref WorldPositionCm _,
                ref CommandSourceSelectableTag _) =>
            {
                if (!_world.Has<BlackboardIntBuffer>(entity) || !_world.Has<BlackboardSpatialBuffer>(entity))
                {
                    return;
                }

                bool hasStoredTarget = false;
                if (_world.Has<OrderBuffer>(entity))
                {
                    ref var orders = ref _world.Get<OrderBuffer>(entity);
                    hasStoredTarget = orders.HasActive &&
                                      orders.ActiveOrder.Order.OrderTypeId == _castAbilityOrderTypeId;
                }

                if (!hasStoredTarget)
                {
                    ref var ints = ref _world.Get<BlackboardIntBuffer>(entity);
                    hasStoredTarget = ints.TryGet(_spawnTargetKindKey, out int kindValue) &&
                                      kindValue != (int)BlackboardStoredTargetKind.None;
                }

                if (hasStoredTarget)
                {
                    commandActors.Add(entity);
                }
            });

            for (int i = 0; i < commandActors.Count; i++)
            {
                Entity actor = commandActors[i];
                if (!_world.IsAlive(actor))
                {
                    continue;
                }

                if (_world.Has<OrderBuffer>(actor) &&
                    _world.Get<OrderBuffer>(actor).HasActive &&
                    _world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderTypeId == _castAbilityOrderTypeId)
                {
                    // 命令卡路径：读 castAbility 的目标（实体或位置）。
                    ref readonly Order castOrder = ref _world.Get<OrderBuffer>(actor).ActiveOrder.Order;
                    if (castOrder.Target != Entity.Null && _world.IsAlive(castOrder.Target))
                    {
                        HandleEnterCity(actor, castOrder.Target);
                    }
                    else
                    {
                        Vector3 targetCm = new(
                            castOrder.Args.Spatial.WorldCm.X,
                            0f,
                            castOrder.Args.Spatial.WorldCm.Z);
                        HandleLeaveOrPlantFlag(actor, targetCm);
                    }
                }
                else
                {
                    var keys = new BlackboardStoredTargetKeys(
                        _spawnTargetKindKey,
                        _spawnTargetPositionKey,
                        _spawnTargetEntityKey,
                        _spawnTargetHexQKey,
                        _spawnTargetHexRKey);
                    if (!BlackboardStoredTargetOps.TryRead(_world, actor, in keys, out BlackboardStoredTargetSnapshot stored) ||
                        !stored.HasTarget)
                    {
                        continue;
                    }

                    if (stored.Kind == BlackboardStoredTargetKind.Entity && stored.TargetEntity != Entity.Null)
                    {
                        HandleEnterCity(actor, stored.TargetEntity);
                    }
                    else if (stored.Kind == BlackboardStoredTargetKind.Point)
                    {
                        HandleLeaveOrPlantFlag(actor, stored.WorldPositionCm);
                    }

                    BlackboardStoredTargetOps.Clear(_world, actor, in keys);
                }
            }
        }

        private void HandleEnterCity(Entity actor, Entity city)
        {
            if (_world.Has<ChildOf>(actor))
            {
                return;
            }

            bool isGovernorCandidate = HasRole(_governorCandidateTagId, actor) ||
                                       IsNamed(actor, "英雄");
            if (isGovernorCandidate && !HasRole(_governorTagId, actor))
            {
                // 英雄就任太守：打 Governor 标签并解锁立旗能力（PlantFlag 的 requiredAll 门控）。
                TagOps tagOps = RequireTagOps();
                if (_governorTagId > 0)
                {
                    tagOps.AddTag(_world, actor, _governorTagId);
                }

                if (_governorCandidateTagId > 0)
                {
                    tagOps.RemoveTag(_world, actor, _governorCandidateTagId);
                }
            }

            RelationOps.SetParent(_world, actor, city);
            SnapToParent(actor, city);
        }

        private void HandleLeaveOrPlantFlag(Entity actor, Vector3 targetCm)
        {
            bool isGovernor = HasRole(_governorTagId, actor);
            if (isGovernor)
            {
                // 太守：插旗引导（读条）→ 完成后建旗并出城。
                if (HasPlanting(actor))
                {
                    CompleteFlagPlanting(actor, targetCm);
                }
                else
                {
                    BeginFlagPlanting(actor, targetCm);
                }

                return;
            }

            // 平民（或未就任英雄）：直接出城走向目标。
            LeaveCity(actor, targetCm);
        }

        private void BeginFlagPlanting(Entity actor, Vector3 targetCm)
        {
            if (!_world.Has<BlackboardSpatialBuffer>(actor))
            {
                _world.Add(actor, new BlackboardSpatialBuffer());
            }

            ref var spatial = ref _world.Get<BlackboardSpatialBuffer>(actor);
            spatial.SetPoint(OrderBlackboardKeys.Cast_TargetPosition, targetCm);

            if (!_world.Has<GameplayTagContainer>(actor))
            {
                _world.Add(actor, new GameplayTagContainer());
            }

            RequireTagOps().AddTag(_world, actor, _plantingTagId);
        }

        private void CompleteFlagPlanting(Entity actor, Vector3 flagPointCm)
        {
            SpawnFlag(actor, flagPointCm);
            RequireTagOps().RemoveTag(_world, actor, _plantingTagId);
            LeaveCity(actor, flagPointCm);
        }

        private void SpawnFlag(Entity actor, Vector3 flagPointCm)
        {
            if (!_world.IsAlive(actor))
            {
                return;
            }

            int teamId = _world.TryGet(actor, out Team team) ? team.Id : 1;
            var attributes = new AttributeBuffer();
            int healthId = AttributeRegistry.GetId("Health");
            attributes.SetBase(healthId, 60f);
            attributes.SetCurrent(healthId, 60f);
            Entity flag = _world.Create(
                new Name { Value = "旗帜" },
                new Team { Id = teamId },
                WorldPositionCm.FromCm(
                    (int)MathF.Round(flagPointCm.X, MidpointRounding.AwayFromZero),
                    (int)MathF.Round(flagPointCm.Z, MidpointRounding.AwayFromZero)),
                attributes);

            _world.Add(flag, new MapEntity { MapId = _engine.CurrentMapSession?.MapId ?? default });
        }

        private void LeaveCity(Entity actor, Vector3 targetCm)
        {
            if (_world.Has<ChildOf>(actor))
            {
                RelationOps.RemoveParent(_world, actor);
            }

            _orders.TryEnqueue(new Order
            {
                OrderTypeId = _moveToOrderTypeId,
                PlayerId = ResolvePlayerId(actor),
                Actor = actor,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = targetCm,
                    },
                },
                SubmitMode = OrderSubmitMode.Immediate,
            });
        }

        private void SnapToParent(Entity child, Entity parent)
        {
            if (!_world.TryGet(parent, out WorldPositionCm parentPos) ||
                !_world.Has<WorldPositionCm>(child))
            {
                return;
            }

            ref var childPos = ref _world.Get<WorldPositionCm>(child);
            childPos.Value = parentPos.Value;
        }

        private bool IsNamed(Entity entity, string nameToken)
        {
            return _world.TryGet(entity, out Name name) &&
                   !string.IsNullOrWhiteSpace(name.Value) &&
                   name.Value.IndexOf(nameToken, StringComparison.Ordinal) >= 0;
        }

        private bool HasRole(int roleTagId, Entity entity)
        {
            if (roleTagId <= 0 || !_world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return RequireTagOps().HasTag(ref tags, roleTagId, TagSense.Effective);
        }

        private bool HasPlanting(Entity entity)
        {
            if (_plantingTagId <= 0 || !_world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return RequireTagOps().HasTag(ref tags, _plantingTagId, TagSense.Effective);
        }

        private TagOps RequireTagOps()
        {
            return _engine.GetService(CoreServiceKeys.TagOps) as TagOps
                ?? throw new InvalidOperationException("CityRallyGarrisonSystem requires TagOps.");
        }

        private int ResolvePlayerId(Entity entity)
        {
            return _world.TryGet(entity, out PlayerOwner owner) ? owner.PlayerId : 1;
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsCityRallyMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "city_rally", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
