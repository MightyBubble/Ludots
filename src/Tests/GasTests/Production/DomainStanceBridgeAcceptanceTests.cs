using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// RFC-0065 DEC-3 bridge acceptance: loading a production map with <c>ParticipantRelationships.Teams</c>
/// attitudes must leave <see cref="DomainStanceQuery"/> and the legacy <see cref="TeamManager"/> matrix
/// in agreement (double-write in <see cref="ParticipantBindingResolver"/>). No stance name literals here:
/// expectations are derived from the TeamManager matrix itself, so name alignment stays a data concern.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class DomainStanceBridgeAcceptanceTests
{
    private const string MapId = "capability_standard_participant_views";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "ParticipantViewCapabilityMod",
        "MassNavigationMod",
        "CapabilityStandardParticipantViewsMod",
    };

    private static readonly int[] PlayerIds = { 1, 2, 3, 4, 5, 6 };
    private static readonly int[] TeamIds = { 1, 2, 3, 4 };

    [Test]
    public void DomainStanceBridge_MapAttitudes_AgreeWithTeamManagerForEveryParticipantPair()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Participant showcase map did not load.");
        Assert.That(session.MapId.Value, Is.EqualTo(MapId));

        DomainStanceQuery stanceQuery = engine.GetService(CoreServiceKeys.DomainStanceQuery)
            ?? throw new InvalidOperationException("DomainStanceQuery missing.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");

        // Player-rep level: stance resolves via MemberOf(playerRep→teamRep) + bridged team stance edges.
        foreach (int playerA in PlayerIds)
        {
            foreach (int playerB in PlayerIds)
            {
                Entity repA = session.PlayerEntityLookup.Get(playerA);
                Entity repB = session.PlayerEntityLookup.Get(playerB);
                int teamA = engine.World.Get<Team>(repA).Id;
                int teamB = engine.World.Get<Team>(repB).Id;
                int expectedStanceId = relationshipTypes.GetId(TeamManager.GetRelationship(teamA, teamB).ToString());

                Assert.That(
                    stanceQuery.GetStance(repA, repB),
                    Is.EqualTo(expectedStanceId),
                    $"DomainStanceQuery(P{playerA}→P{playerB}) diverged from TeamManager({teamA}→{teamB}).");
            }
        }

        // Team-rep level: distinct teams read the bridged direct edge, same team reads sameDomainStance.
        foreach (int teamA in TeamIds)
        {
            foreach (int teamB in TeamIds)
            {
                Entity repA = session.TeamEntityLookup.Get(teamA);
                Entity repB = session.TeamEntityLookup.Get(teamB);
                int expectedStanceId = relationshipTypes.GetId(TeamManager.GetRelationship(teamA, teamB).ToString());

                Assert.That(
                    stanceQuery.GetStance(repA, repB),
                    Is.EqualTo(expectedStanceId),
                    $"DomainStanceQuery(T{teamA}→T{teamB}) diverged from TeamManager({teamA}→{teamB}).");
            }
        }

        // The map declares hostile cross-team pairs: the stance must actually differ from the same-team stance.
        Entity azureAlpha = session.PlayerEntityLookup.Get(1);
        Entity azureBeta = session.PlayerEntityLookup.Get(2);
        Entity crimsonAlpha = session.PlayerEntityLookup.Get(3);
        Assert.That(
            stanceQuery.GetStance(azureAlpha, crimsonAlpha),
            Is.Not.EqualTo(stanceQuery.GetStance(azureAlpha, azureBeta)),
            "Hostile cross-team stance must differ from the same-team stance.");
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
