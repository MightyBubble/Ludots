using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using CapabilityStandardAttachmentVehicleParadeMod;
using CapabilityStandardAttachmentVehicleParadeMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardAttachmentVehicleParadeAcceptanceTests
{
    private const string BindingName = "capability_standard_attachment_vehicle_parade";
    private const string PresetId = "capability_standard_attachment_vehicle_parade_raylib";
    private const string ShowcaseModId = "CapabilityStandardAttachmentVehicleParadeMod";
    private const string MapId = "capability_standard_attachment_vehicle_parade";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void VehicleParade_ChassisDriveThenTurretAim_BarrelFollows()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        AssertLauncher(repoRoot);

        using GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);

        var frameTimes = new List<double>();
        AttachmentVehicleParadeDemoState state = RequireState(engine);
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimes,
            () => state.Phase == AttachmentVehicleParadePhase.Done,
            maxFrames: 120);

        // DemoState 在 InputCollection 快照，子位姿在 PostMovement 派生：多推一帧对齐世界坐标。
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimes);

        Entity chassis = RequireByName(engine.World, "Attachment.Vehicle.Chassis");
        Entity turret = RequireByName(engine.World, "Attachment.Vehicle.Turret");
        Entity barrel = RequireByName(engine.World, "Attachment.Vehicle.Barrel");

        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Get<ChildOf>(turret).Parent, Is.EqualTo(chassis));
            Assert.That(engine.World.Get<ChildOf>(barrel).Parent, Is.EqualTo(turret));
            Assert.That(engine.World.Get<WorldPositionCm>(chassis).Value.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(turret).Value.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
            Assert.That(engine.World.Get<FacingDirection>(turret).AngleRad, Is.EqualTo(MathF.PI / 2f).Within(1e-4f));
            Assert.That(engine.World.Get<WorldPositionCm>(barrel).Value.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
            Assert.That(engine.World.Get<WorldPositionCm>(barrel).Value.Y.ToFloat(), Is.EqualTo(220f).Within(2f));
            Assert.That(state.Caption, Does.Contain("阅兵完成"));
        });

        WriteReceipt(repoRoot, frameTimes.Count);
    }

    private static AttachmentVehicleParadeDemoState RequireState(GameEngine engine)
    {
        return engine.GetService(CapabilityStandardAttachmentVehicleParadeModEntry.DemoStateKey)
            ?? throw new InvalidOperationException("Vehicle parade DemoState missing.");
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
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-attachment-vehicle-parade");
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(
            Path.Combine(artifactDir, "acceptance.txt"),
            $"binding={BindingName}{Environment.NewLine}map={MapId}{Environment.NewLine}frames={frames}{Environment.NewLine}");
    }
}
