using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class PresenterCollectionVisibilityTests
{
    [TestCase(1, false)]
    [TestCase(10000, false)]
    [TestCase(10000, true)]
    public void SwitchingPlayer_HidesAndRestoresRetainedMarkersWithoutChangingMembership(int members, bool hud)
    {
        using var fixture = new Fixture(members, possessionConditions: true, hud: hud);
        for (int i = 0; i < members; i++) fixture.Member(i, false, true);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members * 2));
        Entity selected = fixture.Instance(0, false);
        var archetype = fixture.World.GetArchetype(selected);
        int structure = fixture.Runtime.StructureVersion;
        fixture.SetPlayer(2);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members));
        for (int i = 0; i < members; i++) fixture.Member(i, true, true, player: 1);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members), "A hidden player's newly created preview must stay hidden.");
        fixture.SetPlayer(1);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members * 3));
        fixture.SetPlayer(2);
        fixture.Advance();
        for (int i = 0; i < members; i++) fixture.Member(i, true, false, player: 1);
        fixture.Advance();
        fixture.SetPlayer(1);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members * 2), "Cancelled hidden previews must not return.");
        Assert.That(fixture.Instance(0, false), Is.EqualTo(selected));
        Assert.That(fixture.World.GetArchetype(selected), Is.SameAs(archetype));
        Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure + members));

        fixture.SetPlayer(2);
        fixture.Advance();
        fixture.SetPlayer(1);
        fixture.Advance();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < 20; i++)
        {
            fixture.SetPlayer(i % 2 + 1);
            fixture.Advance();
        }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(fixture.World.GetArchetype(selected), Is.SameAs(archetype));
        TestContext.Out.WriteLine($"possession_switch members={members} hud={hud} retained={fixture.Runtime.ActiveCount} mean_ms={elapsed / 20:F6} allocated={allocated}");

        fixture.ClearPossession();
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members));
        fixture.SetPlayer(1);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(members * 2));

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < members; i++) fixture.Member(i, false, false);
            fixture.Advance();
            for (int i = 0; i < members; i++) fixture.Member(i, false, true);
            fixture.Advance();
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(fixture.VisibleCount, Is.EqualTo(members * 2));
        Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure + members));
        Assert.That(fixture.World.GetArchetype(selected), Is.SameAs(archetype));
    }

    [Test]
    public void SwitchingPlayer_SharedUnitEmitsOnlyTheCurrentPlayersScopedMarkerEachFrame()
    {
        using var fixture = new Fixture(1, possessionConditions: true);
        fixture.Member(0, false, true, scope: 11, player: 1);
        fixture.Member(0, false, true, scope: 22, player: 2);
        fixture.Advance();
        Entity first = fixture.Instance(0, false, scope: 11);
        Entity second = fixture.Instance(0, false, scope: 22);
        int structure = fixture.Runtime.StructureVersion;

        for (int round = 0; round < 6; round++)
        {
            int player = round % 2 + 1;
            fixture.SetPlayer(player);
            for (int frame = 0; frame < 3; frame++)
            {
                fixture.Advance();
                Assert.Multiple(() =>
                {
                    Assert.That(fixture.World.Get<PresenterState>(first).BehaviorActiveMask,
                        Is.EqualTo(player == 1 ? 1u : 0u), $"player={player}, frame={frame}, first marker");
                    Assert.That(fixture.World.Get<PresenterState>(second).BehaviorActiveMask,
                        Is.EqualTo(player == 2 ? 1u : 0u), $"player={player}, frame={frame}, second marker");
                    Assert.That(fixture.Snapshot.Count, Is.EqualTo(2));
                    Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure));
                });
            }
        }
    }

    [Test]
    public void ReusedPresenter_RelationChangeRefreshesActivationWithoutPossessionChange()
    {
        using var fixture = new Fixture(1, possessionConditions: true);
        fixture.Member(0, false, true, player: 1);
        fixture.Advance();
        Entity original = fixture.Instance(0, false);
        Assert.That(fixture.VisibleCount, Is.EqualTo(2));
        fixture.Member(0, false, true, player: 2);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(1));
        Assert.That(fixture.Instance(0, false), Is.EqualTo(original));
        fixture.SetPlayer(2);
        fixture.Advance();
        Assert.That(fixture.VisibleCount, Is.EqualTo(2));
    }

    [Test]
    public void SameOwner_DifferentScopes_RetainIndependentVisibility()
    {
        using var fixture = new Fixture(1);
        fixture.Member(0, true, true, scope: 11);
        fixture.Member(0, true, true, scope: 22);
        fixture.Advance();
        Entity first = fixture.Instance(0, true, scope: 11);
        Entity second = fixture.Instance(0, true, scope: 22);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(3));

        fixture.Member(0, true, false, scope: 11);
        fixture.Advance();
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(2));
        fixture.Member(0, true, false, scope: 22);
        fixture.Advance();
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(1));

        fixture.Member(0, true, true, scope: 11);
        fixture.Advance();
        Assert.That(fixture.Instance(0, true, scope: 11), Is.EqualTo(first));
        Assert.That(fixture.Instance(0, true, scope: 22), Is.EqualTo(second));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(2));
    }

    [Test]
    public void GlobalRules_HideAndRestoreSameIdentity_WithoutAffectingOtherScopes()
    {
        using var fixture = new Fixture(2);
        fixture.Member(0, preview: true, added: true);
        fixture.Member(1, preview: true, added: true);
        fixture.Advance();
        Entity first = fixture.Instance(0, preview: true);
        int stableId = fixture.World.Get<PresenterState>(first).StableId;
        int structure = fixture.Runtime.StructureVersion;
        int keys = fixture.VisualIds.Count;
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(4));

        fixture.Member(0, preview: true, added: false);
        fixture.Advance();
        Assert.That(fixture.World.IsAlive(first), Is.True);
        Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure));
        Assert.That(fixture.VisualIds.Count, Is.EqualTo(keys));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(3));

        fixture.Member(0, preview: true, added: true);
        fixture.Advance();
        Assert.That(fixture.Instance(0, preview: true), Is.EqualTo(first));
        Assert.That(fixture.World.Get<PresenterState>(first).StableId, Is.EqualTo(stableId));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(4));
        Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure));

        fixture.Member(0, preview: true, added: false);
        fixture.Advance();
        fixture.AddCommand(new PresenterCommand { CommandKind = PresenterCommandKind.DestroyPresenter, PresenterEntity = fixture.Roots[0] });
        fixture.Advance();
        Assert.That(fixture.World.IsAlive(first), Is.False);
        Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(2));
        Assert.That(fixture.VisualIds.Count, Is.EqualTo(2));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(2));
    }

    [TestCase(5000)]
    [TestCase(10000)]
    public void CollectionVisibility_At10kOwners_ReusesInstancesAndWritesEvidence(int members)
    {
        using var fixture = new Fixture(10000);
        var rows = new List<object>();
        var csv = new StringBuilder("members,phase,elapsed_ms,allocated_bytes,presenters,visual_keys,visible\n");
        Measure("preview_first", true, true, false);
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000 + members));
        Measure("commit_first", false, true, true);
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000 + members));
        int retainedInstances = fixture.Runtime.ActiveCount;
        int structure = fixture.Runtime.StructureVersion;
        int visualKeys = fixture.VisualIds.Count;
        Measure("clear_first", false, false, false);
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000));

        for (int repeat = 0; repeat < 4; repeat++)
        {
            Measure($"preview_repeat_{repeat}", true, true, false);
            Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000 + members));
            Measure($"commit_repeat_{repeat}", false, true, true);
            Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000 + members));
            Measure($"clear_repeat_{repeat}", false, false, false);
            Assert.That(fixture.Snapshot.Count, Is.EqualTo(10000));
            Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(retainedInstances));
            Assert.That(fixture.Runtime.StructureVersion, Is.EqualTo(structure));
            Assert.That(fixture.VisualIds.Count, Is.EqualTo(visualKeys));
        }

        string directory = Path.Combine(PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(), "artifacts", "acceptance", "presenter-collection-visibility");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"scale-{members}.csv"), csv.ToString());
        File.WriteAllLines(Path.Combine(directory, $"trace-{members}.jsonl"), rows.ConvertAll(row => JsonSerializer.Serialize(row)));
        File.WriteAllText(Path.Combine(directory, $"battle-report-{members}.md"),
            $"# Collection marker acceptance\n\nBuild: PresentationTests Release. Owners: 10000. Members: {members}. Seed: sequential owner identities. Clock: 1/60 s. UTC: {DateTime.UtcNow:O}.\n\n" +
            "Player previews the formation, commits the selection, and clears it. The same sequence repeats four times. Preview disappears on commit; selected markers disappear on clear. No duplicate instances accumulate.\n\n" +
            $"Outcome: passed. Retained presenters: {retainedInstances}. Final visible bodies: 10000. Dropped events: 0 (overflow throws). Exact phase timings and allocations: scale-{members}.csv.\n");
        TestContext.Out.WriteLine(csv.ToString());

        void Measure(string phase, bool preview, bool added, bool commit)
        {
            if (commit) for (int i = 0; i < members; i++) fixture.Member(i, true, false);
            for (int i = 0; i < members; i++) fixture.Member(i, preview, added);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            fixture.Advance();
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (phase.EndsWith("repeat_3", StringComparison.Ordinal))
                Assert.That(allocated, Is.Zero, $"Warmed {phase} allocated managed memory.");
            csv.AppendLine(FormattableString.Invariant($"{members},{phase},{elapsed:F6},{allocated},{fixture.Runtime.ActiveCount},{fixture.VisualIds.Count},{fixture.Snapshot.Count}"));
            rows.Add(new { phase, members, elapsedMs = elapsed, allocatedBytes = allocated, visible = fixture.Snapshot.Count });
        }
    }

    [Test]
    public void DestroyedOwner_ReleasesHiddenAndVisibleMarkers()
    {
        using var fixture = new Fixture(2);
        fixture.Member(0, true, true);
        fixture.Member(0, false, true);
        fixture.Member(1, true, true);
        fixture.Advance();
        Entity hidden = fixture.Instance(0, true);
        Entity visible = fixture.Instance(0, false);
        fixture.Member(0, true, false);
        fixture.Advance();
        fixture.DestroyOwner(0);
        fixture.Advance();
        Assert.That(fixture.World.IsAlive(hidden), Is.False);
        Assert.That(fixture.World.IsAlive(visible), Is.False);
        Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(2));
        Assert.That(fixture.VisualIds.Count, Is.EqualTo(2));
        Assert.That(fixture.Snapshot.Count, Is.EqualTo(2));
    }

    private sealed class Fixture : IDisposable
    {
        public readonly World World = World.Create();
        public readonly PresenterEntityRuntime Runtime;
        public readonly PresenterVisualStableIdTable VisualIds;
        public readonly PrimitiveDrawBuffer Snapshot;
        public readonly WorldHudBatchBuffer Hud = new();
        public int VisibleCount => Snapshot.Count + Hud.Count;
        public readonly Entity[] Roots;
        private readonly Entity[] _owners;
        private readonly Entity _viewer;
        private readonly Entity _secondViewer;
        private readonly ClientLocalSeatRegistry _seats = new();
        private readonly PresenterBehaviorSystem? _behavior;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly PresenterCommandBuffer _commands;
        private readonly PresentationEventStream _events;
        private readonly PresenterRuleSystem _rules;
        private readonly PresenterRuntimeSystem _runtime;
        private readonly PresenterEmitSystem _emit;
        private readonly PresentationRequestFlushSystem _flush;
        private readonly int _previewId;
        private readonly int _selectedId;

        public Fixture(int owners, bool possessionConditions = false, bool hud = false)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(), "src", "Tests", "PresentationTests", "Fixtures", "CollectionVisibility"));
            var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
            _definitions = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(pipeline, _definitions,
                resolveMeshId: _ => 1, resolveMaterialId: _ => 1, resolveBehaviorAssetId: (_, _) => 1,
                resolveEntityCollectionKeyId: key => key == "preview" ? 1 : key == "selected" ? 2 : throw new InvalidOperationException(key))
                .Load(ConfigCatalogLoader.Load(pipeline));
            _previewId = _definitions.GetId("selection.preview");
            _selectedId = _definitions.GetId("selection.selected");
            if (possessionConditions)
            {
                foreach (int id in new[] { _previewId, _selectedId })
                {
                    PresenterDefinition definition = _definitions.Get(id);
                    definition.Behaviors[0].ActivationCondition = new ConditionRef { Inline = InlineConditionKind.TargetIsSolePossessedRep };
                    definition.Behaviors[0].ActiveByDefault = false;
                    if (hud) definition.Behaviors[0].AssetBinding.AssetKind = AssetKind.WorldText;
                    _definitions.Register(_definitions.GetName(id), definition);
                }
            }
            int rootId = _definitions.Register("fixture.root", new PresenterDefinition
            {
                Behaviors = [new BehaviorSlot
                {
                    SlotIndex = 0, Kind = BehaviorKind.AssetBinding, ActiveByDefault = true,
                    AssetBinding = new AssetBindingConfig
                    {
                        AssetKind = AssetKind.Mesh, AssetId = 1, MaterialId = 1,
                        Mobility = VisualMobility.Static, RenderPath = VisualRenderPath.StaticMesh,
                        LocalScale = Vector3.One, AssetIdParamKey = -1, AssetSwapParamKey = -1,
                    }
                }]
            });
            Runtime = new PresenterEntityRuntime(World);
            Runtime.BindDefinitions(_definitions);
            var ids = new PresentationStableIdAllocator();
            VisualIds = new PresenterVisualStableIdTable(ids, 131072);
            var cache = new StableDrawCache(40000);
            _owners = new Entity[owners];
            Roots = new Entity[owners];
            _viewer = World.Create(new PresentationStableId { Value = ids.Allocate() });
            _secondViewer = World.Create(new PresentationStableId { Value = ids.Allocate() });
            _seats.Add(new ClientLocalSeat("seat.0"));
            SetPlayer(1);
            for (int i = 0; i < owners; i++)
            {
                _owners[i] = World.Create(new PresentationStableId { Value = ids.Allocate() },
                    new CullState { IsVisible = true, LOD = LODLevel.High }, VisualTransform.Default);
                Roots[i] = Runtime.Create(rootId, _owners[i], i + 1, PresentationAnchorKind.Entity,
                    Vector3.Zero, ids.Allocate(), Entity.Null, _definitions.Get(rootId));
            }
            _commands = new PresenterCommandBuffer(40000);
            _events = new PresentationEventStream(40000);
            var requests = new PresentationRequestBuffer(40000);
            var globals = new Dictionary<string, object>();
            globals[CoreServiceKeys.ClientLocalSeatRegistry.Name] = _seats;
            if (possessionConditions)
                _behavior = new PresenterBehaviorSystem(World, Runtime, _definitions, _events,
                    new PresentationOwnerChangeBuffer(40000), new SoundRequestBuffer(), globals: globals);
            _rules = new PresenterRuleSystem(World, _events, _commands, _definitions, Runtime, new GraphProgramRegistry(), null!, globals);
            _runtime = new PresenterRuntimeSystem(World, _commands, _events, new TransientMarkerBuffer(16), requests,
                Runtime, ids, _definitions, stableDrawCache: cache, visualStableIds: VisualIds);
            _emit = new PresenterEmitSystem(World, Runtime, _definitions, requests, globals,
                stableDrawCache: cache, visualStableIds: VisualIds);
            Snapshot = new PrimitiveDrawBuffer(40000);
            _flush = new PresentationRequestFlushSystem(World, requests, new MeshAssetRegistry(), cache,
                new PrimitiveDrawBuffer(40000), new GroundOverlayBuffer(), Hud,
                new SplineRibbonBuffer(), Snapshot, new PresentationVisualProxyBuffer(40000), new SkinnedVisualBatchBuffer());
            Advance();
        }

        public void Member(int owner, bool preview, bool added, int? scope = null, int player = 1)
        {
            if (!_events.TryAdd(new PresentationEvent
            {
                Kind = added ? PresentationEventKind.EntityCollectionMemberAdded : PresentationEventKind.EntityCollectionMemberRemoved,
                KeyId = preview ? 1 : 2, Source = _owners[owner], Target = player == 1 ? _viewer : _secondViewer,
                Viewer = player == 1 ? _viewer : _secondViewer, PayloadA = scope ?? owner + 1,
            })) throw new InvalidOperationException("Event capacity exhausted.");
        }

        public Entity Instance(int owner, bool preview, int? scope = null)
        {
            if (!Runtime.TryGetActiveScopedInstance(preview ? _previewId : _selectedId, _owners[owner], scope ?? owner + 1,
                PresentationAnchorKind.Entity, Vector3.Zero, out Entity entity)) throw new InvalidOperationException("Missing instance.");
            return entity;
        }

        public void AddCommand(PresenterCommand command)
        {
            if (!_commands.TryAdd(command)) throw new InvalidOperationException("Command capacity exhausted.");
        }

        public void DestroyOwner(int owner)
        {
            World.Destroy(_owners[owner]);
            if (!_events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntityDestroyed, Source = _owners[owner], PayloadA = owner + 1,
            })) throw new InvalidOperationException("Event capacity exhausted.");
        }

        public void Advance()
        {
            _rules.Update(1f / 60f);
            _runtime.Update(1f / 60f);
            _behavior?.Update(1f / 60f);
            _emit.Update(1f / 60f);
            _flush.Update(1f / 60f);
        }

        public void Dispose()
        {
            _behavior?.Dispose();
            _flush.Dispose();
            _emit.Dispose();
            _runtime.Dispose();
            _rules.Dispose();
            World.Dispose();
        }

        public void SetPlayer(int player) => _seats.SetPossession("seat.0", player, player == 1 ? _viewer : _secondViewer);
        public void ClearPossession() => _seats.ClearPossession("seat.0");
    }
}
