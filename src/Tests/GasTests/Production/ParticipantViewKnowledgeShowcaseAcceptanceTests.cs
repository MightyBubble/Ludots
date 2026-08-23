using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;
using ParticipantViewCapabilityMod.Runtime;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class ParticipantViewKnowledgeShowcaseAcceptanceTests
{
    private const string MapId = "capability_standard_participant_views";
    private const string LargeWorldCameraId = "MassNavigation.Camera.LargeWorldHeightmap";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "ParticipantViewCapabilityMod",
        "MassNavigationMod",
        "CapabilityStandardParticipantViewsMod",
    };

    [Test]
    public void Issue199_ParticipantShowcaseProjectsPlayerTeamAllyAndNeutralNpcVisibilityFromMapMetadata()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Participant showcase map did not load.");
        Assert.That(session.MapId.Value, Is.EqualTo(MapId));
        Assert.That(session.MapConfig.DefaultCamera, Is.Not.Null);
        Assert.That(session.MapConfig.DefaultCamera.VirtualCameraId, Is.EqualTo(LargeWorldCameraId));

        KnowledgeProjectionResolver resolver = engine.GetService(CoreServiceKeys.KnowledgeProjectionResolver)
            ?? throw new InvalidOperationException("CapabilityStandardParticipantViewsMod did not install KnowledgeProjectionResolver.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
        int participantTypeId = relationshipTypes.GetId(ParticipantViewCapabilityIds.RelationshipType);
        int healthAttributeId = AttributeRegistry.GetId("Health");
        Assert.That(participantTypeId, Is.GreaterThanOrEqualTo(0));
        Assert.That(healthAttributeId, Is.GreaterThanOrEqualTo(0));

        Entity playerOne = session.PlayerEntityLookup.Get(1);
        Entity teamOne = session.TeamEntityLookup.Get(1);
        Entity playerTwo = session.PlayerEntityLookup.Get(2);
        Entity npcEnvoy = RequireEntity(session, "npc-amber-envoy");
        Entity ownUnit = RequireEntity(session, "unit-azure-alpha-1");
        Entity allyDisclosedUnit = RequireEntity(session, "unit-azure-beta-1");
        Entity neutralRumorUnit = RequireEntity(session, "unit-amber-alpha-1");
        Entity hostileUnknownUnit = RequireEntity(session, "unit-crimson-alpha-1");

        ParticipantKnowledgeSnapshot playerOwn = Resolve(engine, resolver, playerOne, ownUnit);
        AssertLiveSelfKnowledge(playerOwn, playerOne, healthAttributeId, participantTypeId);

        ParticipantKnowledgeSnapshot playerAlly = Resolve(engine, resolver, playerOne, allyDisclosedUnit);
        Assert.That(playerAlly.IsDisclosed, Is.True, "Player 1 should learn Player 2's authored collection only through relation disclosure.");
        Assert.That(playerAlly.Source, Is.EqualTo(playerTwo));
        Assert.That(playerAlly.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
        Assert.That(playerAlly.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
        Assert.That(playerAlly.AttributeMask.ContainsId(healthAttributeId), Is.True);
        Assert.That(playerAlly.RelationshipTypeMask.ContainsId(participantTypeId), Is.True);

        ParticipantKnowledgeSnapshot playerNeutralRumor = Resolve(engine, resolver, playerOne, neutralRumorUnit);
        Assert.That(playerNeutralRumor.IsDisclosed, Is.True, "The neutral NPC should expose only its finite rumor collection.");
        Assert.That(playerNeutralRumor.Source, Is.EqualTo(npcEnvoy));
        Assert.That(playerNeutralRumor.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
        Assert.That(playerNeutralRumor.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
        Assert.That(playerNeutralRumor.AttributeMask.ContainsId(healthAttributeId), Is.True);
        Assert.That(playerNeutralRumor.RelationshipTypeMask.IsEmpty, Is.True);

        ParticipantKnowledgeSnapshot playerHostile = Resolve(engine, resolver, playerOne, hostileUnknownUnit);
        Assert.That(playerHostile.IsKnown, Is.False, "Player 1 should not know hostile units just because they exist in the map.");

        ParticipantKnowledgeSnapshot teamOwn = Resolve(engine, resolver, teamOne, ownUnit);
        ParticipantKnowledgeSnapshot teamAlly = Resolve(engine, resolver, teamOne, allyDisclosedUnit);
        ParticipantKnowledgeSnapshot teamNeutral = Resolve(engine, resolver, teamOne, neutralRumorUnit);
        AssertLiveSelfKnowledge(teamOwn, teamOne, healthAttributeId, participantTypeId);
        AssertLiveSelfKnowledge(teamAlly, teamOne, healthAttributeId, participantTypeId);
        Assert.That(teamNeutral.IsKnown, Is.False, "Team view should differ from Player 1 by not inheriting Player 1's NPC disclosure.");
    }

    [Test]
    public void ParticipantShowcaseLoadsRenderableFourTeamWorld()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);

        for (int frame = 0; frame < 8; frame++)
        {
            engine.Tick(1f / 60f);
        }

        Assert.That(
            engine.GetService(CoreServiceKeys.VisualHeightmap),
            Is.AssignableTo<IVisualHeightmapRenderSource>(),
            "The four-team participant view showcase must bind the MassNavigation visual heightmap through the formal map service.");
        Assert.That(CountVisualTransforms(engine), Is.GreaterThan(0),
            "The showcase world must contain visual transforms for its authored team members.");

        PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
        SkinnedVisualBatchBuffer skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
            ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

        int worldVisualCount = primitives.GetSpan().Length + skinned.Count;
        Assert.That(worldVisualCount, Is.GreaterThan(0),
            "The showcase must emit world visuals after startup; a UI-only participant panel is not a valid capability standard showcase.");
    }

    private static ParticipantKnowledgeSnapshot Resolve(
        GameEngine engine,
        KnowledgeProjectionResolver resolver,
        Entity viewer,
        Entity target)
    {
        return ParticipantViewProjection.ResolveKnowledgeSnapshot(
            engine.World,
            resolver,
            viewer,
            target,
            KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext));
    }

    private static void AssertLiveSelfKnowledge(
        ParticipantKnowledgeSnapshot snapshot,
        Entity expectedSource,
        int healthAttributeId,
        int participantTypeId)
    {
        Assert.That(snapshot.IsKnown, Is.True);
        Assert.That(snapshot.IsLiveVisible, Is.True);
        Assert.That(snapshot.IsDisclosed, Is.False);
        Assert.That(snapshot.Source, Is.EqualTo(expectedSource));
        Assert.That(snapshot.AttributeMask.ContainsId(healthAttributeId), Is.True);
        Assert.That(snapshot.RelationshipTypeMask.ContainsId(participantTypeId), Is.True);
    }

    private static Entity RequireEntity(MapSession session, string instanceId)
    {
        if (!session.EntityIndex.TryGet(instanceId, out Entity entity))
        {
            throw new InvalidOperationException($"Showcase map does not contain instance '{instanceId}'.");
        }

        return entity;
    }

    private static int CountVisualTransforms(GameEngine engine)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<VisualTransform>();
        engine.World.Query(in query, (Entity _) => count++);
        return count;
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new NullInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => false;

        public Vector2 GetMousePosition() => new(-1f, -1f);

        public float GetMouseWheel() => 0f;

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }

    private sealed class StubViewController : IViewController
    {
        public StubViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }

        public float Fov => 60f;

        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }
}
