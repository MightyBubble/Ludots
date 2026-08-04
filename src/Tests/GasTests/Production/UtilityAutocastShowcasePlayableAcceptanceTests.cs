using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
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
public sealed class UtilityAutocastShowcasePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "utility_autocast_showcase";

    [Test]
    public void UtilityAutocastShowcase_HealsLowestFriendlyAndBlocksOtherAutocastsBySharedGcd()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("utility_autocast_showcase"));

        World world = engine.World;
        Entity mage = FindEntity(world, "Utility Autocast Mage");
        Entity injuredAlly = FindEntity(world, "Utility Autocast Injured Ally");
        Entity healthyAlly = FindEntity(world, "Utility Autocast Healthy Ally");
        Entity brute = FindEntity(world, "Utility Autocast Enemy Brute");
        Entity scout = FindEntity(world, "Utility Autocast Enemy Scout");

        AssertParticipantRelationships(engine);

        float injuredBefore = ReadHealth(world, injuredAlly);
        float healthyBefore = ReadHealth(world, healthyAlly);
        float bruteBefore = ReadHealth(world, brute);
        float scoutBefore = ReadHealth(world, scout);

        UtilityAiDecisionTrace submittedTrace = TickUntilSubmittedOrder(engine, world, mage, injuredAlly, maxFrames: 12);
        Assert.Multiple(() =>
        {
            Assert.That(submittedTrace.CandidateCount, Is.GreaterThan(0));
            Assert.That(submittedTrace.LastSubmittedOrderId, Is.GreaterThan(0));
            Assert.That(submittedTrace.BestTarget, Is.EqualTo(injuredAlly));
        });

        Tick(engine, 5);

        Assert.That(ReadHealth(world, injuredAlly), Is.GreaterThan(injuredBefore));
        Assert.That(ReadHealth(world, healthyAlly), Is.GreaterThan(healthyBefore));
        Assert.That(ReadHealth(world, brute), Is.EqualTo(bruteBefore));
        Assert.That(ReadHealth(world, scout), Is.EqualTo(scoutBefore));
        Assert.That(EntityHasTag(world, mage, "Cooldown.UtilityAutocast.GCD"), Is.True);

        UtilityAiDecisionTrace blockedTrace = TickUntilTaskStatus(
            engine,
            world,
            mage,
            UtilityAiTaskRunStatus.Blocked,
            maxFrames: 20);
        Assert.That(
            blockedTrace.LastTaskStatus,
            Is.EqualTo((int)UtilityAiTaskRunStatus.Blocked));

        engine.TriggerManager.FireEventAsync(new EventKey("AIInspector.PrintAiConfig"), engine.CreateContext())
            .GetAwaiter()
            .GetResult();
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "AIInspectorMod",
                "UtilityAutocastShowcaseMod"
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
        int participantTypeId = types.GetId("UtilityAutocast.Participant");
        Assert.That(relationships.HasLink(friendlyTeam, hostileTeam, participantTypeId), Is.True);
        Assert.That(relationships.HasLink(hostileTeam, friendlyTeam, participantTypeId), Is.True);
        Assert.That(relationships.HasLink(localPlayer, hostilePlayer, participantTypeId), Is.True);
        Assert.That(relationships.HasLink(hostilePlayer, localPlayer, participantTypeId), Is.True);
        Assert.That(relationships.HasLink(localPlayer, friendlyTeam, participantTypeId), Is.True);
        Assert.That(relationships.HasLink(hostilePlayer, hostileTeam, participantTypeId), Is.True);
        Assert.That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Hostile));
    }

    private static UtilityAiDecisionTrace TickUntilSubmittedOrder(
        GameEngine engine,
        World world,
        Entity entity,
        Entity expectedTarget,
        int maxFrames)
    {
        UtilityAiDecisionTrace last = default;
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            last = world.Get<UtilityAiDecisionTrace>(entity);
            if (last.LastSubmittedOrderId > 0 &&
                last.CandidateCount > 0 &&
                last.BestTarget == expectedTarget)
            {
                return last;
            }
        }

        Assert.Fail($"Utility AI did not submit the expected order; last submitted={last.LastSubmittedOrderId}, candidates={last.CandidateCount}, target={last.BestTarget}.");
        return default;
    }

    private static UtilityAiDecisionTrace TickUntilTaskStatus(
        GameEngine engine,
        World world,
        Entity entity,
        UtilityAiTaskRunStatus expected,
        int maxFrames)
    {
        UtilityAiDecisionTrace last = default;
        int expectedValue = (int)expected;
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            last = world.Get<UtilityAiDecisionTrace>(entity);
            if (last.LastTaskStatus == expectedValue)
            {
                return last;
            }
        }

        Assert.Fail($"Utility AI did not report task status {expected}; last status={last.LastTaskStatus}, candidates={last.CandidateCount}, submitted={last.LastSubmittedOrderId}.");
        return default;
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

    private static float ReadHealth(World world, Entity entity)
    {
        int healthId = AttributeRegistry.GetId("Health");
        Assert.That(healthId, Is.GreaterThanOrEqualTo(0));
        Assert.That(world.Has<AttributeBuffer>(entity), Is.True);
        return world.Get<AttributeBuffer>(entity).GetCurrent(healthId);
    }

    private static bool EntityHasTag(World world, Entity entity, string tagName)
    {
        int tagId = TagRegistry.GetId(tagName);
        Assert.That(tagId, Is.GreaterThan(0));
        return world.TryGet(entity, out GameplayTagContainer tags) && tags.HasTag(tagId);
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
