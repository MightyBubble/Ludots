using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using AssociationStressShowcaseMod.Input;
using AssociationStressShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Modding;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace AssociationStressShowcaseMod.Runtime;

public sealed class AssociationStressShowcaseRuntime
{
    private readonly IModContext _context;
    private readonly AssociationStressPanelController _panelController;
    private readonly StringIntRegistry _collectionKeys = new(capacity: 16, comparer: StringComparer.Ordinal);
    private readonly KnowledgeProjectionStore _knowledge = new(initialCapacity: 64);
    private readonly EntityCollectionStore _collections;
    private readonly List<Entity> _members = new(4096);
    private readonly List<Entity> _viewers = new(256);
    private readonly List<Entity> _owners = new(512);
    private readonly List<string> _log = new(16);
    private AssociationStressConfig? _config;
    private int _activeScaleIndex;
    private bool _pulseEnabled = true;
    private bool _scenarioReady;
    private long _lastFrameAllocatedBytes;
    private int _lastExpiredCount;
    private int _lastCompactedCount;
    private int _tick;
    private string _status = "Load the showcase map.";

    public AssociationStressShowcaseRuntime(IModContext context)
    {
        _context = context;
        _collections = new EntityCollectionStore(_collectionKeys, initialCollectionCapacity: 64, initialRowCapacity: 256);
        _panelController = new AssociationStressPanelController(this);
    }

    public AssociationStressSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!AssociationStressIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        EnsureConfig(context);
        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine);
        RefreshPanelInternal(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (AssociationStressIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void Advance(GameEngine engine)
    {
        if (!AssociationStressIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureScenario(engine);
        AdvanceSimulationOnly();
        if ((_tick % 6) == 0)
        {
            _lastCompactedCount = _knowledge.Compact();
        }

        _status = $"Pulse {_tick:000}: stride {ResolveCurrentPulseStride()}, expired {_lastExpiredCount}, compacted {_lastCompactedCount}.";
        RefreshPanelInternal(engine);
    }

    public void AdvanceSimulationOnly()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        _tick++;
        _lastExpiredCount = 0;
        _lastCompactedCount = 0;

        if (_pulseEnabled)
        {
            PulseCurrentScale();
        }

        _lastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (AssociationStressIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    public void IncreaseScale(GameEngine engine)
    {
        EnsureScenario(engine);
        if (_config == null)
        {
            return;
        }

        if (_activeScaleIndex < _config.Scales.Length - 1)
        {
            _activeScaleIndex++;
            RebuildScale();
            _status = $"Scale -> {_config.Scales[_activeScaleIndex].Label}";
        }

        RefreshPanelInternal(engine);
    }

    public void DecreaseScale(GameEngine engine)
    {
        EnsureScenario(engine);
        if (_config == null)
        {
            return;
        }

        if (_activeScaleIndex > 0)
        {
            _activeScaleIndex--;
            RebuildScale();
            _status = $"Scale -> {_config.Scales[_activeScaleIndex].Label}";
        }

        RefreshPanelInternal(engine);
    }

    public void TogglePulse(GameEngine engine)
    {
        _pulseEnabled = !_pulseEnabled;
        _status = _pulseEnabled ? "Pulse resumed." : "Pulse paused.";
        RefreshPanelInternal(engine);
    }

    public void Compact(GameEngine engine)
    {
        _lastCompactedCount = _knowledge.Compact();
        _status = $"Compacted {_lastCompactedCount} inactive knowledge rows.";
        RefreshPanelInternal(engine);
    }

    internal AssociationStressPanelState BuildPanelState()
    {
        AssociationStressSnapshot snapshot = BuildSnapshot();
        return new AssociationStressPanelState(
            Header: _config?.Header ?? "Entity Association Core",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            ScaleLine: $"Scale {snapshot.ScaleLabel} | pulse {(_pulseEnabled ? "ON" : "OFF")} | tick {snapshot.Tick}",
            Metrics: new[]
            {
                $"Associations {snapshot.AssociationCount:N0}",
                $"Knowledge active {snapshot.ActiveKnowledgeCount:N0}",
                $"Knowledge physical {snapshot.PhysicalKnowledgeCount:N0}",
                $"Knowledge capacity {snapshot.KnowledgeCapacity:N0}",
                $"Collections {snapshot.CollectionCount:N0}",
                $"Row capacity {snapshot.CollectionRowCapacity:N0}",
                $"Last frame alloc bytes {snapshot.LastFrameAllocatedBytes:N0}",
                $"Expired {snapshot.LastExpiredCount:N0} | Compacted {snapshot.LastCompactedCount:N0}",
            },
            LogLines: _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    private void EnsureConfig(ScriptContext context)
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/Association/association_stress_config.json");
        _config = AssociationStressConfig.Load(stream);
        _activeScaleIndex = Math.Clamp(_config.InitialScaleIndex, 0, _config.Scales.Length - 1);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Association stress config was not loaded.");
        }

        BuildScenarioEntities(engine);
        RebuildScale();
        _scenarioReady = true;
        _status = $"Ready at {_config.Scales[_activeScaleIndex].Label}.";
        PushLog(_status);
    }

    private void BuildScenarioEntities(GameEngine engine)
    {
        ResetScenario();
        World world = engine.World;
        for (int i = 0; i < _config!.ViewerCount; i++)
        {
            Entity viewer = world.Create();
            world.Add(viewer, new Name { Value = $"Association Viewer {i:000}" });
            world.Add(viewer, new MapEntity { MapId = AssociationStressIds.ShowcaseMap });
            _viewers.Add(viewer);
        }

        int maxSquads = 0;
        int maxMembers = 0;
        for (int i = 0; i < _config.Scales.Length; i++)
        {
            if (_config.Scales[i].SquadCount > maxSquads)
            {
                maxSquads = _config.Scales[i].SquadCount;
            }

            int memberCount = _config.Scales[i].SquadCount * _config.Scales[i].MembersPerSquad;
            if (memberCount > maxMembers)
            {
                maxMembers = memberCount;
            }
        }

        for (int i = 0; i < maxSquads; i++)
        {
            Entity owner = world.Create();
            world.Add(owner, new Name { Value = $"Association Squad {i:000}" });
            world.Add(owner, new MapEntity { MapId = AssociationStressIds.ShowcaseMap });
            _owners.Add(owner);
        }

        for (int i = 0; i < maxMembers; i++)
        {
            Entity member = world.Create();
            world.Add(member, new Name { Value = $"{AssociationStressIds.ScenarioLabel}.{i:0000}" });
            world.Add(member, new MapEntity { MapId = AssociationStressIds.ShowcaseMap });
            _members.Add(member);
        }
    }

    private void RebuildScale()
    {
        if (_config == null)
        {
            return;
        }

        _knowledge.Expire(int.MaxValue);
        _knowledge.Compact();
        for (int i = 0; i < _owners.Count; i++)
        {
            _collections.Remove(_owners[i], "association.members");
        }

        ScaleConfig scale = _config.Scales[_activeScaleIndex];
        int totalMembers = scale.SquadCount * scale.MembersPerSquad;
        int memberIndex = 0;
        Span<Entity> rowBuffer = totalMembers <= 256
            ? stackalloc Entity[256]
            : new Entity[Math.Max(totalMembers, 256)];

        for (int squad = 0; squad < scale.SquadCount; squad++)
        {
            Entity owner = _owners[squad];
            Span<Entity> slice = rowBuffer[..scale.MembersPerSquad];
            for (int j = 0; j < scale.MembersPerSquad; j++)
            {
                Entity member = _members[memberIndex++];
                slice[j] = member;
                PublishKnowledge(member, squad, currentTick: 1, expiryTick: 0);
            }

            _collections.Replace(
                owner,
                EntityCollectionDescriptor.Create(
                    "association.members",
                    EntityCollectionSourceKind.Debug,
                    EntityCollectionRoleKind.Display,
                    contextEntity: owner,
                    primaryEntity: owner,
                    title: $"Squad {squad:000}",
                    summary: scale.Label),
                slice);
        }

        _status = $"Built {scale.Label} with {totalMembers:N0} member associations.";
        PushLog(_status);
    }

    private void PulseCurrentScale()
    {
        if (_config == null)
        {
            return;
        }

        ScaleConfig scale = _config.Scales[_activeScaleIndex];
        int totalMembers = scale.SquadCount * scale.MembersPerSquad;
        int pulseStride = Math.Max(1, scale.PulseStride);
        int expiryTick = _tick + _config.ExpiryTickOffset;
        for (int i = 0; i < totalMembers; i += pulseStride)
        {
            PublishKnowledge(_members[i], i, _tick, expiryTick);
        }

        _lastExpiredCount = _knowledge.Expire(_tick);
    }

    private int ResolveCurrentPulseStride()
    {
        return _config == null
            ? 1
            : Math.Max(1, _config.Scales[_activeScaleIndex].PulseStride);
    }

    private void PublishKnowledge(Entity member, int index, int currentTick, int expiryTick)
    {
        if (_viewers.Count == 0)
        {
            return;
        }

        var empty = default(KnowledgeIdMask256);
        for (int i = 0; i < _viewers.Count; i++)
        {
            Entity viewer = _viewers[i];
            _knowledge.Upsert(
                viewer,
                member,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.Known,
                    KnowledgePositionAccess.LastKnown,
                    empty,
                    empty,
                    empty,
                    source: member,
                    observedTick: currentTick,
                    expiryTick: (index + i) % 3 == 0 ? expiryTick : 0,
                    confidencePermille: 1000,
                    revision: 0));
        }
    }

    private void RefreshPanelInternal(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ActivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || !input.HasContext(AssociationStressInputContexts.Showcase))
        {
            return;
        }

        input.PushContext(AssociationStressInputContexts.Showcase);
    }

    private void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext(AssociationStressInputContexts.Showcase);
    }

    private void ResetScenario()
    {
        _scenarioReady = false;
        _pulseEnabled = true;
        _tick = 0;
        _lastFrameAllocatedBytes = 0;
        _lastExpiredCount = 0;
        _lastCompactedCount = 0;
        _members.Clear();
        _viewers.Clear();
        _owners.Clear();
        _log.Clear();
    }

    private AssociationStressSnapshot BuildSnapshot()
    {
        ScaleConfig? scale = _config != null && _activeScaleIndex >= 0 && _activeScaleIndex < _config.Scales.Length
            ? _config.Scales[_activeScaleIndex]
            : null;
        int associationCount = scale == null ? 0 : scale.SquadCount * scale.MembersPerSquad * Math.Max(1, _viewers.Count);
        return new AssociationStressSnapshot(
            ScaleLabel: scale?.Label ?? "n/a",
            AssociationCount: associationCount,
            ActiveKnowledgeCount: _knowledge.RecordCount,
            PhysicalKnowledgeCount: _knowledge.PhysicalRecordCount,
            KnowledgeCapacity: _knowledge.RecordCapacity,
            CollectionCount: _collections.CollectionCount,
            CollectionRowCapacity: _collections.RowCapacity,
            LastFrameAllocatedBytes: _lastFrameAllocatedBytes,
            LastExpiredCount: _lastExpiredCount,
            LastCompactedCount: _lastCompactedCount,
            PulseEnabled: _pulseEnabled,
            Tick: _tick,
            Status: _status);
    }

    private void PushLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _log.Add($"[T+{_tick:000}] {message}");
        if (_log.Count > 10)
        {
            _log.RemoveAt(0);
        }
    }
}
