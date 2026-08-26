using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using CapabilityStandardAttachmentSettlementMod;
using CapabilityStandardAttachmentSettlementMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardAttachmentSettlementAcceptanceTests
{
    private const string BindingName = "capability_standard_attachment_settlement";
    private const string PresetId = "capability_standard_attachment_settlement_raylib";
    private const string ShowcaseModId = "CapabilityStandardAttachmentSettlementMod";
    private const string MapId = "capability_standard_attachment_settlement";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void Settlement_StaticParentPosesRemainStableAcrossTicks()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        AssertLauncher(repoRoot);

        using GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);

        var frameTimes = new List<double>();
        AttachmentSettlementDemoState state = RequireState(engine);
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimes,
            () => state.PosesStable,
            maxFrames: 30);

        Entity hall = RequireByName(engine.World, "Attachment.Settlement.Hall");
        Entity annex = RequireByName(engine.World, "Attachment.Settlement.Annex");
        Entity tower = RequireByName(engine.World, "Attachment.Settlement.Tower");

        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Get<ChildOf>(annex).Parent, Is.EqualTo(hall));
            Assert.That(engine.World.Get<ChildOf>(tower).Parent, Is.EqualTo(hall));
            Assert.That(engine.World.Get<WorldPositionCm>(hall).Value.X.ToFloat(), Is.EqualTo(5000f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(hall).Value.Y.ToFloat(), Is.EqualTo(5000f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(annex).Value.X.ToFloat(), Is.EqualTo(5700f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(annex).Value.Y.ToFloat(), Is.EqualTo(5000f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(tower).Value.X.ToFloat(), Is.EqualTo(4650f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(tower).Value.Y.ToFloat(), Is.EqualTo(5600f).Within(2f));
            Assert.That(state.StableTicks, Is.GreaterThanOrEqualTo(3));
            Assert.That(state.Caption, Does.Contain("静物验收"));
        });

        WriteReceipt(repoRoot, frameTimes.Count);
    }

    private static AttachmentSettlementDemoState RequireState(GameEngine engine)
    {
        return engine.GetService(CapabilityStandardAttachmentSettlementModEntry.DemoStateKey)
            ?? throw new InvalidOperationException("Settlement DemoState missing.");
    }

    private static Entity RequireByName(World world, string name)
    {
        Entity found = Entity.Null;
        world.Query(in new QueryDescription().WithAll<Name>(), (Entity entity, ref Name componentName) =>
        {
            if (found == Entity.Null && string.Equals(componentName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' must exist.");
        return found;
    }

    private static void AssertLauncher(string repoRoot)
    {
        string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        Assert.That(launcherConfig, Does.Contain($"\"name\": \"{BindingName}\""));
        string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        Assert.That(launcherPresets, Does.Contain($"\"id\": \"{PresetId}\""));
    }

    private static void WriteReceipt(string repoRoot, int frames)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-attachment-settlement");
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(
            Path.Combine(artifactDir, "acceptance.txt"),
            $"binding={BindingName}{Environment.NewLine}map={MapId}{Environment.NewLine}frames={frames}{Environment.NewLine}");
    }
}
