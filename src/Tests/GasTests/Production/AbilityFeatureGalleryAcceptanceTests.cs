using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CapabilityStandardAbilityFeatureGalleryMod.Runtime;
using Ludots.Core.Engine;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class AbilityFeatureGalleryAcceptanceTests
{
    [Test]
    public void EffectSignal_DropsTargetHealthOnce()
    {
        using AbilityFeatureGalleryRuntime runtime = Play("EffectSignal");
        Assert.That(runtime.Title, Is.EqualTo("点一下就打中"));
        AssertHealthDelta(runtime, target: -25);
        AssertBannedPlayerCopy(runtime.Metrics.Detail);
    }

    [Test]
    public void DispatchTarget_HitsCasterNotDummy()
    {
        using AbilityFeatureGalleryRuntime runtime = Play("DispatchTarget");
        AssertHealthDelta(runtime, caster: -20, target: 0);
        Assert.That(runtime.Metrics.Detail, Does.Contain("自己"));
    }

    [Test]
    public void BlockTagsBlocked_SecondCastDoesNotHitAgain()
    {
        using AbilityFeatureGalleryRuntime runtime = Play("BlockTagsBlocked");
        AssertHealthDelta(runtime, target: -25);
        Assert.That(runtime.Metrics.SecondCast, Is.EqualTo("rejected"));
        Assert.That(runtime.ActorHasTag("caster", "Cooldown.AbilityFeature.Lock"), Is.True);
    }

    [Test]
    public void EveryCoveredFeature_HasVignetteAbilityAndUniqueShowcaseId()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        string assets = Path.Combine(repoRoot, AbilityFeatureIds.ModAssetsRelative);
        using JsonDocument coverage = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "assets/GAS/ability_feature_coverage.registry.json")));
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in coverage.RootElement.GetProperty("entries").EnumerateArray())
        {
            string feature = entry.GetProperty("feature").GetString()!;
            string expectedId = AbilityFeatureIds.ShowcaseId(feature);
            if (!seen.Add(feature))
            {
                missing.Add($"{feature}: coverage feature must be unique");
            }

            if (entry.GetProperty("showcaseId").GetString() != expectedId)
            {
                missing.Add($"{feature}: coverage showcaseId must be {expectedId}");
            }

            if (entry.GetProperty("status").GetString() != "covered")
            {
                missing.Add($"{feature}: coverage status must be covered");
            }

            if (!File.Exists(Path.Combine(assets, "Vignettes", feature + ".json")))
            {
                missing.Add($"{feature}: missing Vignettes/{feature}.json");
            }

            if (!File.Exists(Path.Combine(assets, "Maps", expectedId + ".json")))
            {
                missing.Add($"{feature}: missing Maps/{expectedId}.json");
            }
        }

        Assert.That(coverage.RootElement.GetProperty("entries").GetArrayLength(), Is.GreaterThanOrEqualTo(20));
        Assert.That(missing, Is.Empty, "Ability feature galleries incomplete:\n" + string.Join("\n", missing));
    }

    [Test]
    public void CoveredFeatures_PlayAndMatchExpect()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using JsonDocument coverage = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "assets/GAS/ability_feature_coverage.registry.json")));
        var failures = new List<string>();
        foreach (JsonElement entry in coverage.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (entry.GetProperty("status").GetString() != "covered")
            {
                continue;
            }

            string feature = entry.GetProperty("feature").GetString()!;
            try
            {
                using AbilityFeatureGalleryRuntime runtime = Play(feature);
                AssertExpect(runtime);
                AssertBannedPlayerCopy(runtime.Metrics.Detail);
                foreach (string phrase in runtime.Vignette.AssertDetailContains)
                {
                    Assert.That(runtime.Metrics.Detail, Does.Contain(phrase), feature);
                }
            }
            catch (Exception ex)
            {
                failures.Add(feature + ": " + ex.Message);
            }
        }

        Assert.That(failures, Is.Empty, "Ability feature plays failed:\n" + string.Join("\n", failures));
    }

    [Test]
    public void ChampionSkillSandbox_IsNotTheAbilityFeatureEntry()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        string guide = File.ReadAllText(Path.Combine(repoRoot, "gitbook/showcases/README.md"));
        Assert.That(guide, Does.Contain("capability_standard_ability_feature_"));
        Assert.That(guide, Does.Contain("组合戏"));
        using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
        foreach (JsonElement showcase in registry.RootElement.GetProperty("showcases").EnumerateArray())
        {
            if (showcase.GetProperty("id").GetString() != "champion_skill_sandbox")
            {
                continue;
            }

            Assert.That(showcase.GetProperty("summary").GetString(), Does.Contain("组合戏"));
            return;
        }

        Assert.Fail("champion_skill_sandbox is missing from showcase.registry.json.");
    }

    private static AbilityFeatureGalleryRuntime Play(string feature)
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(
            repoRoot,
            new[]
            {
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "CapabilityStandardAbilityFeatureGalleryMod"
            });
        var runtime = new AbilityFeatureGalleryRuntime();
        runtime.BindFeature(feature);
        runtime.AttachEngine(engine, ownsEngine: true);
        engine.RegisterSystem(
            new AbilityFeatureGallerySimulationSystem(engine, runtime),
            SystemGroup.PostMovement);
        engine.LoadMap(AbilityFeatureIds.MapId(feature));
        runtime.EnsureActors();
        runtime.PlayUntilSettled();
        return runtime;
    }

    private static void AssertExpect(AbilityFeatureGalleryRuntime runtime)
    {
        AbilityFeatureExpect expect = runtime.Vignette.Expect;
        AbilityFeatureMetrics metrics = runtime.Metrics;
        if (expect.TargetHealthDelta is float targetDelta)
        {
            Assert.That(metrics.TargetAfter - metrics.TargetBefore, Is.EqualTo(targetDelta).Within(0.01f), runtime.Feature);
        }

        if (expect.Target2HealthDelta is float target2Delta)
        {
            Assert.That(metrics.Target2After - metrics.Target2Before, Is.EqualTo(target2Delta).Within(0.01f), runtime.Feature);
        }

        if (expect.WoundedHealthDelta is float woundedDelta)
        {
            Assert.That(metrics.WoundedAfter - metrics.WoundedBefore, Is.EqualTo(woundedDelta).Within(0.01f), runtime.Feature);
        }

        if (expect.CasterHealthDelta is float casterDelta)
        {
            Assert.That(metrics.CasterAfter - metrics.CasterBefore, Is.EqualTo(casterDelta).Within(0.01f), runtime.Feature);
        }

        if (expect.TargetHealthMax is float max)
        {
            Assert.That(metrics.TargetAfter, Is.LessThanOrEqualTo(max), runtime.Feature);
        }

        if (expect.WaitedForGate == true)
        {
            Assert.That(metrics.WaitedForGate, Is.True, runtime.Feature);
        }

        if (expect.Interrupted == true)
        {
            Assert.That(metrics.Interrupted, Is.True, runtime.Feature);
        }

        if (expect.TriggerGraphFired == true)
        {
            Assert.That(metrics.TriggerGraphFired, Is.True, runtime.Feature);
        }

        if (expect.EventCountMin is int minEvents)
        {
            Assert.That(metrics.EventCount, Is.GreaterThanOrEqualTo(minEvents), runtime.Feature);
        }

        if (expect.VisibleBeforeCount is int before)
        {
            Assert.That(metrics.VisibleBeforeCount, Is.EqualTo(before), runtime.Feature);
        }

        if (expect.VisibleAfterCount is int after)
        {
            Assert.That(metrics.VisibleAfterCount, Is.EqualTo(after), runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.Slot0After))
        {
            Assert.That(metrics.Slot0After, Is.EqualTo(expect.Slot0After), runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.FirstCast))
        {
            Assert.That(metrics.FirstCast, Is.EqualTo(expect.FirstCast), runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.SecondCast))
        {
            Assert.That(metrics.SecondCast, Is.EqualTo(expect.SecondCast), runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.CasterHasTag))
        {
            Assert.That(runtime.ActorHasTag("caster", expect.CasterHasTag), Is.True, runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.CasterLacksTag))
        {
            Assert.That(runtime.ActorHasTag("caster", expect.CasterLacksTag), Is.False, runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.TargetHasTag))
        {
            Assert.That(runtime.ActorHasTag("target", expect.TargetHasTag), Is.True, runtime.Feature);
        }

        if (!string.IsNullOrWhiteSpace(expect.TargetLacksTag))
        {
            Assert.That(runtime.ActorHasTag("target", expect.TargetLacksTag), Is.False, runtime.Feature);
        }
    }

    private static void AssertHealthDelta(AbilityFeatureGalleryRuntime runtime, float? caster = null, float? target = null)
    {
        if (caster is float casterDelta)
        {
            Assert.That(runtime.Metrics.CasterAfter - runtime.Metrics.CasterBefore, Is.EqualTo(casterDelta).Within(0.01f));
        }

        if (target is float targetDelta)
        {
            Assert.That(runtime.Metrics.TargetAfter - runtime.Metrics.TargetBefore, Is.EqualTo(targetDelta).Within(0.01f));
        }
    }

    private static void AssertBannedPlayerCopy(string detail)
    {
        string[] banned = { "opcode", "ECS", "fallback", "TODO", "FIXME" };
        foreach (string word in banned)
        {
            Assert.That(detail, Does.Not.Contain(word));
        }
    }
}
