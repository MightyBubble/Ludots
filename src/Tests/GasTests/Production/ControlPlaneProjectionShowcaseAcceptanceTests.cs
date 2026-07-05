using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;
using ControlPlaneProjectionShowcaseMod;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ControlPlaneProjectionShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.ControlPlaneProjection.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "ControlPlaneProjectionShowcaseMod",
    };

    [Test]
    public void ControlPlaneProjectionShowcase_RoutesSelectionByDomainAndProjectsOwnedVsProxiedMarkers()
    {
        string repoRoot = FindRepoRoot();
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(ControlPlaneProjectionShowcaseIds.MapId);
        Tick(engine, 4);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Control plane projection map did not load.");
        Assert.That(session.MapId.Value, Is.EqualTo(ControlPlaneProjectionShowcaseIds.MapId));

        ControlPlaneProjectionScenarioState state = ResolveState(engine);
        Assert.That(state.Ready, Is.True, "Scenario bootstrap did not complete.");

        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        RelationshipFlagRegistry relationshipFlags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
            ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");
        int grantedFlagId = relationshipFlags.GetId(AssociationControlProfileRuntime.GrantedFlagName);
        EntityCollectionStore store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("SelectionRuntime missing.");
        TestInputBackend input = GetInputBackend(engine);

        Entity p1Unit = state.P1Units[0];
        Entity p2Unit = state.P2Units[0];

        // CTRL-2 slice: relationship edges were built by the mod trigger through EnsureLink.
        foreach (Entity unit in state.P1Units)
        {
            Assert.That(relationships.HasLink(state.P1Rep, unit, state.OwnsTypeId), Is.True, "Owns(P1Rep→p1 unit) missing.");
        }

        foreach (Entity unit in state.P2Units)
        {
            Assert.That(relationships.HasLink(state.P2Rep, unit, state.OwnsTypeId), Is.True, "Owns(P2Rep→p2 unit) missing.");
        }

        Assert.That(relationships.HasLink(state.P1Rep, state.TeamRep, state.MemberOfTypeId), Is.True, "MemberOf(P1Rep→TeamRep) missing.");
        Assert.That(relationships.HasLink(state.P2Rep, state.TeamRep, state.MemberOfTypeId), Is.True, "MemberOf(P2Rep→TeamRep) missing.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.AllyTypeId), Is.True, "Ally(P1Rep↔P2Rep) missing.");

        // Enable proxy control via the real ToggleProxy input binding (O key). The input action only
        // flips the trigger tag on P2Rep; the Controls edge must be granted by the
        // AssociationControlProfileRuntime on its SchemaUpdate evaluation pass (RFC-0065 CTRL-4b/M3).
        PressButton(engine, input, "<Keyboard>/o");
        Assert.That(state.ProxyActive, Is.True, "ToggleProxy input did not activate the proxy.");
        Assert.That(HasOfflineTag(engine, state), Is.True, "participant.offline tag missing on P2Rep after proxy on.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.ControlsTypeId), Is.True, "Controls(P1Rep→P2Rep) missing after proxy on.");
        Assert.That(relationships.HasFlag(state.P1Rep, state.P2Rep, state.ControlsTypeId, grantedFlagId), Is.True,
            "Controls(P1Rep→P2Rep) must carry the Granted flag — the edge has to come from the profile engine, not manual EnsureLink.");

        // Simulate the committed box selection: replace P1Rep's formal selection with a mixed set.
        Span<Entity> selected = stackalloc Entity[2];
        selected[0] = p1Unit;
        selected[1] = p2Unit;
        Assert.That(selection.ReplaceSelection(state.P1Rep, SelectionSetKeys.LivePrimary, selected), Is.True);
        Tick(engine, 4);

        // M3: the routed write split the batch per control domain — no cross-domain rows.
        Assert.That(CopyCollection(store, state.P1Rep, state.CommandSourceKeyId), Is.EquivalentTo(new[] { p1Unit }),
            "(P1Rep, CommandSource) must contain exactly the P1-owned selection.");
        Assert.That(CopyCollection(store, state.P2Rep, state.CommandSourceKeyId), Is.EquivalentTo(new[] { p2Unit }),
            "(P2Rep, CommandSource) must contain exactly the P2-owned selection.");

        // M4: viewer-relative projections partition the composite view by row domain.
        Assert.That(CopyCollection(store, state.P1Rep, state.OwnedProjectionKeyId), Is.EquivalentTo(new[] { p1Unit }),
            "Owned projection must hold P1Rep-domain members.");
        Assert.That(CopyCollection(store, state.P1Rep, state.ProxiedProjectionKeyId), Is.EquivalentTo(new[] { p2Unit }),
            "Proxied projection must hold members reached through the Controls grant.");

        // Disable proxy: the tag disappears, the profile revokes its own grant, and the composite view
        // shrinks while the teammate domain keeps its rows.
        PressButton(engine, input, "<Keyboard>/o");
        Assert.That(state.ProxyActive, Is.False, "ToggleProxy input did not deactivate the proxy.");
        Assert.That(HasOfflineTag(engine, state), Is.False, "participant.offline tag must be removed after proxy off.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.ControlsTypeId), Is.False, "Controls(P1Rep→P2Rep) must be revoked by the profile after proxy off.");
        Tick(engine, 4);

        Assert.That(CopyCollection(store, state.P2Rep, state.CommandSourceKeyId), Is.EquivalentTo(new[] { p2Unit }),
            "(P2Rep, CommandSource) must be preserved after the Controls grant disappears (collections never migrate).");
        Assert.That(CopyCollection(store, state.P1Rep, state.OwnedProjectionKeyId), Is.EquivalentTo(new[] { p1Unit }),
            "Owned projection must survive the proxy toggle.");
        Assert.That(CopyCollection(store, state.P1Rep, state.ProxiedProjectionKeyId), Is.Empty,
            "Proxied projection must clear once the control plane view no longer reaches P2Rep.");
    }

    private static ControlPlaneProjectionScenarioState ResolveState(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(ControlPlaneProjectionShowcaseIds.StateKey, out object? stateObj) &&
               stateObj is ControlPlaneProjectionScenarioState state
            ? state
            : throw new InvalidOperationException("Control plane projection scenario state missing.");
    }

    private static bool HasOfflineTag(GameEngine engine, ControlPlaneProjectionScenarioState state)
    {
        GameplayTagContainer tags = engine.World.Get<GameplayTagContainer>(state.P2Rep);
        return tags.HasTag(state.OfflineTagId);
    }

    private static Entity[] CopyCollection(EntityCollectionStore store, Entity owner, int keyId)
    {
        if (!store.TryGet(owner, keyId, out EntityCollectionHandle handle) ||
            !store.TryGetView(handle, out EntityCollectionView view) ||
            view.Count == 0)
        {
            return Array.Empty<Entity>();
        }

        var buffer = new Entity[view.Count];
        int copied = store.CopyEntities(handle, 0, buffer);
        return buffer.AsSpan(0, copied).ToArray();
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
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
        var backend = new TestInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.GlobalContext[TestInputBackendKey] = backend;
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Control plane projection input backend missing.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    // The engine simulates at Time.FixedDeltaTime (0.05s) while the test ticks at 1/60s, so one
    // simulation step needs up to 3 wall ticks. Hold/release across 4 ticks each so the press is
    // sampled by InputCollection AND the AssociationControlProfileSystem (SchemaUpdate phase) gets a
    // later simulation step to react to the tag flip.
    private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
    {
        backend.SetButton(path, true);
        Tick(engine, 4);
        backend.SetButton(path, false);
        Tick(engine, 4);
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

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => new(-1f, -1f);

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool down)
        {
            if (down)
            {
                _buttons.Add(path);
            }
            else
            {
                _buttons.Remove(path);
            }
        }

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
