using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using CapabilityStandardAttachmentMountDismountMod;
using CapabilityStandardAttachmentMountDismountMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Movement;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardAttachmentMountDismountAcceptanceTests
{
    private const string BindingName = "capability_standard_attachment_mount_dismount";
    private const string PresetId = "capability_standard_attachment_mount_dismount_raylib";
    private const string ShowcaseModId = "CapabilityStandardAttachmentMountDismountMod";
    private const string MapId = "capability_standard_attachment_mount_dismount";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void MountDismount_AttachRideThenPerimeterDetach()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        AssertLauncher(repoRoot);

        using GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);

        var frameTimes = new List<double>();
        AttachmentMountDemoState state = RequireState(engine);

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimes,
            () => state.Phase == AttachmentMountPhase.Ride && state.RiderAttached,
            maxFrames: 40);

        Entity carrier = RequireByName(engine.World, "Attachment.Mount.Carrier");
        Entity rider = RequireByName(engine.World, "Attachment.Mount.Rider");
        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Get<ChildOf>(rider).Parent, Is.EqualTo(carrier));
            Assert.That(engine.World.Has<AttachedLocalPose>(rider), Is.True);
            Assert.That(engine.World.Get<PoseAuthority>(rider).Value, Is.EqualTo(PoseAuthorityKind.Attached));
        });

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimes,
            () => state.Phase == AttachmentMountPhase.Done,
            maxFrames: 300);
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimes);

        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Has<ChildOf>(rider), Is.False);
            Assert.That(engine.World.Has<AttachedLocalPose>(rider), Is.False);
            Assert.That(engine.World.Get<PoseAuthority>(rider).Value, Is.EqualTo(PoseAuthorityKind.Nav));
            Assert.That(engine.World.Get<WorldPositionCm>(carrier).Value.X.ToFloat(), Is.EqualTo(3500f).Within(2f));
            float riderX = engine.World.Get<WorldPositionCm>(rider).Value.X.ToFloat();
            float riderY = engine.World.Get<WorldPositionCm>(rider).Value.Y.ToFloat();
            float dx = riderX - 3500f;
            float dy = riderY - 0f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            Assert.That(dist, Is.EqualTo(260f).Within(4f), "下车应落在载具周界环上");
            Assert.That(state.Caption, Does.Contain("上下车完成"));
        });

        WriteReceipt(repoRoot, frameTimes.Count);
    }

    private static AttachmentMountDemoState RequireState(GameEngine engine)
    {
        return engine.GetService(CapabilityStandardAttachmentMountDismountModEntry.DemoStateKey)
            ?? throw new InvalidOperationException("Mount DemoState missing.");
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
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-attachment-mount-dismount");
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(
            Path.Combine(artifactDir, "acceptance.txt"),
            $"binding={BindingName}{Environment.NewLine}map={MapId}{Environment.NewLine}frames={frames}{Environment.NewLine}");
    }
}
