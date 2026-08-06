using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using CombatStanceBehaviorMod.Components;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class CombatStanceShowcasePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "combat_stance_showcase";

    [Test]
    public void CombatStanceShowcase_UsesParticipantRelationshipsAndRunsPlayableStanceOrders()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("combat_stance_showcase"));

        World world = engine.World;
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
        int attackTargetOrderTypeId = orderTypes.GetId(StanceOrderKeys.AttackTarget);
        int moveToOrderTypeId = orderTypes.GetId(StanceOrderKeys.MoveTo);

        Entity vanguard = FindEntity(world, "Combat Stance Vanguard");
        Entity roadblock = FindEntity(world, "Combat Stance Roadblock");
        Entity guardian = FindEntity(world, "Combat Stance Guardian");
        Entity ward = FindEntity(world, "Combat Stance Ward");
        Entity guardThreat = FindEntity(world, "Combat Stance Guard Threat");
        Entity switchUnit = FindEntity(world, "Combat Stance Switch Unit");
        Entity switchAttacker = FindEntity(world, "Combat Stance Switch Attacker");
        Entity priorityUnit = FindEntity(world, "Combat Stance Priority Unit");
        Entity priorityCritical = FindEntity(world, "Combat Stance Priority Critical Far");

        AssertParticipantRelationships(engine);
        Assert.That(world.Has<OrderBuffer>(switchUnit), Is.True, "switch unit should load with OrderBuffer.");
        Assert.That(world.Has<CombatStanceState>(switchUnit), Is.True, "switch unit should load with CombatStanceState.");

        Order vanguardAttack = TickUntilActiveOrder(engine, world, vanguard, attackTargetOrderTypeId, roadblock, maxFrames: 60);
        Assert.That(vanguardAttack.Target, Is.EqualTo(roadblock));

        Order guardianAttack = TickUntilActiveOrder(engine, world, guardian, attackTargetOrderTypeId, guardThreat, maxFrames: 60);
        Assert.That(guardianAttack.Target, Is.EqualTo(guardThreat));

        world.Destroy(roadblock);
        OrderSubmitter.NotifyOrderComplete(world, vanguard, orderTypes);
        Order resumedMove = TickUntilActiveOrder(engine, world, vanguard, moveToOrderTypeId, Entity.Null, maxFrames: 60);
        Assert.Multiple(() =>
        {
            Assert.That((int)resumedMove.Args.Spatial.WorldCm.X, Is.EqualTo(2200));
            Assert.That((int)resumedMove.Args.Spatial.WorldCm.Z, Is.EqualTo(1000));
        });

        world.Destroy(guardThreat);
        OrderSubmitter.NotifyOrderComplete(world, guardian, orderTypes);
        Order guardFollow = TickUntilActiveOrder(engine, world, guardian, moveToOrderTypeId, Entity.Null, maxFrames: 60);
        Assert.Multiple(() =>
        {
            Assert.That(guardFollow.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(guardFollow.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.Single));
            Assert.That((int)guardFollow.Args.Spatial.WorldCm.X, Is.EqualTo(ReadX(world, ward)));
            Assert.That((int)guardFollow.Args.Spatial.WorldCm.Z, Is.EqualTo(ReadY(world, ward)));
        });

        PublishDamage(engine, switchAttacker, switchUnit);
        Tick(engine, 3);
        AssertActiveOrderIsNot(world, switchUnit, attackTargetOrderTypeId);

        SubmitSetCombatStance(engine, switchUnit, CombatStances.ReturnFire, leashRadiusCm: 900, retaliationTtlSteps: 180);
        TickUntilStance(engine, world, switchUnit, CombatStances.ReturnFire, maxFrames: 60);
        PublishDamage(engine, switchAttacker, switchUnit);
        Order retaliation = TickUntilActiveOrder(engine, world, switchUnit, attackTargetOrderTypeId, switchAttacker, maxFrames: 60);
        Assert.That(retaliation.Target, Is.EqualTo(switchAttacker));

        SubmitSetCombatStance(engine, switchUnit, CombatStances.HoldFire, leashRadiusCm: 0, retaliationTtlSteps: 0);
        TickUntilStance(engine, world, switchUnit, CombatStances.HoldFire, maxFrames: 60);
        PublishDamage(engine, switchAttacker, switchUnit);
        Tick(engine, 3);
        AssertActiveOrderIsNot(world, switchUnit, attackTargetOrderTypeId);

        SubmitSetCombatStance(engine, priorityUnit, CombatStances.AttackAnything, leashRadiusCm: 900, retaliationTtlSteps: 180);
        TickUntilStance(engine, world, priorityUnit, CombatStances.AttackAnything, maxFrames: 60);
        Order priorityAttack = TickUntilActiveOrder(engine, world, priorityUnit, attackTargetOrderTypeId, priorityCritical, maxFrames: 60);
        Assert.That(priorityAttack.Target, Is.EqualTo(priorityCritical));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "CombatStanceBehaviorMod",
                "CombatStanceShowcaseMod"
            }),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);
        return engine;
    }

    private static void AssertParticipantRelationships(GameEngine engine)
    {
        TeamEntityLookup teams = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException("TeamEntityLookup missing.");
        PlayerEntityLookup players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        RelationshipTypeRegistry types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");

        Assert.That(teams.TryGet(1, out Entity friendlyTeam), Is.True);
        Assert.That(teams.TryGet(2, out Entity hostileTeam), Is.True);
        Assert.That(players.TryGet(1, out Entity localPlayer), Is.True);
        Assert.That(players.TryGet(2, out Entity hostilePlayer), Is.True);

        int participantTypeId = types.GetId("CombatStance.Participant");
        int hostileTypeId = types.GetId("CombatStance.Hostile");
        Assert.Multiple(() =>
        {
            Assert.That(relationships.HasLink(friendlyTeam, hostileTeam, participantTypeId), Is.True);
            Assert.That(relationships.HasLink(hostileTeam, friendlyTeam, participantTypeId), Is.True);
            Assert.That(relationships.HasLink(friendlyTeam, hostileTeam, hostileTypeId), Is.True);
            Assert.That(relationships.HasLink(hostileTeam, friendlyTeam, hostileTypeId), Is.True);
            Assert.That(relationships.HasLink(localPlayer, hostilePlayer, participantTypeId), Is.True);
            Assert.That(relationships.HasLink(hostilePlayer, localPlayer, participantTypeId), Is.True);
            Assert.That(relationships.HasLink(localPlayer, friendlyTeam, participantTypeId), Is.True);
            Assert.That(relationships.HasLink(hostilePlayer, hostileTeam, participantTypeId), Is.True);
        });
    }

    private static Order TickUntilActiveOrder(
        GameEngine engine,
        World world,
        Entity actor,
        int orderTypeId,
        Entity expectedTarget,
        int maxFrames)
    {
        Order last = default;
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (!world.IsAlive(actor) || !world.Has<OrderBuffer>(actor))
            {
                continue;
            }

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            if (!buffer.HasActive)
            {
                continue;
            }

            last = buffer.ActiveOrder.Order;
            if (last.OrderTypeId == orderTypeId &&
                (expectedTarget == Entity.Null || last.Target == expectedTarget))
            {
                return last;
            }
        }

        Assert.Fail($"Expected active order {orderTypeId} target {expectedTarget}; last active order was {last.OrderTypeId} target {last.Target}.");
        return default;
    }

    private static void TickUntilStance(GameEngine engine, World world, Entity actor, int stance, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (world.IsAlive(actor) &&
                world.Has<CombatStanceState>(actor) &&
                world.Get<CombatStanceState>(actor).Stance == stance)
            {
                return;
            }
        }

        int current = world.IsAlive(actor) && world.Has<CombatStanceState>(actor)
            ? world.Get<CombatStanceState>(actor).Stance
            : -1;
        Assert.Fail($"Expected combat stance {stance}; current stance was {current}.");
    }

    private static void SubmitSetCombatStance(
        GameEngine engine,
        Entity actor,
        int stance,
        int leashRadiusCm,
        int retaliationTtlSteps)
    {
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue missing.");

        var order = new Order
        {
            Actor = actor,
            OrderTypeId = orderTypes.GetId(StanceOrderKeys.SetCombatStance),
            SubmitMode = OrderSubmitMode.Immediate
        };
        CombatStanceOrderPayload.ConfigureSetCombatStance(
            ref order,
            stance,
            leashRadiusCm,
            retaliationTtlSteps);

        if (!orders.TryEnqueue(in order))
        {
            throw new InvalidOperationException("Could not enqueue setCombatStance order.");
        }
    }

    private static void PublishDamage(GameEngine engine, Entity source, Entity target)
    {
        int tagId = TagRegistry.GetId("Event.DamageTaken");
        Assert.That(tagId, Is.GreaterThan(0));
        engine.EventBus.Publish(new GameplayEvent
        {
            TagId = tagId,
            Source = source,
            Target = target
        });
    }

    private static void AssertActiveOrderIsNot(World world, Entity actor, int orderTypeId)
    {
        if (!world.IsAlive(actor) || !world.Has<OrderBuffer>(actor))
        {
            return;
        }

        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        if (!buffer.HasActive)
        {
            return;
        }

        Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.Not.EqualTo(orderTypeId));
    }

    private static int ReadX(World world, Entity entity)
    {
        return world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2().X;
    }

    private static int ReadY(World world, Entity entity)
    {
        return world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2().Y;
    }

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });

        if (result == Entity.Null)
        {
            throw new InvalidOperationException($"Missing entity '{entityName}'.");
        }

        return result;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        GasClockStepPolicy stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            engine.Tick(DeltaTime);
        }
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
