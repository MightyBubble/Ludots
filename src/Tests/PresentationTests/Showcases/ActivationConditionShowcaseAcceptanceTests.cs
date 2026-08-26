using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class ActivationConditionShowcaseAcceptanceTests
{
    private const string MapId = "capability_standard_presenter_activation_condition_showcase";
    private const string BeaconDefinitionKey = "activation_condition_showcase.beacon";
    private const int BodySlot = 0;

    [Test]
    public void ActivationConditionShowcase_SourceHasVisualTransform_GatesGlowSlot()
    {
        string repoRoot = FindRepoRoot();
        var modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "CapabilityStandardPresenterActivationConditionShowcaseMod" });

        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        HeadlessPresentationTestHost.Install(engine);

        PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
        PresenterCommandBuffer commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("PresenterCommandBuffer missing.");

        int beaconDefinitionId = definitions.GetId(BeaconDefinitionKey);
        Assert.That(beaconDefinitionId, Is.GreaterThan(0));
        ref readonly PresenterDefinition definition = ref definitions.Get(beaconDefinitionId);
        Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
        Assert.That(definition.Behaviors[0].ActiveByDefault, Is.False,
            "loader must force ActiveByDefault=false when activationCondition is present");
        Assert.That(
            definition.Behaviors[0].ActivationCondition.Inline,
            Is.EqualTo(InlineConditionKind.SourceHasVisualTransform));

        engine.Start();
        engine.LoadMap(MapId);

        Entity ownerFalse = engine.World.Create(new PresentationStableId { Value = 72001 });
        Entity ownerTrue = engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(-4f, 0.6f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = 72002 });

        Assert.That(commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = beaconDefinitionId,
            ScopeTag = PresenterScopeTagRegistry.Register("activation_condition_showcase.acceptance.false"),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.WorldPosition,
            Source = ownerFalse,
            Position = new Vector3(4f, 0.6f, 0f),
        }), Is.True);
        Assert.That(commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = beaconDefinitionId,
            ScopeTag = PresenterScopeTagRegistry.Register("activation_condition_showcase.acceptance.true"),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = ownerTrue,
        }), Is.True);

        TickFrames(engine, 4);

        Entity falsePresenter = FindPresenterByOwner(engine, beaconDefinitionId, ownerFalse);
        Entity truePresenter = FindPresenterByOwner(engine, beaconDefinitionId, ownerTrue);
        Assert.That(falsePresenter, Is.Not.EqualTo(Entity.Null));
        Assert.That(truePresenter, Is.Not.EqualTo(Entity.Null));

        uint falseMask = engine.World.Get<PresenterState>(falsePresenter).BehaviorActiveMask;
        uint trueMask = engine.World.Get<PresenterState>(truePresenter).BehaviorActiveMask;
        Assert.That(falseMask & (1u << BodySlot), Is.EqualTo(0u), "no VisualTransform → glow stays inactive");
        Assert.That(trueMask & (1u << BodySlot), Is.Not.EqualTo(0u), "VisualTransform present → glow activates");
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
        }
    }

    private static Entity FindPresenterByOwner(GameEngine engine, int definitionId, Entity owner)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<PresenterState>();
        engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
        {
            if (state.DefId == definitionId && state.OwnerEntity == owner)
            {
                found = entity;
            }
        });

        return found;
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
