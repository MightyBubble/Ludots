using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class MaterialBehaviorShowcaseAcceptanceTests
{
    private const string MapId = "capability_standard_presenter_material_behavior_showcase";
    private const string PropDefinitionKey = "material_behavior_showcase.prop";
    private const string CoolMaterialKey = "material_behavior_showcase.mat.cool";
    private const string WarmMaterialKey = "material_behavior_showcase.mat.warm";
    private const string SwapParamKey = "material_behavior_showcase.swap";

    [Test]
    public void MaterialBehaviorShowcase_SetParam_SwapsMaterialViaSwapTable()
    {
        string repoRoot = FindRepoRoot();
        var modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "CapabilityStandardPresenterMaterialBehaviorShowcaseMod" });

        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        HeadlessPresentationTestHost.Install(engine);

        PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
        PresenterCommandBuffer commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("PresenterCommandBuffer missing.");
        PresentationEventStream events = engine.GetService(CoreServiceKeys.PresentationEventStream)
            ?? throw new InvalidOperationException("PresentationEventStream missing.");
        PresenterEntityRuntime runtime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
            ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
        PresentationMaterialRegistry materials = engine.GetService(CoreServiceKeys.PresentationMaterialRegistry)
            ?? throw new InvalidOperationException("PresentationMaterialRegistry missing.");

        int coolMaterialId = materials.GetId(CoolMaterialKey);
        int warmMaterialId = materials.GetId(WarmMaterialKey);
        Assert.That(coolMaterialId, Is.GreaterThan(0));
        Assert.That(warmMaterialId, Is.GreaterThan(0));
        Assert.That(coolMaterialId, Is.Not.EqualTo(warmMaterialId));

        int propDefinitionId = definitions.GetId(PropDefinitionKey);
        Assert.That(propDefinitionId, Is.GreaterThan(0));
        ref readonly PresenterDefinition definition = ref definitions.Get(propDefinitionId);
        Assert.That(definition.Behaviors[1].Kind, Is.EqualTo(BehaviorKind.Material));
        Assert.That(definition.Behaviors[1].Material.SwapTable.Length, Is.EqualTo(2));

        engine.Start();
        engine.LoadMap(MapId);

        Entity owner = engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(0f, 0.7f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = 71901 });
        Assert.That(commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = propDefinitionId,
            ScopeTag = PresenterScopeTagRegistry.Register("material_behavior_showcase.acceptance.prop"),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = owner,
        }), Is.True);

        TickFrames(engine, 4);
        Entity presenter = FindPresenterByDefinition(engine, propDefinitionId);
        Assert.That(presenter, Is.Not.EqualTo(Entity.Null));

        int swapParamId = PresenterParamKeyRegistry.Register(SwapParamKey);
        Assert.That(runtime.ResolveInt(presenter, swapParamId), Is.EqualTo(coolMaterialId));

        PublishGameplayEvent(events, "material_behavior_showcase.swap.warm", owner);
        TickFrames(engine, 2);
        Assert.That(runtime.ResolveInt(presenter, swapParamId), Is.EqualTo(warmMaterialId));

        PublishGameplayEvent(events, "material_behavior_showcase.swap.cool", owner);
        TickFrames(engine, 2);
        Assert.That(runtime.ResolveInt(presenter, swapParamId), Is.EqualTo(coolMaterialId));
    }

    [Test]
    public void MaterialBehaviorShowcase_HostAssetRows_BindSolidColorAlbedo()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPresenterMaterialBehaviorShowcaseMod");
        JsonArray hostRows = JsonNode.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Presentation", "host_assets.json"))) as JsonArray
            ?? throw new InvalidOperationException("host_assets.json must be a JSON array.");

        Assert.That(hostRows.Count, Is.EqualTo(2));
        foreach (JsonNode? row in hostRows)
        {
            var obj = (JsonObject)row!;
            Assert.That(obj["assetKind"]!.GetValue<string>(), Is.EqualTo("Material"));
            Assert.That(obj["backendId"]!.GetValue<string>(), Is.EqualTo("raylib"));
            string uri = obj["textures"]!["albedo"]!.GetValue<string>();
            Assert.That(uri, Does.StartWith("CapabilityStandardPresenterMaterialBehaviorShowcaseMod:"));
            string relativePath = uri.Substring("CapabilityStandardPresenterMaterialBehaviorShowcaseMod:".Length);
            Assert.That(
                File.Exists(Path.Combine(modRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                Is.True,
                $"host material albedo '{uri}' must exist on disk");
        }
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
        }
    }

    private static void PublishGameplayEvent(PresentationEventStream events, string key, Entity source)
    {
        if (!events.TryAdd(new PresentationEvent
        {
            Kind = PresentationEventKind.GameplayEvent,
            KeyId = TagRegistry.Register(key),
            Source = source,
            Target = source,
            Position = Vector3.Zero,
        }))
        {
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the Material behavior acceptance event.");
        }
    }

    private static Entity FindPresenterByDefinition(GameEngine engine, int definitionId)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<PresenterState>();
        engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
        {
            if (state.DefId == definitionId)
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
