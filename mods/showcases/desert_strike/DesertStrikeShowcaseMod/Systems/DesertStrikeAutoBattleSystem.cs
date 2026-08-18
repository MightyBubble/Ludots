using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeAutoBattleSystem : BaseSystem<World, float>
    {
        private const int ArrivalRadiusCm = 140;
        private const int TargetScratchCapacity = 512;
        private const string AttackCooldownTag = "Cooldown.Ds.Attack";

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<OrderBuffer, WorldPositionCm, AttributeBuffer, Team, GameplayTagContainer, DesertStrikeUnit>();

        private readonly DesertStrikeState _state;
        private readonly OrderQueue _orders;
        private readonly ISpatialQueryService _spatial;
        private readonly TagOps _tagOps;
        private readonly int _castAbilityOrderTypeId;
        private readonly int _moveToOrderTypeId;
        private readonly int _attackRangeAttributeId;
        private readonly int _attackCooldownTagId;
        private readonly Entity[] _scratch = new Entity[TargetScratchCapacity];

        public DesertStrikeAutoBattleSystem(GameEngine engine, DesertStrikeState state)
            : base(engine.World)
        {
            _state = state;
            _orders = engine.GetService(CoreServiceKeys.OrderQueue);
            _spatial = engine.GetService(CoreServiceKeys.SpatialQueryService);
            _tagOps = engine.GetService(CoreServiceKeys.TagOps);
            _castAbilityOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            _moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            _attackRangeAttributeId = EnsureAttributeId("AttackRange");
            _attackCooldownTagId = EnsureTagId(AttackCooldownTag);
        }

        public override void Update(in float dt)
        {
            if (_state.GameOver)
            {
                return;
            }

            if (!World.IsAlive(_state.PlayerBase) || !World.IsAlive(_state.AiBase))
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var buffers = chunk.GetSpan<OrderBuffer>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                var attributes = chunk.GetSpan<AttributeBuffer>();
                var tagContainers = chunk.GetSpan<GameplayTagContainer>();

                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref OrderBuffer buffer = ref buffers[index];
                    bool marching = buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == _moveToOrderTypeId;
                    if (buffer.HasActive && !marching)
                    {
                        continue;
                    }

                    if (buffer.HasQueued || buffer.HasPending)
                    {
                        continue;
                    }

                    ref GameplayTagContainer tags = ref tagContainers[index];
                    if (_tagOps.HasTag(ref tags, _attackCooldownTagId, TagSense.Effective))
                    {
                        continue;
                    }

                    WorldCmInt2 actorPos = positions[index].Value.ToWorldCmInt2();
                    int rangeCm = (int)MathF.Max(0f, attributes[index].GetCurrent(_attackRangeAttributeId));
                    if (TryAcquireHostile(entity, in actorPos, rangeCm, out Entity target))
                    {
                        EnqueueAttack(entity, target);
                    }
                    else if (!marching && !IsNearEnemyBase(entity, in actorPos))
                    {
                        EnqueueMarch(entity);
                    }
                }
            }
        }

        private bool TryAcquireHostile(Entity actor, in WorldCmInt2 actorPos, int radiusCm, out Entity target)
        {
            target = default;
            if (radiusCm <= 0)
            {
                return false;
            }

            int myTeam = World.Get<Team>(actor).Id;
            int count = _spatial.QueryRadius(actorPos, radiusCm, _scratch).Count;
            long bestDistanceSq = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Entity candidate = _scratch[i];
                if (candidate == default ||
                    candidate.Equals(actor) ||
                    !World.IsAlive(candidate) ||
                    !World.Has<WorldPositionCm>(candidate) ||
                    !World.Has<Team>(candidate) ||
                    !World.Has<AttributeBuffer>(candidate))
                {
                    continue;
                }

                int candidateTeam = World.Get<Team>(candidate).Id;
                if (TeamManager.GetRelationship(myTeam, candidateTeam) != TeamRelationship.Hostile)
                {
                    continue;
                }

                WorldCmInt2 candidatePos = World.Get<WorldPositionCm>(candidate).Value.ToWorldCmInt2();
                long dx = candidatePos.X - actorPos.X;
                long dy = candidatePos.Y - actorPos.Y;
                long distanceSq = dx * dx + dy * dy;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    target = candidate;
                }
            }

            return target != default;
        }

        private bool IsNearEnemyBase(Entity actor, in WorldCmInt2 actorPos)
        {
            int myTeam = World.Get<Team>(actor).Id;
            Entity enemyBase = myTeam == _state.PlayerTeam ? _state.AiBase : _state.PlayerBase;
            if (!World.IsAlive(enemyBase) || !World.Has<WorldPositionCm>(enemyBase))
            {
                return true;
            }

            WorldCmInt2 basePos = World.Get<WorldPositionCm>(enemyBase).Value.ToWorldCmInt2();
            long dx = basePos.X - actorPos.X;
            long dy = basePos.Y - actorPos.Y;
            return dx * dx + dy * dy <= (long)ArrivalRadiusCm * ArrivalRadiusCm;
        }

        private void EnqueueAttack(Entity actor, Entity target)
        {
            var order = new Order
            {
                OrderTypeId = _castAbilityOrderTypeId,
                Actor = actor,
                Target = target,
                PlayerId = ResolvePlayerId(actor),
                SubmitMode = OrderSubmitMode.Immediate,
            };
            order.Args.I0 = 0;

            if (!_orders.TryEnqueue(in order))
            {
                throw new InvalidOperationException("DS.BATTLE.ERR.OrderQueueFull");
            }
        }

        private void EnqueueMarch(Entity actor)
        {
            int myTeam = World.Get<Team>(actor).Id;
            Entity enemyBase = myTeam == _state.PlayerTeam ? _state.AiBase : _state.PlayerBase;
            if (!World.IsAlive(enemyBase) || !World.Has<WorldPositionCm>(enemyBase))
            {
                return;
            }

            WorldCmInt2 destination = World.Get<WorldPositionCm>(enemyBase).Value.ToWorldCmInt2();
            var order = new Order
            {
                OrderTypeId = _moveToOrderTypeId,
                Actor = actor,
                PlayerId = ResolvePlayerId(actor),
                SubmitMode = OrderSubmitMode.Immediate,
            };
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = new Vector3(destination.X, 0f, destination.Y);

            if (!_orders.TryEnqueue(in order))
            {
                throw new InvalidOperationException("DS.BATTLE.ERR.OrderQueueFull");
            }
        }

        private int ResolvePlayerId(Entity actor)
        {
            return World.Has<PlayerOwner>(actor) ? World.Get<PlayerOwner>(actor).PlayerId : 0;
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }

        private static int EnsureTagId(string tagName)
        {
            int id = TagRegistry.GetId(tagName);
            return id > 0 ? id : TagRegistry.Register(tagName);
        }
    }
}
