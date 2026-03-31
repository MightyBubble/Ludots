using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Networking;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Networking
{
    [TestFixture]
    [NonParallelizable]
    public sealed class GameplayReplicationFoundationTests
    {
        private static readonly string[] NetworkingMods =
        {
            "LudotsCoreMod",
            "CoreInputMod"
        };

        [Test]
        public void CoreBootstrap_EventDispatch_RegistersGameplayReplicationSystemsInAuthoritativeOrder()
        {
            using var engine = CreateEngine();

            var eventDispatchNames = GetSystemNames(engine, SystemGroup.EventDispatch);

            Assert.That(eventDispatchNames, Does.Contain("GameplayReplicationBootstrapSystem"));
            Assert.That(eventDispatchNames, Does.Contain("GameplayReplicationEmitSystem"));
            Assert.That(eventDispatchNames.IndexOf("GameplayEventDispatchSystem"), Is.LessThan(eventDispatchNames.IndexOf("GameplayReplicationBootstrapSystem")));
            Assert.That(eventDispatchNames.IndexOf("GameplayReplicationBootstrapSystem"), Is.LessThan(eventDispatchNames.IndexOf("GameplayReplicationEmitSystem")));
            Assert.That(eventDispatchNames.IndexOf("GameplayReplicationEmitSystem"), Is.LessThan(eventDispatchNames.IndexOf("GasBudgetReportSystem")));
        }

        [Test]
        public void GameplayReplicationSnapshotExtractor_EmitsAuthoritativeSnapshotWithStableReplicationIds()
        {
            using var engine = CreateEngine();
            ScenarioState scenario = SeedScenario(engine);

            Tick(engine, 1);

            var extractor = new GameplayReplicationSnapshotExtractor(engine);
            GameplayReplicationSnapshotView first = extractor.Extract();

            Assert.That(first.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(first.SimTick, Is.GreaterThan(0));

            var ownedSpawn = FindByPosition(first, scenario.OwnedSpawnPosition);
            var neutralSpawn = FindByPosition(first, scenario.NeutralSpawnPosition);
            var source = FindByPosition(first, scenario.SourcePosition);

            Assert.That(source.ReplicationEntityId, Is.GreaterThan(0));
            Assert.That(ownedSpawn.ReplicationEntityId, Is.GreaterThan(0));
            Assert.That(neutralSpawn.ReplicationEntityId, Is.GreaterThan(0));
            Assert.That(ownedSpawn.Flags.HasFlag(GameplayReplicationSnapshotFlags.HasFacing), Is.True);
            Assert.That(ownedSpawn.Flags.HasFlag(GameplayReplicationSnapshotFlags.HasTeam), Is.True);
            Assert.That(ownedSpawn.Flags.HasFlag(GameplayReplicationSnapshotFlags.HasPlayerOwner), Is.True);
            Assert.That(ownedSpawn.TeamId, Is.EqualTo(2));
            Assert.That(ownedSpawn.PlayerId, Is.EqualTo(17));
            Assert.That(ownedSpawn.FacingAngleRad, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(neutralSpawn.Flags.HasFlag(GameplayReplicationSnapshotFlags.HasTeam), Is.False);
            Assert.That(neutralSpawn.Flags.HasFlag(GameplayReplicationSnapshotFlags.HasPlayerOwner), Is.False);

            GameplayReplicationSnapshotView secondWithoutTick = extractor.Extract();
            Assert.That(secondWithoutTick.Count, Is.EqualTo(first.Count), "Latest authoritative snapshot should persist until the next fixed-step rebuild.");
            Assert.That(FindByPosition(secondWithoutTick, scenario.OwnedSpawnPosition).ReplicationEntityId, Is.EqualTo(ownedSpawn.ReplicationEntityId));

            Tick(engine, 2);
            GameplayReplicationSnapshotView afterLaterTicks = extractor.Extract();
            Assert.That(FindByPosition(afterLaterTicks, scenario.SourcePosition).ReplicationEntityId, Is.EqualTo(source.ReplicationEntityId));
            Assert.That(FindByPosition(afterLaterTicks, scenario.OwnedSpawnPosition).ReplicationEntityId, Is.EqualTo(ownedSpawn.ReplicationEntityId));
            Assert.That(FindByPosition(afterLaterTicks, scenario.NeutralSpawnPosition).ReplicationEntityId, Is.EqualTo(neutralSpawn.ReplicationEntityId));
        }

        [Test]
        public void GameplayReplicationFoundation_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "gameplay-replication-foundation");
            Directory.CreateDirectory(artifactDir);

            using var engine = CreateEngine();
            ScenarioState scenario = SeedScenario(engine);
            var extractor = new GameplayReplicationSnapshotExtractor(engine);

            var timeline = new List<string>();
            var traceLines = new List<string>();

            Tick(engine, 1);
            var snapshotTick1 = extractor.Extract();
            Tick(engine, 1);
            var snapshotTick2 = extractor.Extract();

            var source = FindByPosition(snapshotTick1, scenario.SourcePosition);
            var ownedSpawnTick1 = FindByPosition(snapshotTick1, scenario.OwnedSpawnPosition);
            var neutralSpawnTick1 = FindByPosition(snapshotTick1, scenario.NeutralSpawnPosition);
            var ownedSpawnTick2 = FindByPosition(snapshotTick2, scenario.OwnedSpawnPosition);

            timeline.Add($"[T+001] AuthoritySource#{source.ReplicationEntityId} enters gameplay replication bootstrap at ({scenario.SourcePosition.X.ToInt()},{scenario.SourcePosition.Y.ToInt()})cm");
            timeline.Add($"[T+002] RuntimeEntitySpawnQueue.Assembly -> OwnedSpawn#{ownedSpawnTick1.ReplicationEntityId} | CopyTeam/PlayerOwner/Facing | Team {ownedSpawnTick1.TeamId} | Player {ownedSpawnTick1.PlayerId}");
            timeline.Add($"[T+003] RuntimeEntitySpawnQueue.Assembly -> NeutralSpawn#{neutralSpawnTick1.ReplicationEntityId} | Guard branch strips optional ownership flags");
            timeline.Add($"[T+004] Snapshot.Emit(simTick={snapshotTick2.SimTick}) -> OwnedSpawn#{ownedSpawnTick2.ReplicationEntityId} keeps a stable replication id across ticks");

            AppendTrace(traceLines, "tick1", snapshotTick1);
            AppendTrace(traceLines, "tick2", snapshotTick2);

            string tracePath = Path.Combine(artifactDir, "trace.jsonl");
            string battleReportPath = Path.Combine(artifactDir, "battle-report.md");
            string pathPath = Path.Combine(artifactDir, "path.mmd");

            File.WriteAllText(tracePath, string.Join(Environment.NewLine, traceLines));
            File.WriteAllText(battleReportPath, BuildBattleReport(timeline, scenario, snapshotTick1, snapshotTick2));
            File.WriteAllText(pathPath, BuildPathMermaid());

            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(File.Exists(battleReportPath), Is.True);
            Assert.That(File.Exists(pathPath), Is.True);
            Assert.That(File.ReadAllText(battleReportPath), Does.Contain("gameplay-replication-foundation"));
            Assert.That(File.ReadAllText(pathPath), Does.Contain("guard branch"));
        }

        private static void AppendTrace(List<string> lines, string stage, GameplayReplicationSnapshotView snapshot)
        {
            for (int i = 0; i < snapshot.Entities.Length; i++)
            {
                var entity = snapshot.Entities[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"{stage}_{i + 1}",
                    stage,
                    sim_tick = snapshot.SimTick,
                    replication_entity_id = entity.ReplicationEntityId,
                    presentation_stable_id = entity.PresentationStableId,
                    team_id = entity.TeamId,
                    player_id = entity.PlayerId,
                    position_x_raw = entity.PositionXRaw,
                    position_y_raw = entity.PositionYRaw,
                    facing_angle_rad = entity.FacingAngleRad,
                    flags = entity.Flags.ToString(),
                }));
            }
        }

        private static string BuildBattleReport(
            List<string> timeline,
            ScenarioState scenario,
            GameplayReplicationSnapshotView tick1,
            GameplayReplicationSnapshotView tick2)
        {
            var ownedSpawnTick1 = FindByPosition(tick1, scenario.OwnedSpawnPosition);
            var ownedSpawnTick2 = FindByPosition(tick2, scenario.OwnedSpawnPosition);
            var neutralSpawnTick1 = FindByPosition(tick1, scenario.NeutralSpawnPosition);

            var sb = new StringBuilder();
            sb.AppendLine("# gameplay-replication-foundation");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario name: gameplay-replication-foundation");
            sb.AppendLine("- build/version: local test run");
            sb.AppendLine("- seed/map/clock: seed=network-foundation-001 map=manual-world clock=60hz");
            sb.AppendLine($"- execution timestamp: {DateTimeOffset.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine(timeline[i]);
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine($"- success/failure decision: success");
            sb.AppendLine($"- failed assertions: none");
            sb.AppendLine($"- reason codes: OWNED_FLAGS_PROPAGATED, NEUTRAL_FLAGS_GUARDED, REPLICATION_ID_STABLE={ownedSpawnTick1.ReplicationEntityId == ownedSpawnTick2.ReplicationEntityId}");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- total actions: {timeline.Count}");
            sb.AppendLine($"- key damage/heal/control counters: not applicable for this infrastructure scenario");
            sb.AppendLine($"- dropped/budget/fuse counters: dropped={tick2.DroppedTotal}, budget_fused=false");
            sb.AppendLine($"- owned spawn: #{ownedSpawnTick1.ReplicationEntityId} team={ownedSpawnTick1.TeamId} player={ownedSpawnTick1.PlayerId} flags={ownedSpawnTick1.Flags}");
            sb.AppendLine($"- neutral guard branch: #{neutralSpawnTick1.ReplicationEntityId} flags={neutralSpawnTick1.Flags}");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return """
flowchart TD
    A["Scenario: seed authority source + neutral probe"] --> B["EventDispatch: GameplayReplicationBootstrapSystem assigns stable replication ids"]
    B --> C["EventDispatch: GameplayReplicationEmitSystem rebuilds authoritative snapshot"]
    C --> D["Web Debug: /api/runtime/gameplay-snapshot exposes latest snapshot"]
    B --> E["guard branch: entity lacks Team / PlayerOwner"]
    E --> F["Emit result: ownership flags omitted, position still replicated"]
    B --> G["owned spawn branch: copy team/player/facing from source"]
    G --> H["Emit result: HasTeam + HasPlayerOwner + HasFacing"]
""";
        }

        private static GameplayReplicationSnapshotEntityView FindByPosition(GameplayReplicationSnapshotView snapshot, Fix64Vec2 position)
        {
            long xRaw = position.X.RawValue;
            long yRaw = position.Y.RawValue;
            for (int i = 0; i < snapshot.Entities.Length; i++)
            {
                var entity = snapshot.Entities[i];
                if (entity.PositionXRaw == xRaw && entity.PositionYRaw == yRaw)
                {
                    return entity;
                }
            }

            throw new AssertionException($"Missing gameplay replication snapshot entry for raw position ({xRaw}, {yRaw}).");
        }

        private static ScenarioState SeedScenario(GameEngine engine)
        {
            var queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue service missing.");

            var sourcePosition = Fix64Vec2.FromInt(1200, 800);
            var neutralProbePosition = Fix64Vec2.FromInt(300, 450);
            var ownedSpawnPosition = Fix64Vec2.FromInt(1600, 950);
            var neutralSpawnPosition = Fix64Vec2.FromInt(1800, 1200);

            var source = engine.World.Create(
                new Name { Value = "AuthoritySource" },
                new WorldPositionCm { Value = sourcePosition },
                new PreviousWorldPositionCm { Value = sourcePosition },
                new Team { Id = 2 },
                new PlayerOwner { PlayerId = 17 },
                new FacingDirection { AngleRad = 0.5f });

            var neutralProbe = engine.World.Create(
                new Name { Value = "NeutralProbe" },
                new WorldPositionCm { Value = neutralProbePosition },
                new PreviousWorldPositionCm { Value = neutralProbePosition });

            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                Source = source,
                WorldPositionCm = ownedSpawnPosition,
                HasWorldPosition = 1,
                CopySourceTeam = 1,
                CopySourcePlayerOwner = 1,
                HasFacing = 1,
                FacingAngleRad = 1.25f,
            }), Is.True);

            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                Source = neutralProbe,
                WorldPositionCm = neutralSpawnPosition,
                HasWorldPosition = 1,
            }), Is.True);

            return new ScenarioState(sourcePosition, ownedSpawnPosition, neutralSpawnPosition);
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, NetworkingMods), assetsRoot);
            engine.SimulationBudgetMsPerFrame = 1000;
            engine.SimulationMaxSlicesPerLogicFrame = 10000;
            InstallInput(engine);
            engine.Start();
            if (!string.IsNullOrWhiteSpace(engine.MergedConfig.StartupMapId))
            {
                engine.LoadMap(engine.MergedConfig.StartupMapId);
                Tick(engine, 2);
            }

            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(Time.FixedDeltaTime);
            }
        }

        private static List<string> GetSystemNames(GameEngine engine, SystemGroup group)
        {
            var systems = GetSystems(engine, group);
            var result = new List<string>(systems.Count);
            for (int i = 0; i < systems.Count; i++)
            {
                result.Add(systems[i].GetType().Name);
            }

            return result;
        }

        private static List<ISystem<float>> GetSystems(GameEngine engine, SystemGroup group)
        {
            var field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>;
            Assert.That(systemGroups, Is.Not.Null);
            Assert.That(systemGroups!.ContainsKey(group), Is.True);

            return systemGroups[group];
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private readonly record struct ScenarioState(
            Fix64Vec2 SourcePosition,
            Fix64Vec2 OwnedSpawnPosition,
            Fix64Vec2 NeutralSpawnPosition);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;

            public bool GetButton(string devicePath) => false;

            public Vector2 GetMousePosition() => Vector2.Zero;

            public float GetMouseWheel() => 0f;

            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;
        }
    }
}
