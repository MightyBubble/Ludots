using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// GAS chain order tag configuration - loaded from GameConfig.Constants.GasOrderTags
    /// </summary>
    public struct GasChainOrderTags
    {
        public int ChainPass;
        public int ChainNegate;
        public int ChainActivateEffect;
        
        public static GasChainOrderTags Default => new GasChainOrderTags
        {
            ChainPass = 1,
            ChainNegate = 2,
            ChainActivateEffect = 3
        };
    }

    public sealed class EffectProposalProcessingSystem : BaseSystem<World, float>, ITimeSlicedSystem
    {
        private readonly EffectRequestQueue _queue;
        private readonly GasBudget _budget;
        private readonly EffectTemplateRegistry _templates;
        private readonly InputRequestQueue _inputRequests;
        private readonly OrderQueue _chainOrders;
        private readonly ResponseChainTelemetryBuffer _telemetry;
        private readonly OrderRequestQueue _orderRequests;
        private readonly GasChainOrderTags _chainTags;
        private readonly GasPresentationEventBuffer _presentationEvents;

        // ── Phase Graph execution (optional, null = legacy-only mode) ──
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi _graphApiHost;

        private static readonly QueryDescription _listenersQuery = new QueryDescription().WithAll<ResponseChainListener>();

        private readonly List<Entity> _listeners = new(1024);
        private readonly ProposalWindow _window = new();
        private readonly ProposalResponseQueue _responseQueue = new();

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public byte DebugWindowPhase => (byte)_phase;

        private bool _sliceActive;
        private int _rootCursor;
        private int _rootCountSnapshot;
        private bool _listenersDirty = true;

        private enum WindowPhase : byte
        {
            None = 0,
            Collect = 1,
            WaitInput = 2,
            Resolve = 3
        }

        private WindowPhase _phase;
        private EffectRequest _activeReq;
        private int _responseSteps;
        private int _creates;
        private int _passStreak;
        private int _pendingNegates;
        private int _resolveIndex;
        private int _resolveNegatesRemaining;
        private bool _interactiveRequested;
        private bool _closeRequested;
        private bool _inputRequestSent;
        private int _inputRequestTagId;
        private int _nextWindowId = 1;

        private sealed class EntityStableComparer : IComparer<Entity>
        {
            public static readonly EntityStableComparer Instance = new EntityStableComparer();

            public int Compare(Entity x, Entity y)
            {
                int c = x.WorldId.CompareTo(y.WorldId);
                if (c != 0) return c;
                c = x.Id.CompareTo(y.Id);
                if (c != 0) return c;
                return x.Version.CompareTo(y.Version);
            }
        }

        private struct ProposalResponseItem
        {
            public int ProposalIndex;
            public Entity ResponseEntity;
            public ResponseType Type;
            public int Priority;
            public int StableSequence;
            public float ModifyValue;
            public ModifierOp ModifyOp;
            public int EffectTemplateId;
        }

        private sealed class ProposalWindow
        {
            private readonly EffectProposal[] _items = new EffectProposal[GasConstants.MAX_DEPTH];
            private int _count;

            public int Count => _count;

            public EffectProposal this[int index]
            {
                get => _items[index];
                set => _items[index] = value;
            }

            public void Clear()
            {
                _count = 0;
            }

            public bool TryAdd(EffectProposal proposal)
            {
                if (_count >= _items.Length) return false;
                _items[_count++] = proposal;
                return true;
            }
        }

        private sealed class ProposalResponseQueue
        {
            private readonly Node[] _nodes = new Node[GasConstants.MAX_RESPONSES_PER_WINDOW];
            private int _count;

            private struct Node
            {
                public ProposalResponseItem Item;
            }

            public bool IsEmpty => _count == 0;

            public void Clear()
            {
                _count = 0;
            }

            public bool TryEnqueue(ProposalResponseItem item)
            {
                if (_count >= _nodes.Length) return false;

                _nodes[_count] = new Node { Item = item };
                HeapifyUp(_count);
                _count++;
                return true;
            }

            public bool TryDequeue(out ProposalResponseItem item)
            {
                if (_count == 0)
                {
                    item = default;
                    return false;
                }

                item = _nodes[0].Item;
                _count--;
                if (_count > 0)
                {
                    _nodes[0] = _nodes[_count];
                    HeapifyDown(0);
                }
                return true;
            }

            private void HeapifyUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (!IsHigherPriority(_nodes[index], _nodes[parent])) return;
                    (_nodes[index], _nodes[parent]) = (_nodes[parent], _nodes[index]);
                    index = parent;
                }
            }

            private void HeapifyDown(int index)
            {
                while (true)
                {
                    int left = (index << 1) + 1;
                    if (left >= _count) return;

                    int right = left + 1;
                    int best = left;
                    if (right < _count && IsHigherPriority(_nodes[right], _nodes[left])) best = right;
                    if (!IsHigherPriority(_nodes[best], _nodes[index])) return;

                    (_nodes[index], _nodes[best]) = (_nodes[best], _nodes[index]);
                    index = best;
                }
            }

            private static bool IsHigherPriority(in Node a, in Node b)
            {
                if (a.Item.Priority != b.Item.Priority) return a.Item.Priority > b.Item.Priority;

                int aTemplateId = a.Item.EffectTemplateId;
                int bTemplateId = b.Item.EffectTemplateId;
                if (aTemplateId != bTemplateId) return aTemplateId < bTemplateId;

                int aWorldId = a.Item.ResponseEntity.WorldId;
                int bWorldId = b.Item.ResponseEntity.WorldId;
                if (aWorldId != bWorldId) return aWorldId < bWorldId;

                int aId = a.Item.ResponseEntity.Id;
                int bId = b.Item.ResponseEntity.Id;
                if (aId != bId) return aId < bId;

                int aVersion = a.Item.ResponseEntity.Version;
                int bVersion = b.Item.ResponseEntity.Version;
                if (aVersion != bVersion) return aVersion < bVersion;

                return a.Item.StableSequence < b.Item.StableSequence;
            }
        }

        public EffectProposalProcessingSystem(World world, EffectRequestQueue queue, GasBudget budget = null, EffectTemplateRegistry templates = null, InputRequestQueue inputRequests = null, OrderQueue chainOrders = null, ResponseChainTelemetryBuffer telemetry = null, OrderRequestQueue orderRequests = null, GasChainOrderTags? chainTags = null, GasPresentationEventBuffer presentationEvents = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null)
            : base(world)
        {
            _queue = queue;
            _budget = budget;
            _templates = templates;
            _inputRequests = inputRequests;
            _chainOrders = chainOrders;
            _telemetry = telemetry;
            _orderRequests = orderRequests;
            _chainTags = chainTags ?? GasChainOrderTags.Default;
            _presentationEvents = presentationEvents;
            _phaseExecutor = phaseExecutor;
            _graphApiHost = graphApi;
            _graphApi = graphApi;
        }

        public override void Update(in float dt)
        {
            int prev = MaxWorkUnitsPerSlice;
            MaxWorkUnitsPerSlice = int.MaxValue;
            while (!UpdateSlice(dt, int.MaxValue)) { }
            MaxWorkUnitsPerSlice = prev;
        }

        public void MarkListenersDirty()
        {
            _listenersDirty = true;
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            if (_queue == null || _queue.Count == 0)
            {
                _sliceActive = false;
                return true;
            }

            if (!_sliceActive)
            {
                _sliceActive = true;
                _rootCursor = 0;
                _rootCountSnapshot = _queue.Count;
                _phase = WindowPhase.None;

                if (_listenersDirty)
                {
                    _listenersDirty = false;
                    _listeners.Clear();
                    var job = new CollectListenerEntitiesJob { Entities = _listeners };
                    World.InlineEntityQuery<CollectListenerEntitiesJob, ResponseChainListener>(in _listenersQuery, ref job);
                    if (_listeners.Count > 1) _listeners.Sort(EntityStableComparer.Instance);
                }
            }

            int workUnits = 0;
            while (true)
            {
                if (workUnits >= MaxWorkUnitsPerSlice) return false;

                if (_phase == WindowPhase.None)
                {
                    if (_rootCursor >= _rootCountSnapshot)
                    {
                        _queue.ConsumePrefix(_rootCountSnapshot);
                        _sliceActive = false;
                        return true;
                    }

                    var req = _queue[_rootCursor++];
                    if (!World.IsAlive(req.Target))
                    {
                        workUnits++;
                        continue;
                    }

                    if (_templates == null || req.TemplateId <= 0 || !_templates.TryGet(req.TemplateId, out var rootTpl))
                    {
                        workUnits++;
                        continue;
                    }

                    _activeReq = req;
                    _phase = WindowPhase.Collect;
                    _responseQueue.Clear();
                    _window.Clear();
                    _responseSteps = 0;
                    _creates = 0;
                    _passStreak = 0;
                    _pendingNegates = 0;
                    _resolveIndex = -1;
                    _resolveNegatesRemaining = 0;
                    _interactiveRequested = false;
                    _closeRequested = false;
                    _inputRequestSent = false;
                    _inputRequestTagId = 0;

                    var rootModifiers = rootTpl.Modifiers;
                    ApplyPresetModifiers(ref rootModifiers, in rootTpl, in req);
                    var root = new EffectProposal
                    {
                        RootId = req.RootId,
                        Source = req.Source,
                        Target = req.Target,
                        TargetContext = req.TargetContext,
                        TemplateId = req.TemplateId,
                        TagId = rootTpl.TagId,
                        ParticipatesInResponse = rootTpl.ParticipatesInResponse,
                        Cancelled = false,
                        Modifiers = rootModifiers,
                        CallerParams = req.CallerParams,
                        HasCallerParams = req.HasCallerParams,
                    };
                    if (!_window.TryAdd(root))
                    {
                        if (_budget != null) _budget.ResponseDepthDropped++;
                        _phase = WindowPhase.None;
                        workUnits++;
                        continue;
                    }
 
                    if (_telemetry != null)
                    {
                        _telemetry.TryAdd(new ResponseChainTelemetryEvent
                        {
                            Kind = ResponseChainTelemetryKind.WindowOpened,
                            RootId = req.RootId,
                            TemplateId = req.TemplateId,
                            TagId = rootTpl.TagId,
                            ProposalIndex = 0,
                            Source = req.Source,
                            Target = req.Target,
                            Context = req.TargetContext
                        });
                    }

                    // ── Execute OnPropose Phase Graphs (before ResponseChain) ──
                    ExecuteOnProposePhase(in root, in rootTpl);

                    if (rootTpl.ParticipatesInResponse)
                    {
                        EnqueueResponsesForEffect(proposalIndex: 0, effectTagId: rootTpl.TagId);
                        if (_budget != null) _budget.ResponseWindows++;
                    }

                    workUnits++;
                    continue;
                }

                if (_phase == WindowPhase.Collect)
                {
                    if (!_responseQueue.IsEmpty)
                    {
                        if (_responseSteps++ >= GasConstants.MAX_RESPONSE_STEPS_PER_WINDOW)
                        {
                            if (_budget != null) _budget.ResponseStepBudgetFused++;
                            _responseQueue.Clear();
                            _closeRequested = true;
                        }
                        else if (_responseQueue.TryDequeue(out var response))
                        {
                            if ((uint)response.ProposalIndex < (uint)_window.Count)
                            {
                                var eff = _window[response.ProposalIndex];
                                switch (response.Type)
                                {
                                    case ResponseType.Hook:
                                        eff.Cancelled = true;
                                        _window[response.ProposalIndex] = eff;
                                        break;

                                    case ResponseType.Modify:
                                        ApplyModify(ref eff.Modifiers, response.ModifyValue, response.ModifyOp);
                                        _window[response.ProposalIndex] = eff;
                                        break;

                                    case ResponseType.Chain:
                                        if (_creates >= GasConstants.MAX_CREATES_PER_ROOT)
                                        {
                                            if (_budget != null) _budget.ResponseCreatesDropped++;
                                            break;
                                        }
                                        if (_templates == null || response.EffectTemplateId <= 0 || !_templates.TryGet(response.EffectTemplateId, out var tpl))
                                        {
                                            break;
                                        }

                                        var chainedModifiers = tpl.Modifiers;
                                        ApplyPresetModifiers(ref chainedModifiers, in tpl, in _activeReq);
                                        var chained = new EffectProposal
                                        {
                                            RootId = _activeReq.RootId,
                                            Source = _activeReq.Source,
                                            Target = _activeReq.Target,
                                            TargetContext = _activeReq.TargetContext,
                                            TemplateId = response.EffectTemplateId,
                                            TagId = tpl.TagId,
                                            ParticipatesInResponse = tpl.ParticipatesInResponse,
                                            Cancelled = false,
                                            Modifiers = chainedModifiers
                                        };

                                        int newIndex = _window.Count;
                                        if (!_window.TryAdd(chained))
                                        {
                                            if (_budget != null) _budget.ResponseDepthDropped++;
                                            break;
                                        }
                                        _creates++;
                                        if (_budget != null) _budget.ResponseCreates++;

                                        if (tpl.ParticipatesInResponse)
                                        {
                                            EnqueueResponsesForEffect(newIndex, tpl.TagId);
                                        }
                                        break;

                                    case ResponseType.PromptInput:
                                        _interactiveRequested = true;
                                        if (_inputRequestTagId == 0) _inputRequestTagId = response.EffectTemplateId;
                                        break;
                                }
                            }
                        }

                        workUnits++;
                        continue;
                    }

                    if (_budget != null && _responseSteps > 0) _budget.ResponseSteps += _responseSteps;
                    _responseSteps = 0;

                    if (_interactiveRequested && !_closeRequested)
                    {
                        _phase = WindowPhase.WaitInput;
                        continue;
                    }

                    _resolveIndex = _window.Count - 1;
                    _resolveNegatesRemaining = _pendingNegates;
                    _phase = WindowPhase.Resolve;
                    continue;
                }

                if (_phase == WindowPhase.WaitInput)
                {
                    if (!_inputRequestSent && _inputRequests != null && _inputRequestTagId > 0 && _window.Count > 0)
                    {
                        var windowId = _nextWindowId++;
                        _inputRequests.TryEnqueue(new InputRequest
                        {
                            RequestId = windowId,
                            RequestTagId = _inputRequestTagId,
                            Source = _window[0].Source,
                            Context = _window[0].TargetContext,
                            PayloadA = 0,
                            PayloadB = 0
                        });
                        _inputRequestSent = true;
 
                        if (_telemetry != null)
                        {
                            _telemetry.TryAdd(new ResponseChainTelemetryEvent
                            {
                                Kind = ResponseChainTelemetryKind.PromptRequested,
                                RootId = _activeReq.RootId,
                                TemplateId = _activeReq.TemplateId,
                                TagId = _window[0].TagId,
                                ProposalIndex = 0,
                                PromptTagId = _inputRequestTagId,
                                Source = _window[0].Source,
                                Target = _window[0].Target,
                                Context = _window[0].TargetContext
                            });
                        }
 
                        if (_orderRequests != null)
                        {
                            // Resolve PlayerId from source entity's PlayerOwner component
                            int playerId = 0;
                            var src = _window[0].Source;
                            if (World.IsAlive(src) && World.Has<PlayerOwner>(src))
                            {
                                playerId = World.Get<PlayerOwner>(src).PlayerId;
                            }

                            var req = new OrderRequest
                            {
                                RequestId = _activeReq.RootId,
                                PromptTagId = _inputRequestTagId,
                                PlayerId = playerId,
                                Actor = src,
                                Target = _window[0].Target,
                                TargetContext = _window[0].TargetContext,
                                AllowedCount = 0
                            };
                            req.AddAllowed(_chainTags.ChainPass);
                            req.AddAllowed(_chainTags.ChainNegate);
                            if (_inputRequestTagId > 0) req.AddAllowed(_chainTags.ChainActivateEffect);
                            _orderRequests.TryEnqueue(req);
                        }
                    }

                    bool progressed = false;
                    if (_chainOrders != null)
                    {
                        while (_chainOrders.TryDequeue(out var order))
                        {
                            if (workUnits >= MaxWorkUnitsPerSlice) return false;
                            progressed = true;

                            if (order.OrderTagId == _chainTags.ChainPass)
                            {
                                if (_telemetry != null)
                                {
                                    _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                    {
                                        Kind = ResponseChainTelemetryKind.OrderConsumed,
                                        RootId = _activeReq.RootId,
                                        TemplateId = _activeReq.TemplateId,
                                        TagId = _window[0].TagId,
                                        ProposalIndex = 0,
                                        OrderTagId = order.OrderTagId,
                                        Source = order.Actor,
                                        Target = order.Target,
                                        Context = order.TargetContext
                                    });
                                }
                                _passStreak++;
                                if (_passStreak >= 2)
                                {
                                    _closeRequested = true;
                                    break;
                                }

                                workUnits++;
                                continue;
                            }

                            _passStreak = 0;
                            if (order.OrderTagId == _chainTags.ChainNegate)
                            {
                                if (_telemetry != null)
                                {
                                    _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                    {
                                        Kind = ResponseChainTelemetryKind.OrderConsumed,
                                        RootId = _activeReq.RootId,
                                        TemplateId = _activeReq.TemplateId,
                                        TagId = _window[0].TagId,
                                        ProposalIndex = 0,
                                        OrderTagId = order.OrderTagId,
                                        Source = order.Actor,
                                        Target = order.Target,
                                        Context = order.TargetContext
                                    });
                                }
                                _pendingNegates++;
                                workUnits++;
                                continue;
                            }

                            if (order.OrderTagId == _chainTags.ChainActivateEffect && order.Args.I0 > 0)
                            {
                                if (_telemetry != null)
                                {
                                    _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                    {
                                        Kind = ResponseChainTelemetryKind.OrderConsumed,
                                        RootId = _activeReq.RootId,
                                        TemplateId = order.Args.I0,
                                        TagId = _window[0].TagId,
                                        ProposalIndex = 0,
                                        OrderTagId = order.OrderTagId,
                                        Source = order.Actor,
                                        Target = order.Target,
                                        Context = order.TargetContext
                                    });
                                }
                                if (_creates >= GasConstants.MAX_CREATES_PER_ROOT)
                                {
                                    if (_budget != null) _budget.ResponseCreatesDropped++;
                                    workUnits++;
                                    continue;
                                }
                                if (_templates == null || !_templates.TryGet(order.Args.I0, out var tpl))
                                {
                                    workUnits++;
                                    continue;
                                }

                                var chainedModifiers = tpl.Modifiers;
                                ApplyPresetModifiers(ref chainedModifiers, in tpl, in _activeReq);
                                var chained = new EffectProposal
                                {
                                    RootId = _activeReq.RootId,
                                    Source = World.IsAlive(order.Actor) ? order.Actor : _activeReq.Source,
                                    Target = _activeReq.Target,
                                    TargetContext = _activeReq.TargetContext,
                                    TemplateId = order.Args.I0,
                                    TagId = tpl.TagId,
                                    ParticipatesInResponse = tpl.ParticipatesInResponse,
                                    Cancelled = false,
                                    Modifiers = chainedModifiers
                                };

                                int newIndex = _window.Count;
                                if (!_window.TryAdd(chained))
                                {
                                    if (_budget != null) _budget.ResponseDepthDropped++;
                                    workUnits++;
                                    continue;
                                }
                                _creates++;
                                if (_budget != null) _budget.ResponseCreates++;
                                if (_telemetry != null)
                                {
                                    _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                    {
                                        Kind = ResponseChainTelemetryKind.ProposalAdded,
                                        RootId = _activeReq.RootId,
                                        TemplateId = chained.TemplateId,
                                        TagId = chained.TagId,
                                        ProposalIndex = newIndex,
                                        Source = chained.Source,
                                        Target = chained.Target,
                                        Context = chained.TargetContext
                                    });
                                }

                                if (tpl.ParticipatesInResponse)
                                {
                                    EnqueueResponsesForEffect(newIndex, tpl.TagId);
                                }

                                _phase = WindowPhase.Collect;
                                workUnits++;
                                goto ContinueOuter;
                            }

                            workUnits++;
                        }
                    }

                    if (_closeRequested)
                    {
                        _resolveIndex = _window.Count - 1;
                        _resolveNegatesRemaining = _pendingNegates;
                        _phase = WindowPhase.Resolve;
                        continue;
                    }

                    if (!progressed)
                    {
                        return true;
                    }

                    workUnits++;
                    continue;

                ContinueOuter:
                    continue;
                }

                if (_phase == WindowPhase.Resolve)
                {
                    while (_resolveIndex >= 0)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice) return false;

                        int i = _resolveIndex--;
                        var e = _window[i];
                        if (e.Cancelled)
                        {
                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.Cancelled,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                            workUnits++;
                            continue;
                        }

                        if (i > 0 && _resolveNegatesRemaining > 0)
                        {
                            _resolveNegatesRemaining--;
                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.Negated,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                            workUnits++;
                            continue;
                        }

                        if (!World.IsAlive(e.Target))
                        {
                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.TargetDead,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                            workUnits++;
                            continue;
                        }

                        if (_templates == null || e.TemplateId <= 0 || !_templates.TryGet(e.TemplateId, out var tpl))
                        {
                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.TemplateMissing,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                            workUnits++;
                            continue;
                        }

                        // ── Execute OnCalculate Phase Graphs (after ResponseChain resolves) ──
                        ExecuteOnCalculatePhase(in e, in tpl);

                        if (IsPureInstantTemplate(in tpl))
                        {
                            ref var attr = ref World.TryGetRef<AttributeBuffer>(e.Target, out bool hasAttr);
                            if (hasAttr)
                            {
                                // Snapshot primary attribute for delta calculation
                                int primaryAttrId = e.Modifiers.Count > 0 ? e.Modifiers.Get(0).AttributeId : -1;
                                float before = primaryAttrId >= 0 ? attr.GetCurrent(primaryAttrId) : 0f;
                                EffectModifierOps.Apply(in e.Modifiers, ref attr);
                                float after = primaryAttrId >= 0 ? attr.GetCurrent(primaryAttrId) : 0f;
                                float delta = after - before;
                                if (_presentationEvents != null && delta != 0f)
                                {
                                    _presentationEvents.Publish(new GasPresentationEvent
                                    {
                                        Kind = GasPresentationEventKind.EffectApplied,
                                        Actor = e.Source,
                                        Target = e.Target,
                                        EffectTemplateId = e.TemplateId,
                                        AttributeId = primaryAttrId,
                                        Delta = delta
                                    });
                                }
                            }

                            // Dispatch OnApply Phase Listeners even for pure-instant effects.
                            // Modifiers are applied inline above (equivalent to Main handler),
                            // but Listeners must still fire for observability — e.g. "whenever
                            // damage is dealt, draw a card" or "thorns: reflect damage on hit".
                            if (_phaseExecutor != null && _graphApi != null)
                            {
                                SetMergedConfigContext(in tpl, in e);
                                _phaseExecutor.DispatchPhaseListeners(
                                    World, _graphApi,
                                    e.Source, e.Target, e.TargetContext,
                                    default,
                                    EffectPhaseId.OnApply,
                                    tpl.TagId,
                                    e.TemplateId);
                                ClearConfigContext();
                            }

                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.AppliedInstant,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                        }
                        else
                        {
                            CreateEntityEffect(in e, in tpl);
                            if (_telemetry != null)
                            {
                                _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                {
                                    Kind = ResponseChainTelemetryKind.ProposalResolved,
                                    RootId = _activeReq.RootId,
                                    TemplateId = e.TemplateId,
                                    TagId = e.TagId,
                                    ProposalIndex = i,
                                    Outcome = ResponseChainResolveOutcome.CreatedEffect,
                                    Source = e.Source,
                                    Target = e.Target,
                                    Context = e.TargetContext
                                });
                            }
                        }

                        workUnits++;
                    }

                    if (_telemetry != null)
                    {
                        _telemetry.TryAdd(new ResponseChainTelemetryEvent
                        {
                            Kind = ResponseChainTelemetryKind.WindowClosed,
                            RootId = _activeReq.RootId,
                            TemplateId = _activeReq.TemplateId,
                            TagId = _window.Count > 0 ? _window[0].TagId : 0,
                            ProposalIndex = _window.Count,
                            Source = _activeReq.Source,
                            Target = _activeReq.Target,
                            Context = _activeReq.TargetContext
                        });
                    }
                    _phase = WindowPhase.None;
                    _window.Clear();
                    _responseQueue.Clear();
                    _interactiveRequested = false;
                    _closeRequested = false;
                    _inputRequestSent = false;
                    _pendingNegates = 0;
                    _passStreak = 0;
                    workUnits++;
                    continue;
                }
            }
        }

        public void ResetSlice()
        {
            if (!_sliceActive) return;

            // Consume roots that have already been fully resolved (or partially
            // resolved in Resolve phase where inline instant effects were applied)
            // to prevent double-application on re-processing.
            int consumed = _rootCursor;
            if (_phase == WindowPhase.Collect || _phase == WindowPhase.WaitInput)
            {
                // Current root hasn't been resolved yet — safe to re-process it.
                consumed = _rootCursor > 0 ? _rootCursor - 1 : 0;
            }
            if (consumed > 0 && _queue != null)
            {
                _queue.ConsumePrefix(consumed);
            }

            _window.Clear();
            _responseQueue.Clear();
            _phase = WindowPhase.None;
            _rootCursor = 0;
            _rootCountSnapshot = 0;
            _responseSteps = 0;
            _creates = 0;
            _passStreak = 0;
            _pendingNegates = 0;
            _resolveIndex = 0;
            _resolveNegatesRemaining = 0;
            _interactiveRequested = false;
            _closeRequested = false;
            _inputRequestSent = false;
            _inputRequestTagId = 0;
            _sliceActive = false;
        }

        private struct CollectListenerEntitiesJob : IForEachWithEntity<ResponseChainListener>
        {
            public List<Entity> Entities;

            public void Update(Entity entity, ref ResponseChainListener _)
            {
                Entities.Add(entity);
            }
        }

        private unsafe void EnqueueResponsesForEffect(int proposalIndex, int effectTagId)
        {
            for (int li = 0; li < _listeners.Count; li++)
            {
                var listenerEntity = _listeners[li];
                if (!World.IsAlive(listenerEntity)) continue;

                ref var listener = ref World.TryGetRef<ResponseChainListener>(listenerEntity, out bool hasListener);
                if (!hasListener) continue;
                for (int i = 0; i < listener.Count; i++)
                {
                    int eventTagId = listener.EventTagIds[i];
                    if (eventTagId != 0 && effectTagId != eventTagId) continue;

                                var responseType = (ResponseType)listener.ResponseTypes[i];
                                if (!_responseQueue.TryEnqueue(new ProposalResponseItem
                    {
                        ProposalIndex = proposalIndex,
                        ResponseEntity = listenerEntity,
                                    Type = responseType,
                        Priority = listener.Priorities[i],
                        StableSequence = i,
                                    EffectTemplateId = responseType == ResponseType.Chain || responseType == ResponseType.PromptInput ? listener.EffectTemplateIds[i] : 0,
                        ModifyValue = listener.ModifyValues[i],
                        ModifyOp = (ModifierOp)listener.ModifyOps[i]
                                }))
                                {
                                    if (_budget != null) _budget.ResponseQueueOverflowDropped++;
                                }
                }
            }
        }

        private static void ApplyPresetModifiers(ref EffectModifiers modifiers, in EffectTemplateData tpl, in EffectRequest req)
        {
            switch (tpl.PresetType)
            {
                case EffectPresetType.None:
                    return;
                case EffectPresetType.ApplyForce2D:
                {
                    // Read force values from CallerParams (if present) or template ConfigParams.
                    float fx = 0f, fy = 0f;
                    if (req.HasCallerParams)
                    {
                        req.CallerParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out fx);
                        req.CallerParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out fy);
                    }
                    else
                    {
                        tpl.ConfigParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out fx);
                        tpl.ConfigParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out fy);
                    }
                    modifiers.Add(tpl.PresetAttribute0, ModifierOp.Add, fx);
                    modifiers.Add(tpl.PresetAttribute1, ModifierOp.Add, fy);
                    return;
                }
            }
        }

        private static bool IsPureInstantTemplate(in EffectTemplateData tpl)
        {
            if (tpl.LifetimeKind != EffectLifetimeKind.Instant) return false;
            if (tpl.PeriodTicks > 0) return false;
            // Templates with Phase Graph bindings need entity-based processing
            if (tpl.PhaseGraphBindings.StepCount > 0) return false;
            // Templates with Phase Listeners need entity-based processing (registration occurs on OnApply)
            if (tpl.ListenerSetup.Count > 0) return false;
            // Templates with TargetResolver need entity-based processing for fan-out
            if (tpl.HasTargetResolver) return false;
            // Presets that perform non-modifier side effects require entity-based phase execution.
            if (tpl.PresetType == EffectPresetType.LaunchProjectile) return false;
            if (tpl.PresetType == EffectPresetType.CreateUnit) return false;
            return true;
        }

        private void CreateEntityEffect(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            // ── Stack merge: if template has stack policy and an existing effect exists on target, merge ──
            if (tpl.HasStackPolicy && tpl.LifetimeKind != EffectLifetimeKind.Instant
                && World.IsAlive(proposal.Target) && World.Has<ActiveEffectContainer>(proposal.Target))
            {
                ref var container = ref World.Get<ActiveEffectContainer>(proposal.Target);
                Entity existing = FindExistingEffectByTemplate(in container, proposal.TemplateId);
                if (existing != Entity.Null && World.IsAlive(existing) && World.Has<EffectStack>(existing))
                {
                    ref var stack = ref World.Get<EffectStack>(existing);
                    int oldCount = stack.Count;
                    if (stack.TryAddStack())
                    {
                        // Apply duration policy
                        ref var effect = ref World.Get<GameplayEffect>(existing);
                        switch (tpl.StackPolicy)
                        {
                            case StackPolicy.RefreshDuration:
                                effect.RemainingTicks = tpl.DurationTicks;
                                effect.ExpiresAtTick = 0; // Will be recomputed next tick
                                break;
                            case StackPolicy.AddDuration:
                                effect.RemainingTicks += tpl.DurationTicks;
                                effect.ExpiresAtTick = 0;
                                break;
                            // KeepDuration: do nothing
                        }

                        // Update tag contributions (delta from oldCount → newCount)
                        if (World.Has<EffectGrantedTags>(existing) && World.Has<TagCountContainer>(proposal.Target))
                        {
                            ref readonly var grantedTags = ref World.Get<EffectGrantedTags>(existing);
                            ref var tagCounts = ref World.Get<TagCountContainer>(proposal.Target);
                            EffectTagContributionHelper.Update(in grantedTags, ref tagCounts, oldCount, stack.Count, _budget);
                        }
                        return; // Merged into existing stack, no new entity
                    }
                    // TryAddStack returned false = stack full + RejectNew policy
                    return;
                }
            }

            var newEffect = GameplayEffectFactory.CreateEffect(World, proposal.RootId, proposal.Source, proposal.Target, tpl.DurationTicks, tpl.LifetimeKind, tpl.PeriodTicks, proposal.TargetContext, tpl.ClockId, tpl.ExpireCondition);
            World.Get<EffectModifiers>(newEffect) = proposal.Modifiers;

            ref var effectState = ref World.Get<GameplayEffect>(newEffect);
            effectState.State = EffectState.Pending;

            World.Add(newEffect, new ExcludeFromChain());

            // Store template ID so EffectApplicationSystem can look up TargetResolver
            World.Add(newEffect, new EffectTemplateRef { TemplateId = proposal.TemplateId });

            // Pre-merge CallerParams with template ConfigParams at creation time,
            // storing the merged EffectConfigParams directly on the entity.
            if (proposal.HasCallerParams)
            {
                var merged = tpl.ConfigParams;
                merged.MergeFrom(in proposal.CallerParams);
                World.Add(newEffect, merged);
            }
            else if (tpl.ConfigParams.Count > 0)
            {
                World.Add(newEffect, tpl.ConfigParams);
            }

            // Attach EffectGrantedTags if template declares tag contributions
            if (tpl.GrantedTags.Count > 0)
            {
                World.Add(newEffect, tpl.GrantedTags);
            }

            // Attach EffectStack if template has stack policy (first application = count 1)
            if (tpl.HasStackPolicy && tpl.LifetimeKind != EffectLifetimeKind.Instant)
            {
                World.Add(newEffect, new EffectStack
                {
                    Count = 1,
                    Limit = tpl.StackLimit,
                    Policy = tpl.StackPolicy,
                    OverflowPolicy = tpl.StackOverflowPolicy,
                });
            }
        }

        /// <summary>
        /// Find an existing active effect on the target with the given template ID.
        /// Returns Entity.Null if not found.
        /// </summary>
        private Entity FindExistingEffectByTemplate(in ActiveEffectContainer container, int templateId)
        {
            for (int i = 0; i < container.Count; i++)
            {
                var entity = container.GetEntity(i);
                if (World.IsAlive(entity) && World.Has<EffectTemplateRef>(entity))
                {
                    if (World.Get<EffectTemplateRef>(entity).TemplateId == templateId)
                        return entity;
                }
            }
            return Entity.Null;
        }

        /// <summary>
        /// Execute OnPropose phase graphs for a proposal.
        /// Called after EffectProposal is created, before ResponseChain window.
        /// </summary>
        private void ExecuteOnProposePhase(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            if (_phaseExecutor == null || _graphApi == null) return;

            SetMergedConfigContext(in tpl, in proposal);
            _phaseExecutor.ExecutePhase(
                World, _graphApi,
                proposal.Source, proposal.Target, proposal.TargetContext,
                default, // targetPos: not needed for Propose
                EffectPhaseId.OnPropose,
                in tpl.PhaseGraphBindings,
                tpl.PresetType,
                proposal.TagId,
                proposal.TemplateId);
            ClearConfigContext();
        }

        /// <summary>
        /// Execute OnCalculate phase graphs for a proposal.
        /// Called after ResponseChain resolves, before applying modifiers.
        /// </summary>
        private void ExecuteOnCalculatePhase(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            if (_phaseExecutor == null || _graphApi == null) return;

            SetMergedConfigContext(in tpl, in proposal);
            _phaseExecutor.ExecutePhase(
                World, _graphApi,
                proposal.Source, proposal.Target, proposal.TargetContext,
                default,
                EffectPhaseId.OnCalculate,
                in tpl.PhaseGraphBindings,
                tpl.PresetType,
                proposal.TagId,
                proposal.TemplateId);
            ClearConfigContext();
        }

        private EffectConfigParams _mergedConfigTemp; // reusable to avoid repeated stack allocation

        private void SetConfigContext(in EffectTemplateData tpl)
        {
            if (_graphApiHost != null && tpl.ConfigParams.Count > 0)
            {
                _graphApiHost.SetConfigContext(in tpl.ConfigParams);
            }
        }

        /// <summary>
        /// Set merged config context: template params + proposal-level CallerParams.
        /// </summary>
        private void SetMergedConfigContext(in EffectTemplateData tpl, in EffectProposal proposal)
        {
            if (_graphApiHost == null) return;

            if (proposal.HasCallerParams)
            {
                _mergedConfigTemp = tpl.ConfigParams;
                _mergedConfigTemp.MergeFrom(in proposal.CallerParams);
                if (_mergedConfigTemp.Count > 0)
                    _graphApiHost.SetConfigContext(in _mergedConfigTemp);
            }
            else if (tpl.ConfigParams.Count > 0)
            {
                _graphApiHost.SetConfigContext(in tpl.ConfigParams);
            }
        }

        private void ClearConfigContext()
        {
            _graphApiHost?.ClearConfigContext();
        }

        private static unsafe void ApplyModify(ref EffectModifiers modifiers, float modifyValue, ModifierOp op)
        {
            fixed (float* valuesPtr = modifiers.Values)
            {
                for (int j = 0; j < modifiers.Count; j++)
                {
                    float current = valuesPtr[j];
                    valuesPtr[j] = op switch
                    {
                        ModifierOp.Add => current + modifyValue,
                        ModifierOp.Multiply => current * modifyValue,
                        ModifierOp.Override => modifyValue,
                        _ => current
                    };
                }
            }
        }

    }
}
