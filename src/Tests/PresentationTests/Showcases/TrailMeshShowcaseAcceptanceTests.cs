using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class TrailMeshShowcaseAcceptanceTests
{
    private const string MapId = "capability_standard_presenter_trailmesh_showcase";
    private const string BladeDefinitionKey = "trailmesh_showcase.blade";

    [Test]
    public void TrailMeshShowcase_MovingOwner_SamplesTrailWithoutDirectBufferWrites()
    {
        string repoRoot = FindRepoRoot();
        var modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "CapabilityStandardPresenterTrailMeshShowcaseMod" });

        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        HeadlessPresentationTestHost.Install(engine);

        PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
        PresenterCommandBuffer commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("PresenterCommandBuffer missing.");
        TrailMeshBuffer trails = engine.GetService(CoreServiceKeys.TrailMeshBuffer)
            ?? throw new InvalidOperationException("TrailMeshBuffer missing.");

        int bladeDefinitionId = definitions.GetId(BladeDefinitionKey);
        Assert.That(bladeDefinitionId, Is.GreaterThan(0));
        PresenterDefinition definition = definitions.Get(bladeDefinitionId);
        Assert.That(definition.Behaviors.Length, Is.EqualTo(2));
        Assert.That(definition.Behaviors[1].Kind, Is.EqualTo(BehaviorKind.TrailMesh));
        Assert.That(definition.Behaviors[1].TrailMesh.MaxSamples, Is.EqualTo(20));

        engine.Start();
        engine.LoadMap(MapId);

        Entity owner = engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(4f, 1.2f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = 71801 });
        Assert.That(commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = bladeDefinitionId,
            ScopeTag = PresenterScopeTagRegistry.Register("trailmesh_showcase.acceptance.blade"),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = owner,
        }), Is.True);

        TickFrames(engine, 4);
        Assert.That(trails.Count, Is.GreaterThan(0), "TrailMesh behavior must sample into TrailMeshBuffer after activation");
        int samplesBeforeMove = trails.GetSamples(0).Length;

        engine.World.Get<VisualTransform>(owner).Position = new Vector3(0f, 1.2f, 4f);
        engine.World.Get<VisualTransform>(owner).Rotation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        TickFrames(engine, 4);

        Assert.That(trails.Count, Is.GreaterThan(0));
        Assert.That(
            trails.GetSamples(0).Length,
            Is.GreaterThan(samplesBeforeMove),
            "moving/rotating the owner must append TrailMesh samples; demo must not write TrailMeshBuffer from C#");
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
        }
    }

    private static string FindRepoRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
