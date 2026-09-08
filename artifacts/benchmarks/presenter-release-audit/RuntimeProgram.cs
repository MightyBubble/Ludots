using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;

string outputDirectory = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outputDirectory);
using var csv = new StreamWriter(Path.Combine(outputDirectory, "runtime-samples.csv"));
csv.WriteLine("requested_capacity,actual_capacity,owners,members,repeat,phase,elapsed_ms,allocated_bytes,presenters,visual_keys,cache_entries");
Run(16384, 100, -1);
foreach (int capacity in new[] { 16384, 131072 })
    for (int repeat = 0; repeat < 3; repeat++)
        Run(capacity, 5000, repeat);

void Run(int capacity, int members, int repeat)
{
    const int ownerCount = 10000;
    using var world = World.Create();
    var instances = new PresenterEntityRuntime(world);
    var definitions = new PresenterDefinitionRegistry();
    int rootDefId = definitions.Register("audit.root", Definition());
    int previewDefId = definitions.Register("audit.preview", Definition());
    int selectedDefId = definitions.Register("audit.selected", Definition());
    var stableIds = new PresentationStableIdAllocator();
    var visualIds = new PresenterVisualStableIdTable(stableIds, capacity);
    var cache = new StableDrawCache(capacity);
    var commands = new PresenterCommandBuffer(ownerCount + 16);
    var events = new PresentationEventStream(ownerCount * 2 + 16);
    var requests = new PresentationRequestBuffer(ownerCount + 16);
    var markers = new TransientMarkerBuffer(16);
    var owners = new Entity[ownerCount];
    var roots = new Entity[ownerCount];
    for (int i = 0; i < ownerCount; i++)
    {
        owners[i] = world.Create(new PresentationStableId { Value = stableIds.Allocate() },
            new CullState { IsVisible = true, LOD = LODLevel.High }, VisualTransform.Default);
        roots[i] = instances.Create(rootDefId, owners[i], i + 1, PresentationAnchorKind.Entity,
            Vector3.Zero, stableIds.Allocate(), Entity.Null, definitions.Get(rootDefId));
    }
    using var runtime = new PresenterRuntimeSystem(world, commands, events, markers, requests,
        instances, stableIds, definitions, stableDrawCache: cache, visualStableIds: visualIds);
    using var emit = new PresenterEmitSystem(world, instances, definitions, requests,
        new Dictionary<string, object>(), animatorStates: null!, soundRequests: null!,
        stableDrawCache: cache, visualStableIds: visualIds);
    runtime.Update(0.016f);
    emit.Update(0.016f);
    Check(ownerCount);
    cache.ClearStaticMeshDeltas();
    commands.Clear();
    events.Clear();

    EnqueueCreate(previewDefId);
    Measure("preview_create", () => runtime.Update(0.016f));
    Measure("preview_first_emit", () => emit.Update(0.016f));
    Check(ownerCount + members);
    cache.ClearStaticMeshDeltas();
    commands.Clear();
    events.Clear();
    Measure("preview_unchanged_runtime", () => runtime.Update(0.016f));
    Measure("preview_unchanged_emit", () => emit.Update(0.016f));
    for (int i = 0; i < members; i++)
        Add(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.DestroyScopedPresenter,
            PresenterDefinitionId = previewDefId,
            Source = owners[i], ScopeTag = i + 1,
            ScopeSource = PresenterCommandScopeSource.Fixed,
        });
    Measure("preview_destroy", () => runtime.Update(0.016f));
    Check(ownerCount);
    commands.Clear();
    events.Clear();
    EnqueueCreate(selectedDefId);
    Measure("selected_create", () => runtime.Update(0.016f));
    Measure("selected_first_emit", () => emit.Update(0.016f));
    Check(ownerCount + members);

    void Check(int expected)
    {
        if (instances.ActiveCount != expected || visualIds.Count != expected || cache.Count != expected)
            throw new InvalidOperationException($"Expected {expected}, got presenters={instances.ActiveCount}, keys={visualIds.Count}, cache={cache.Count}.");
        for (int i = 0; i < ownerCount; i++)
            if (!world.IsAlive(roots[i]) || !world.IsAlive(owners[i]))
                throw new InvalidOperationException("Owner or root was removed.");
    }

    void EnqueueCreate(int defId)
    {
        for (int i = 0; i < members; i++)
            Add(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                Source = owners[i], Target = Entity.Null, Viewer = Entity.Null,
                ParentEntity = roots[i], ScopeTag = i + 1,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
            });
    }

    void Add(PresenterCommand command)
    {
        if (!commands.TryAdd(in command))
            throw new InvalidOperationException("Command capacity exhausted.");
    }

    void Measure(string phase, Action action)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        action();
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (repeat < 0) return;
        string row = FormattableString.Invariant($"{capacity},{visualIds.Capacity},{ownerCount},{members},{repeat},{phase},{milliseconds:F6},{allocated},{instances.ActiveCount},{visualIds.Count},{cache.Count}");
        csv.WriteLine(row);
        csv.Flush();
        Console.WriteLine(row);
    }
}

static PresenterDefinition Definition() => new()
{
    Behaviors = [new BehaviorSlot
    {
        SlotIndex = 0, Kind = BehaviorKind.AssetBinding, ActiveByDefault = true,
        AssetBinding = new AssetBindingConfig
        {
            AssetKind = AssetKind.Mesh, AssetId = 1, MaterialId = 1,
            Mobility = VisualMobility.Static, RenderPath = VisualRenderPath.StaticMesh,
            LocalScale = Vector3.One, AssetIdParamKey = -1, AssetSwapParamKey = -1,
        },
    }],
};
