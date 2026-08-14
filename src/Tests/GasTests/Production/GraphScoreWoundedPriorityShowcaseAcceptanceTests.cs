using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphScoreShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class GraphScoreWoundedPriorityShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    [Test]
    public void GraphScoreShowcase_WoundedDummyScoresHigherAndTakesTheHit()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(GraphScoreShowcaseContract.MapId);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain(GraphScoreShowcaseContract.MapId));

        World world = engine.World;
        Entity caster = FindEntity(world, GraphScoreShowcaseContract.CasterName);
        Entity fullDummy = FindEntity(world, GraphScoreShowcaseContract.FullDummyName);
        Entity woundedDummy = FindEntity(world, GraphScoreShowcaseContract.WoundedDummyName);

        float fullBefore = ReadHealth(world, fullDummy);
        float woundedBefore = ReadHealth(world, woundedDummy);
        Assert.That(woundedBefore, Is.LessThan(fullBefore));

        int strikeAbilityId = AbilityIdRegistry.GetId(GraphScoreShowcaseContract.AbilityKey);
        UtilityAiDecisionTrace submitted = TickUntilSubmitted(engine, world, caster, strikeAbilityId, maxFrames: 12);
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("残血打分短剧需要屏幕字幕缓冲。");
        Assert.Multiple(() =>
        {
            Assert.That(submitted.CandidateCount, Is.GreaterThan(0));
            Assert.That(submitted.LastSubmittedAbilityId, Is.EqualTo(strikeAbilityId));
            Assert.That(submitted.BestTarget, Is.EqualTo(woundedDummy));
            Assert.That(submitted.BestScore, Is.GreaterThan(0f));
            Assert.That(OverlayContainsText(overlay, GraphScoreShowcaseContract.PlayerTitle), Is.True);
            Assert.That(OverlayContainsText(overlay, GraphScoreShowcaseContract.WoundedDummyName), Is.True);
            Assert.That(OverlayContainsText(overlay, "这一刀打向残血木桩"), Is.True);
            Assert.That(OverlayContainsText(overlay, $"分 {submitted.BestScore:0}"), Is.True);
        });

        Tick(engine, 5);
        Assert.That(ReadHealth(world, woundedDummy), Is.LessThan(woundedBefore));
        Assert.That(ReadHealth(world, fullDummy), Is.EqualTo(fullBefore));
        Assert.That(OverlayContainsText(overlay, "这一刀打向残血木桩"), Is.True);
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
                "CoreInputMod",
                "CapabilityStandardGraphScoreShowcaseMod"
            }),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);
        return engine;
    }

    private static UtilityAiDecisionTrace TickUntilSubmitted(
        GameEngine engine,
        World world,
        Entity entity,
        int abilityId,
        int maxFrames)
    {
        UtilityAiDecisionTrace last = default;
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            last = world.Get<UtilityAiDecisionTrace>(entity);
            if (last.LastSubmittedAbilityId == abilityId && last.CandidateCount > 0)
            {
                return last;
            }
        }

        Assert.Fail(
            $"打分短剧没有出手；last submitted={last.LastSubmittedAbilityId}, candidates={last.CandidateCount}, readiness={last.LastReadinessBlockReason}, best={last.BestTarget}.");
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

    private static bool OverlayContainsText(ScreenOverlayBuffer overlay, string expected)
    {
        foreach (ref readonly var item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text) &&
                text.Contains(expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
