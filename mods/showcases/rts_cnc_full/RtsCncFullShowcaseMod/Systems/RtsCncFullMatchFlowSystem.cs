using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace RtsCncFullShowcaseMod.Systems;

internal sealed class RtsCncFullMatchFlowSystem : ISystem<float>
{
    private const string MapId = "rts_cnc_full";
    private const string ProducerName = "Atlantic Directorate Barracks";
    private const string HarvesterName = "Atlantic Directorate Repair Rig";
    private const string EnemyProbeName = "Volkov Union Rifle Section";
    private const string TrainedUnitName = "Atlantic Directorate Rifle Section";
    private const int PlayerTeamId = 1;
    private const int EnemyTeamId = 2;

    private readonly GameEngine _engine;
    private readonly RtsCncFullMatchRuntime _runtime;
    private readonly List<Entity> _scratchEntities = new(128);
    private bool _initialized;
    private float _phaseElapsed;
    private float _trainCooldown;
    private float _attackCooldown;
    private int _trainOrdersSubmitted;
    private int _startingTrainedUnitCount;
    private int _startingEnemyCount;
    private int _debugTicks;

    public RtsCncFullMatchFlowSystem(GameEngine engine, RtsCncFullMatchRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!IsCurrentMap())
        {
            return;
        }

        EnsureInitialized();
        if (_debugTicks < 12)
        {
            Console.WriteLine($"[RtsCncFullMatchFlow] tick {_debugTicks} phase={_runtime.Phase}");
        }

        while (_runtime.TryDequeueAction(out RtsCncFullQueuedAction action))
        {
            if (_debugTicks < 12)
            {
                Console.WriteLine($"[RtsCncFullMatchFlow] action {action.Action}");
            }

            ApplyAction(action);
        }

        float safeDt = Math.Clamp(dt, 0f, 0.1f);
        switch (_runtime.Phase)
        {
            case RtsCncFullMatchPhase.Mining:
                if (_debugTicks < 12)
                {
                    Console.WriteLine("[RtsCncFullMatchFlow] before mining");
                }

                TickMining(safeDt);
                if (_debugTicks < 12)
                {
                    Console.WriteLine("[RtsCncFullMatchFlow] after mining");
                }

                break;
            case RtsCncFullMatchPhase.Training:
                TickTraining(safeDt);
                break;
            case RtsCncFullMatchPhase.Combat:
                TickCombat(safeDt);
                break;
            default:
                RefreshCounts();
                break;
        }

        _debugTicks++;
    }

    private bool IsCurrentMap()
    {
        return string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.Ordinal);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _startingTrainedUnitCount = CountEntitiesByName(TrainedUnitName);
        _startingEnemyCount = CountTeamNonAnchorEntities(EnemyTeamId);
        Entity producer = FindEntityByName(ProducerName);
        SetAttributeCurrent(producer, "Credits", 400f);
        SetAttributeCurrent(producer, "Ore", 0f);
        RefreshCounts();
        _runtime.Record("Opening state loaded: Atlantic base online, Volkov opponent alive.");
    }

    private void ApplyAction(in RtsCncFullQueuedAction action)
    {
        if (action.Action == RtsCncFullMatchAction.Reset)
        {
            _initialized = false;
            _phaseElapsed = 0f;
            _trainCooldown = 0f;
            _attackCooldown = 0f;
            _trainOrdersSubmitted = 0;
            _runtime.Reset();
            return;
        }

        switch (action.Action)
        {
            case RtsCncFullMatchAction.StartHarvest:
                BeginMining();
                break;
            case RtsCncFullMatchAction.TrainArmy:
                BeginTraining();
                break;
            case RtsCncFullMatchAction.AttackEnemy:
                BeginCombat();
                break;
        }
    }

    private void BeginMining()
    {
        _phaseElapsed = 0f;
        Entity producer = FindEntityByName(ProducerName);
        SetAttributeCurrent(producer, "Credits", 400f);
        SetAttributeCurrent(producer, "Ore", 0f);
        MoveEntityIfAlive(FindEntityByName(HarvesterName), 8200f, 12630f);
        _runtime.SetEconomy(400f, 0f, 0f);
        _runtime.SetHarvestLoads(0);
        _runtime.EnterPhase(
            RtsCncFullMatchPhase.Mining,
            "Ore truck is harvesting. Wait for the Train Army button to unlock.",
            "Mining started from real UI/DataPlane input.");
    }

    private void TickMining(float dt)
    {
        _phaseElapsed += dt;
        float progress = Math.Clamp(_phaseElapsed / 5.5f, 0f, 1f);
        float ore = progress * 1200f;
        float credits = 400f + progress * 900f;
        int loads = Math.Min(4, (int)MathF.Floor(progress * 4.01f));

        Entity producer = FindEntityByName(ProducerName);
        SetAttributeCurrent(producer, "Credits", credits);
        SetAttributeCurrent(producer, "Ore", ore);
        _runtime.SetEconomy(credits, ore, 164f);
        _runtime.SetHarvestLoads(loads);
        _runtime.SetPhaseProgress(progress);

        float leg = progress < 0.5f ? progress / 0.5f : (1f - progress) / 0.5f;
        MoveEntityIfAlive(
            FindEntityByName(HarvesterName),
            Lerp(8200f, 9700f, leg),
            Lerp(12630f, 8700f, leg));

        if (progress >= 1f)
        {
            _runtime.SetEconomy(credits, ore, 0f);
            _runtime.EnterPhase(
                RtsCncFullMatchPhase.ReadyToTrain,
                "Ore is banked. Click Train Army to spend credits through GAS production.",
                "Mining complete: credits and ore were written to the producer attributes.");
        }
    }

    private void BeginTraining()
    {
        _phaseElapsed = 0f;
        _trainCooldown = 0f;
        _trainOrdersSubmitted = 0;
        _startingTrainedUnitCount = CountEntitiesByName(TrainedUnitName);
        _runtime.SetUnitsTrained(0);
        _runtime.EnterPhase(
            RtsCncFullMatchPhase.Training,
            "Barracks is training rifle sections through GAS CreateUnit effects.",
            "Training started from real UI/DataPlane input.");
    }

    private void TickTraining(float dt)
    {
        _phaseElapsed += dt;
        _trainCooldown -= dt;
        if (_trainOrdersSubmitted < 3 && _trainCooldown <= 0f)
        {
            if (TryCastTrainAbility())
            {
                _trainOrdersSubmitted++;
                _runtime.Record($"GAS train order submitted ({_trainOrdersSubmitted}/3).");
            }

            _trainCooldown = 0.85f;
        }

        int trained = Math.Max(0, CountEntitiesByName(TrainedUnitName) - _startingTrainedUnitCount);
        _runtime.SetUnitsTrained(trained);
        _runtime.SetPhaseProgress(Math.Clamp(_phaseElapsed / 4.5f, 0f, 1f));
        RefreshCounts();

        if ((_phaseElapsed >= 3f && trained > 0) || trained >= 3)
        {
            _runtime.EnterPhase(
                RtsCncFullMatchPhase.ReadyToAttack,
                "Fresh units are on the field. Click Attack Enemy to assault Volkov.",
                $"Training complete: {trained} new rifle section(s) exist in ECS.");
        }
    }

    private bool TryCastTrainAbility()
    {
        Entity producer = FindEntityByName(ProducerName);
        Entity target = FindEntityByName(EnemyProbeName);
        if (producer == Entity.Null || target == Entity.Null)
        {
            return false;
        }

        OrderQueue orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("RtsCncFull match flow requires OrderQueue.");
        if (!_engine.MergedConfig.Constants.OrderTypeIds.TryGetValue("castAbility", out int castAbilityOrderTypeId))
        {
            throw new InvalidOperationException("Missing castAbility order type id.");
        }

        return orderQueue.TryEnqueue(new Order
        {
            OrderTypeId = castAbilityOrderTypeId,
            PlayerId = 1,
            Actor = producer,
            Target = target,
            Args = new OrderArgs { I0 = 0 },
            SubmitMode = OrderSubmitMode.Immediate,
        });
    }

    private void BeginCombat()
    {
        _phaseElapsed = 0f;
        _attackCooldown = 0f;
        _startingEnemyCount = Math.Max(1, CountTeamNonAnchorEntities(EnemyTeamId));
        _runtime.EnterPhase(
            RtsCncFullMatchPhase.Combat,
            "Atlantic forces are crossing the map and destroying the Volkov base.",
            "Attack order started from real UI/DataPlane input.");
    }

    private void TickCombat(float dt)
    {
        _phaseElapsed += dt;
        _attackCooldown -= dt;
        float progress = Math.Clamp(_phaseElapsed / 9.5f, 0f, 1f);
        MovePlayerArmyTowardEnemy(progress);

        if (_attackCooldown <= 0f)
        {
            Entity victim = FindFirstEnemyCombatEntity();
            if (victim != Entity.Null)
            {
                DamageAndMaybeDestroy(victim);
            }

            _attackCooldown = 0.32f;
        }

        int enemiesAlive = CountTeamNonAnchorEntities(EnemyTeamId);
        int destroyed = Math.Max(0, _startingEnemyCount - enemiesAlive);
        RefreshCounts(destroyed);
        _runtime.SetPhaseProgress(_startingEnemyCount <= 0 ? 1f : Math.Clamp(destroyed / (float)_startingEnemyCount, 0f, 1f));

        if (enemiesAlive == 0)
        {
            _runtime.EnterPhase(
                RtsCncFullMatchPhase.Victory,
                "Victory achieved. Volkov opponent eliminated by real ECS destruction.",
                "Victory: all Volkov non-anchor entities were destroyed.");
        }
    }

    private void MovePlayerArmyTowardEnemy(float progress)
    {
        _scratchEntities.Clear();
        GatherTeamEntities(PlayerTeamId, includeProducers: false, _scratchEntities);
        float centerX = Lerp(8800f, 17750f, progress);
        float centerY = Lerp(11900f, 10850f, progress);
        for (int i = 0; i < _scratchEntities.Count; i++)
        {
            Entity entity = _scratchEntities[i];
            float offsetX = ((i % 6) - 2.5f) * 190f;
            float offsetY = ((i / 6) % 4) * 170f;
            MoveToward(entity, centerX + offsetX, centerY + offsetY, 1800f);
        }
    }

    private void DamageAndMaybeDestroy(Entity entity)
    {
        string name = _engine.World.TryGet(entity, out Name resolved)
            ? resolved.Value
            : $"Entity {entity.Id}";
        if (!_engine.World.IsAlive(entity) || _engine.World.Has<PresentationDestroyPending>(entity))
        {
            return;
        }

        if (_engine.World.TryGet(entity, out AttributeBuffer attributes))
        {
            int healthId = AttributeRegistry.GetId("Health");
            if (healthId > 0 && attributes.HasAttribute(healthId))
            {
                attributes.SetCurrent(healthId, 0f);
                _engine.World.Set(entity, attributes);
            }
        }

        if (_engine.World.Has<PresentationStableId>(entity))
        {
            PresentationEntityLifecycle.RequestDestroy(_engine.World, entity, "RtsCncFull match combat victim");
        }
        else
        {
            _engine.World.Destroy(entity);
        }

        _runtime.Record($"Destroyed {name}.");
    }

    private void RefreshCounts(int? enemyDestroyedOverride = null)
    {
        int enemyUnits = CountTeamNonAnchorEntities(EnemyTeamId, producersOnly: false, unitsOnly: true);
        int enemyStructures = CountTeamNonAnchorEntities(EnemyTeamId, producersOnly: true);
        int playerArmy = CountTeamNonAnchorEntities(PlayerTeamId, producersOnly: false, unitsOnly: true);
        int destroyed = enemyDestroyedOverride ?? Math.Max(0, _startingEnemyCount - CountTeamNonAnchorEntities(EnemyTeamId));
        _runtime.SetCounts(playerArmy, enemyUnits, enemyStructures, destroyed);
    }

    private Entity FindFirstEnemyCombatEntity()
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Team, EntityTemplateKeyRef>();
        EntityTemplateKeyRegistry templateKeys = RequireTemplateKeys();
        _engine.World.Query(in query, (Entity entity, ref Team team, ref EntityTemplateKeyRef templateKey) =>
        {
            if (result != Entity.Null ||
                team.Id != EnemyTeamId ||
                _engine.World.Has<PresentationDestroyPending>(entity))
            {
                return;
            }

            string templateId = templateKeys.GetName(templateKey.TemplateKeyId);
            if (IsRtsCncFullCombatTemplate(templateId))
            {
                result = entity;
            }
        });

        return result;
    }

    private Entity FindEntityByName(string name)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name candidate) =>
        {
            if (result == Entity.Null &&
                string.Equals(candidate.Value, name, StringComparison.Ordinal) &&
                !_engine.World.Has<PresentationDestroyPending>(entity))
            {
                result = entity;
            }
        });

        return result;
    }

    private int CountEntitiesByName(string name)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name candidate) =>
        {
            if (string.Equals(candidate.Value, name, StringComparison.Ordinal) &&
                !_engine.World.Has<PresentationDestroyPending>(entity))
            {
                count++;
            }
        });

        return count;
    }

    private int CountTeamNonAnchorEntities(int teamId, bool producersOnly = false, bool unitsOnly = false)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Team, EntityTemplateKeyRef>();
        EntityTemplateKeyRegistry templateKeys = RequireTemplateKeys();
        _engine.World.Query(in query, (Entity entity, ref Team team, ref EntityTemplateKeyRef templateKey) =>
        {
            if (team.Id != teamId || _engine.World.Has<PresentationDestroyPending>(entity))
            {
                return;
            }

            string templateId = templateKeys.GetName(templateKey.TemplateKeyId);
            if (!IsRtsCncFullCombatTemplate(templateId))
            {
                return;
            }

            bool isProducer = templateId.EndsWith("_producer", StringComparison.Ordinal);
            if (producersOnly && !isProducer)
            {
                return;
            }

            if (unitsOnly && isProducer)
            {
                return;
            }

            count++;
        });

        return count;
    }

    private void GatherTeamEntities(int teamId, bool includeProducers, List<Entity> output)
    {
        var query = new QueryDescription().WithAll<Team, EntityTemplateKeyRef, WorldPositionCm>();
        EntityTemplateKeyRegistry templateKeys = RequireTemplateKeys();
        _engine.World.Query(in query, (Entity entity, ref Team team, ref EntityTemplateKeyRef templateKey, ref WorldPositionCm _) =>
        {
            if (team.Id != teamId || _engine.World.Has<PresentationDestroyPending>(entity))
            {
                return;
            }

            string templateId = templateKeys.GetName(templateKey.TemplateKeyId);
            if (!IsRtsCncFullCombatTemplate(templateId))
            {
                return;
            }

            if (!includeProducers && templateId.EndsWith("_producer", StringComparison.Ordinal))
            {
                return;
            }

            output.Add(entity);
        });
    }

    private EntityTemplateKeyRegistry RequireTemplateKeys()
    {
        return _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("RtsCncFull match flow requires EntityTemplateKeyRegistry.");
    }

    private static bool IsRtsCncFullCombatTemplate(string templateId)
    {
        return templateId.StartsWith("rts_cnc_full_", StringComparison.Ordinal) &&
               !templateId.EndsWith("_anchor", StringComparison.Ordinal);
    }

    private void SetAttributeCurrent(Entity entity, string attributeName, float value)
    {
        if (entity == Entity.Null ||
            !_engine.World.IsAlive(entity) ||
            !_engine.World.TryGet(entity, out AttributeBuffer attributes))
        {
            return;
        }

        int attributeId = AttributeRegistry.GetId(attributeName);
        if (attributeId <= 0)
        {
            return;
        }

        attributes.SetCurrent(attributeId, value);
        _engine.World.Set(entity, attributes);
    }

    private void MoveEntityIfAlive(Entity entity, float x, float y)
    {
        if (entity == Entity.Null || !_engine.World.IsAlive(entity) || !_engine.World.Has<WorldPositionCm>(entity))
        {
            return;
        }

        ref WorldPositionCm position = ref _engine.World.Get<WorldPositionCm>(entity);
        position.Value = Fix64Vec2.FromFloat(x, y);
    }

    private void MoveToward(Entity entity, float targetX, float targetY, float speedCmPerSecond)
    {
        if (entity == Entity.Null || !_engine.World.IsAlive(entity) || !_engine.World.Has<WorldPositionCm>(entity))
        {
            return;
        }

        ref WorldPositionCm position = ref _engine.World.Get<WorldPositionCm>(entity);
        float x = position.Value.X.ToFloat();
        float y = position.Value.Y.ToFloat();
        float dx = targetX - x;
        float dy = targetY - y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0.1f)
        {
            return;
        }

        float step = MathF.Min(distance, speedCmPerSecond / 60f);
        position.Value = Fix64Vec2.FromFloat(x + dx / distance * step, y + dy / distance * step);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
