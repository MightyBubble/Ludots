using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Panel instance lifecycle and refresh service. Both entry paths —
    /// the CreatePanel/DestroyPanel graph ops (via IGraphRuntimeApi) and direct system
    /// code — terminate here; there is no second instantiation path.
    ///
    /// Refresh model: variables are pull-based. A variable declared `"realtime": true`
    /// is re-evaluated by <see cref="RefreshRealtime"/>; anything else only moves when
    /// someone calls <see cref="Refresh"/> on the instance handle. No implicit
    /// full-panel invalidation machinery.
    /// </summary>
    public sealed class PanelHost
    {
        private readonly PanelTemplateRegistry _templates;
        private readonly PanelProjectionReader _reader;
        private readonly PanelListProjector? _listProjector;
        private readonly List<Entry> _entries = new();
        private readonly Stack<int> _freeSlots = new();

        public PanelHost(PanelTemplateRegistry templates, PanelProjectionReader reader, IPanelGraphEvaluator? graphEvaluator = null)
            : this(templates, reader, graphEvaluator, listProjector: null)
        {
        }

        public PanelHost(
            PanelTemplateRegistry templates,
            PanelProjectionReader reader,
            IPanelGraphEvaluator? graphEvaluator,
            PanelListProjector? listProjector)
        {
            _writerThreadId = Environment.CurrentManagedThreadId;
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _graphEvaluator = graphEvaluator;
            _listProjector = listProjector;
        }

        private readonly IPanelGraphEvaluator? _graphEvaluator;
        private readonly int _writerThreadId;

        public int Count { get; private set; }

        /// <summary>Instances auto-collected by the last <see cref="RefreshRealtime"/> because their scope entity died.</summary>
        public int AutoCollectedLastRefresh { get; private set; }

        /// <summary>
        /// Creates a live instance and evaluates it once, so authoring mistakes
        /// (missing attributes, ghost output keys) fail here — not on first paint.
        /// </summary>
        public PanelInstanceHandle Instantiate(string templateId, string anchor, Entity scope)
        {
            return Instantiate(templateId, anchor, scope, null, 100);
        }

        public PanelInstanceHandle Instantiate(string templateId, string anchor, Entity scope, string? skin, int zOrder)
        {
            if (string.IsNullOrWhiteSpace(anchor))
            {
                throw new ArgumentException($"Panel '{templateId}' requires a non-empty anchor.", nameof(anchor));
            }

            PanelTemplate template = _templates.Require(templateId);
            var entry = new Entry(template, anchor.Trim(), scope, skin, zOrder);
            EvaluateAll(entry);

            int slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _entries.Count;
            int generation = slot < _entries.Count ? _entries[slot].Generation + 1 : 1;
            entry.Generation = generation;
            if (slot < _entries.Count)
            {
                _entries[slot] = entry;
            }
            else
            {
                _entries.Add(entry);
            }

            Count++;
            return new PanelInstanceHandle(slot, generation);
        }

        public bool Dispose(PanelInstanceHandle handle)
        {
            if (!TryGetEntry(handle, out Entry? entry))
            {
                return false;
            }

            entry.Alive = false;
            _freeSlots.Push(handle.Id);
            Count--;
            return true;
        }

        /// <summary>Manual full refresh of one instance; stale handles throw.</summary>
        public PanelVariableSet Refresh(PanelInstanceHandle handle)
        {
            Entry entry = RequireEntry(handle);
            EvaluateAll(entry);
            return Snapshot(entry);
        }

        /// <summary>
        /// Re-evaluates only realtime variables across all live instances.
        /// Returns how many instances had at least one value change.
        /// </summary>
        public int RefreshRealtime()
        {
            int touched = 0;
            int autoCollected = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                bool hasCollections = entry.Template.Collections.Count > 0;
                if (!entry.Alive || (!entry.HasRealtime && !hasCollections))
                {
                    continue;
                }

                // scope 实体死亡即回收：单位死亡是正常游戏事件，不是作者漏调 DestroyPanel；
                // 每帧刷新不能把整条模拟拖炸。
                if (!_reader.IsOwnerLive(entry.Scope))
                {
                    entry.Alive = false;
                    _freeSlots.Push(i);
                    Count--;
                    autoCollected++;
                    continue;
                }

                if (!EvaluateGraph(entry))
                {
                    continue;
                }

                bool changed = false;
                foreach (PanelPin pin in entry.Template.Pins)
                {
                    if (!pin.Realtime)
                    {
                        continue;
                    }

                    PanelProjectionValue value = _reader.Resolve(entry.Scope, pin);
                    uint previous = entry.Revisions[pin.Name];
                    if (previous != value.Revision)
                    {
                        entry.Values[pin.Name] = value.FloatValue;
                        entry.Revisions[pin.Name] = value.Revision;
                        entry.Revision = (entry.Revision ^ previous ^ value.Revision) * 16777619;
                        changed = true;
                    }
                }

                if (hasCollections)
                {
                    ProjectLists(entry);
                    changed = true;
                }

                if (changed)
                {
                    touched++;
                }
            }

            AutoCollectedLastRefresh = autoCollected;
            return touched;
        }

        public bool TryGetValues(PanelInstanceHandle handle, out PanelVariableSet values)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                values = Snapshot(entry);
                return true;
            }

            values = null!;
            return false;
        }

        public bool TryGetListProjections(PanelInstanceHandle handle, out IReadOnlyList<PanelListProjection> lists)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                lists = entry.ListProjections;
                return true;
            }

            lists = Array.Empty<PanelListProjection>();
            return false;
        }

        public bool TryGetTemplate(PanelInstanceHandle handle, out PanelTemplate template)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                template = entry.Template;
                return true;
            }

            template = null!;
            return false;
        }

        public bool TryGetAnchor(PanelInstanceHandle handle, out string anchor)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                anchor = entry.Anchor;
                return true;
            }

            anchor = string.Empty;
            return false;
        }

        public bool TryGetScope(PanelInstanceHandle handle, out Entity scope)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                scope = entry.Scope;
                return true;
            }

            scope = Entity.Null;
            return false;
        }

        public bool TryGetTemplateId(PanelInstanceHandle handle, out string templateId)
        {
            if (TryGetEntry(handle, out Entry? entry))
            {
                templateId = entry.Template.Id;
                return true;
            }

            templateId = string.Empty;
            return false;
        }

        /// <summary>Disposes every instance whose (template, scope) matches; scope Null matches any scope.</summary>
        public int DisposeMatching(string templateId, Entity scope)
        {
            int disposed = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (!entry.Alive ||
                    !string.Equals(entry.Template.Id, templateId, StringComparison.Ordinal) ||
                    (scope != Entity.Null && entry.Scope != scope))
                {
                    continue;
                }

                entry.Alive = false;
                _freeSlots.Push(i);
                Count--;
                disposed++;
            }

            return disposed;
        }

        /// <summary>Live instance listing for surface adapters: handle, template, anchor, scope, revision.</summary>
        public IReadOnlyList<PanelHostInstanceInfo> SnapshotInstances()
        {
            var list = new List<PanelHostInstanceInfo>(Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry.Alive)
                {
                    list.Add(new PanelHostInstanceInfo(
                        new PanelInstanceHandle(i, entry.Generation),
                        entry.Template.Id,
                        entry.Anchor,
                        entry.Scope,
                        entry.Revision,
                        entry.Skin,
                        entry.ZOrder));
                }
            }

            return list;
        }

        private void EvaluateAll(Entry entry)
        {
            EvaluateGraph(entry);
            uint revision = 2166136261;
            foreach (PanelPin pin in entry.Template.Pins)
            {
                PanelProjectionValue value = _reader.Resolve(entry.Scope, pin);
                entry.Values[pin.Name] = value.FloatValue;
                revision = (revision ^ value.Revision) * 16777619;
                entry.Revisions[pin.Name] = value.Revision;
                entry.HasRealtime |= pin.Realtime;
            }

            ProjectLists(entry);
            entry.Revision = revision;
        }

        private void ProjectLists(Entry entry)
        {
            if (_listProjector == null || entry.Template.Collections.Count == 0)
            {
                entry.ListProjections = Array.Empty<PanelListProjection>();
                return;
            }

            entry.ListProjections = _listProjector.Project(entry.Scope, entry.Template);
        }

        /// <summary>
        /// Data-plane contract: graph execution failure logs and leaves previous/default
        /// values standing — the panel keeps rendering; structural failures were rejected
        /// at load. No evaluator (lightweight hosts) means read-only against the store.
        /// </summary>
        /// <summary>
        /// Returns false when this pass's evaluation FAILED — the realtime sweep then
        /// skips re-reading pins so externally written store values cannot leak through
        /// a failed evaluation. No evaluator (lightweight hosts) or an unregistered
        /// graph means "the store is the source": not a failure, returns true.
        /// </summary>
        private bool EvaluateGraph(Entry entry)
        {
            System.Diagnostics.Debug.Assert(
                Environment.CurrentManagedThreadId == _writerThreadId,
                "PanelHost is single-writer: all mutations must run on the thread that constructed it.");
            if (_graphEvaluator == null || entry.Template.GraphId < 0)
            {
                return true;
            }

            try
            {
                _graphEvaluator.Evaluate(entry.Template.GraphId, entry.Scope);
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(
                    in Diagnostics.LogChannels.Engine,
                    $"[PanelHost] graph '{entry.Template.Graph}' evaluation failed for panel '{entry.Template.Id}'; pins keep previous/default values: {ex.Message}");
                return false;
            }
        }

        private static PanelVariableSet Snapshot(Entry entry)
        {
            return new PanelVariableSet(
                entry.Template.Id,
                new Dictionary<string, float>(entry.Values, StringComparer.Ordinal),
                entry.Revision);
        }

        private Entry RequireEntry(PanelInstanceHandle handle)
        {
            if (!TryGetEntry(handle, out Entry? entry))
            {
                throw new InvalidOperationException($"Panel instance handle {handle.Id}#{handle.Generation} is stale or was not created by this host.");
            }

            return entry;
        }

        private bool TryGetEntry(PanelInstanceHandle handle, out Entry? entry)
        {
            entry = null;
            return handle.IsValid &&
                handle.Id < _entries.Count &&
                _entries[handle.Id] is { Alive: true } candidate &&
                candidate.Generation == handle.Generation &&
                (entry = candidate) != null;
        }

        private sealed class Entry
        {
            public Entry(PanelTemplate template, string anchor, Entity scope, string? skin, int zOrder)
            {
                Template = template;
                Anchor = anchor;
                Scope = scope;
                Skin = skin;
                ZOrder = zOrder;
                Values = new Dictionary<string, float>(template.Pins.Count, StringComparer.Ordinal);
                Revisions = new Dictionary<string, uint>(template.Pins.Count, StringComparer.Ordinal);
            }

            public PanelTemplate Template { get; }
            public string Anchor { get; }
            public Entity Scope { get; }
            public string? Skin { get; }
            public int ZOrder { get; }
            public Dictionary<string, float> Values { get; }
            public Dictionary<string, uint> Revisions { get; }
            public IReadOnlyList<PanelListProjection> ListProjections { get; set; } = Array.Empty<PanelListProjection>();
            public uint Revision { get; set; }
            public int Generation { get; set; }
            public bool Alive { get; set; } = true;
            public bool HasRealtime { get; set; }
        }
    }
}
