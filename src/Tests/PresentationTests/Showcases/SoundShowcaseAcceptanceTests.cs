using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class SoundShowcaseAcceptanceTests
{
    private const string MapId = "capability_standard_sound_showcase";
    private const string EmitterDefinitionKey = "sound_showcase.emitter";
    private const string BeaconDefinitionKey = "sound_showcase.beacon";
    private const string EmitterToneAssetKey = "sound_showcase.tone_440";
    private const string BeaconToneAssetKey = "sound_showcase.tone_880";

    [Test]
    public void SoundShowcase_EventDrivenSoundBehaviors_ProduceAndStopBufferRequests()
    {
        string repoRoot = FindRepoRoot();
        List<string> modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "CapabilityStandardSoundShowcaseMod" });

        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        HeadlessPresentationTestHost.Install(engine);

        MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
        PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
        PresenterCommandBuffer commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("PresenterCommandBuffer missing.");
        PresentationEventStream events = engine.GetService(CoreServiceKeys.PresentationEventStream)
            ?? throw new InvalidOperationException("PresentationEventStream missing.");
        SoundRequestBuffer soundRequests = engine.GetService(CoreServiceKeys.SoundRequestBuffer)
            ?? throw new InvalidOperationException("SoundRequestBuffer missing.");

        int emitterToneAssetId = meshes.GetId(EmitterToneAssetKey);
        int beaconToneAssetId = meshes.GetId(BeaconToneAssetKey);
        Assert.That(emitterToneAssetId, Is.GreaterThan(0), $"Sound asset '{EmitterToneAssetKey}' should be registered.");
        Assert.That(beaconToneAssetId, Is.GreaterThan(0), $"Sound asset '{BeaconToneAssetKey}' should be registered.");
        Assert.That(meshes.TryGetDescriptor(emitterToneAssetId, out _), Is.True);

        int emitterDefinitionId = definitions.GetId(EmitterDefinitionKey);
        int beaconDefinitionId = definitions.GetId(BeaconDefinitionKey);
        Assert.That(emitterDefinitionId, Is.GreaterThan(0));
        Assert.That(beaconDefinitionId, Is.GreaterThan(0));

        engine.Start();
        engine.LoadMap(MapId);

        Entity emitterOwner = CreateOwner(engine, stableId: 71701, new Vector3(8f, 0.5f, 0f));
        Entity beaconOwner = CreateOwner(engine, stableId: 71702, new Vector3(-10f, 0.5f, 6f));
        EnqueuePresenterCreate(commands, emitterDefinitionId, "sound_showcase.acceptance.emitter", emitterOwner);
        EnqueuePresenterCreate(commands, beaconDefinitionId, "sound_showcase.acceptance.beacon", beaconOwner);
        TickFrames(engine, 4);

        Entity emitterPresenter = FindPresenterByDefinition(engine, emitterDefinitionId);
        Entity beaconPresenter = FindPresenterByDefinition(engine, beaconDefinitionId);
        Assert.That(emitterPresenter, Is.Not.EqualTo(Entity.Null));
        Assert.That(beaconPresenter, Is.Not.EqualTo(Entity.Null));

        Assert.That(soundRequests.Count, Is.EqualTo(0), "sound behaviors default to inactive; no requests before the on event");

        PublishGameplayEvent(events, "sound_showcase.emitter.on", emitterOwner);
        PublishGameplayEvent(events, "sound_showcase.beacon.on", beaconOwner);
        TickFrames(engine, 2);

        SoundRequest emitterPlay = FindSingle(soundRequests, SoundRequestKind.PlayOrUpdate, emitterToneAssetId);
        Assert.That(emitterPlay.Loop, Is.True, "emitter tone must request a looped sound");
        Assert.That(emitterPlay.Volume, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(emitterPlay.Owner, Is.EqualTo(emitterOwner));

        SoundRequest beaconPlay = FindSingle(soundRequests, SoundRequestKind.PlayOrUpdate, beaconToneAssetId);
        Assert.That(beaconPlay.Loop, Is.False, "beacon tone must request a one-shot sound");
        int beaconStableId = beaconPlay.StableId;

        engine.World.Get<VisualTransform>(emitterOwner).Position = new Vector3(30f, 0.5f, 0f);
        TickFrames(engine, 2);
        SoundRequest emitterMoved = FindSingle(soundRequests, SoundRequestKind.PlayOrUpdate, emitterToneAssetId);
        Assert.That(
            emitterMoved.WorldPosition.X,
            Is.EqualTo(30f).Within(0.001f),
            "moving the owner must move the 3D world position of the sound request (attenuation source)");

        PublishGameplayEvent(events, "sound_showcase.emitter.off", emitterOwner);
        TickFrames(engine, 1);
        SoundRequest emitterStop = FindSingle(soundRequests, SoundRequestKind.Stop, emitterToneAssetId);
        Assert.That(emitterStop.StableId, Is.EqualTo(emitterPlay.StableId), "Stop must target the same stableId the PlayOrUpdate created");
        TickFrames(engine, 1);
        Assert.That(CountRequests(soundRequests, SoundRequestKind.PlayOrUpdate, emitterToneAssetId), Is.Zero,
            "deactivated sound behavior stops emitting PlayOrUpdate");

        Assert.That(commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.DestroyPresenter,
            CommandKindId = (byte)PresenterCommandKind.DestroyPresenter,
            RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
            PresenterEntity = beaconPresenter,
        }), Is.True);
        TickFrames(engine, 1);

        SoundRequest beaconStop = FindSingle(soundRequests, SoundRequestKind.Stop, beaconToneAssetId);
        Assert.That(beaconStop.StableId, Is.EqualTo(beaconStableId), "presenter destruction must emit the Stop that releases the sound");
    }

    [Test]
    public void SoundShowcase_HostAssetRows_BindGeneratedToneWaves()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardSoundShowcaseMod");
        JsonArray hostRows = ReadJsonArray(Path.Combine(modRoot, "assets", "Presentation", "host_assets.json"));

        Assert.That(hostRows.Count, Is.EqualTo(2));
        foreach (JsonNode? row in hostRows)
        {
            var obj = (JsonObject)row!;
            Assert.That(obj["assetKind"]!.GetValue<string>(), Is.EqualTo("Sound"));
            Assert.That(obj["backendId"]!.GetValue<string>(), Is.EqualTo("raylib"));
            string uri = obj["sourceUris"]![0]!.GetValue<string>();
            Assert.That(uri, Does.StartWith("CapabilityStandardSoundShowcaseMod:"));
            string relativePath = uri.Substring("CapabilityStandardSoundShowcaseMod:".Length);
            Assert.That(
                File.Exists(Path.Combine(modRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                Is.True,
                $"host sound source '{uri}' must exist on disk");
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

    private static Entity CreateOwner(GameEngine engine, int stableId, Vector3 position)
    {
        return engine.World.Create(
            new VisualTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = stableId });
    }

    private static void EnqueuePresenterCreate(PresenterCommandBuffer commands, int definitionId, string scopeTagName, Entity source)
    {
        if (!commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = definitionId,
            ScopeTag = PresenterScopeTagRegistry.Register(scopeTagName),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = source,
        }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the sound showcase acceptance presenters.");
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
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the sound showcase acceptance event.");
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

    private static SoundRequest FindSingle(SoundRequestBuffer buffer, SoundRequestKind kind, int soundAssetId)
    {
        SoundRequest match = default;
        int matches = 0;
        foreach (ref readonly SoundRequest request in buffer.GetSpan())
        {
            if (request.Kind == kind && request.SoundAssetId == soundAssetId)
            {
                match = request;
                matches++;
            }
        }

        Assert.That(matches, Is.EqualTo(1), $"expected exactly one {kind} request for soundAssetId={soundAssetId}, found {matches}");
        return match;
    }

    private static int CountRequests(SoundRequestBuffer buffer, SoundRequestKind kind, int soundAssetId)
    {
        int count = 0;
        foreach (ref readonly SoundRequest request in buffer.GetSpan())
        {
            if (request.Kind == kind && request.SoundAssetId == soundAssetId)
            {
                count++;
            }
        }

        return count;
    }

    private static JsonArray ReadJsonArray(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path)) as JsonArray
            ?? throw new InvalidOperationException($"'{path}' must contain a JSON array.");
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
