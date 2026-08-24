using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Response-chain order type configuration loaded from GameConfig.Constants.ResponseChainOrderTypeIds
    /// </summary>
    public struct ResponseChainOrderTypes
    {
        public int ChainPass;
        public int ChainNegate;
        public int ChainActivateEffect;

        public static ResponseChainOrderTypes RequireConfigured(ResponseChainOrderTypes? value, string consumerName)
        {
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_GAS_RESPONSE_CHAIN_ORDER_TYPES_REQUIRED: {consumerName} requires response-chain order type ids injected from GameConfig.Constants.ResponseChainOrderTypeIds.");
            }

            var configured = value.Value;
            if (configured.ChainPass <= 0 ||
                configured.ChainNegate <= 0 ||
                configured.ChainActivateEffect <= 0)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_GAS_RESPONSE_CHAIN_ORDER_TYPES_INVALID: {consumerName} requires positive response-chain order type ids for chainPass, chainNegate, and chainActivateEffect.");
            }

            return configured;
        }
    }

    public sealed class EffectProposalProcessingSystem : BaseSystem<World, float>, ITimeSlicedSystem
    {
        public const string WindowDepthExceededError = "GAS.RESPONSE_CHAIN.ERR.WindowDepthExceeded";
        public const string CreateCapacityExceededError = "GAS.RESPONSE_CHAIN.ERR.CreateCapacityExceeded";
        public const string ResponseQueueOverflowError = "GAS.RESPONSE_CHAIN.ERR.ResponseQueueOverflow";
        public const string InputRequestQueueMissingError = "GAS.RESPONSE_CHAIN.ERR.InputRequestQueueMissing";
        public const string InputRequestQueueFullError = "GAS.RESPONSE_CHAIN.ERR.InputRequestQueueFull";
        public const string InputRequestTagMissingError = "GAS.RESPONSE_CHAIN.ERR.InputRequestTagMissing";
        public const string OrderRequestQueueFullError = "GAS.RESPONSE_CHAIN.ERR.OrderRequestQueueFull";

        private readonly EffectRequestQueue _queue = null!;
        private readonly GasBudget? _budget;
        private readonly EffectTemplateRegistry? _templates;
        private readonly InputRequestQueue? _inputRequests;
        private readonly OrderQueue? _chainOrders;
        private readonly ResponseChainTelemetryBuffer? _telemetry;
        private readonly OrderRequestQueue? _orderRequests;
        private readonly ResponseChainOrderTypes _responseChainOrderTypes;
        private readonly GasPresentationEventBuffer? _presentationEvents;
        private readonly TagOps? _tagOps;

        // Phase Graph execution (optional)
        private readonly EffectPhaseExecutor? _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi? _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi? _graphApiHost;
        private readonly BuiltinHandlerExecutionContext _builtinRuntime = new();
        private readonly EffectPhaseSideEffectTransaction _instantPhaseTransaction;
        private readonly RootBudgetTable _fanOutBudget;
        // An injected budget is advanced by the effect-loop owner once per processing transaction.
        private readonly bool _ownsFanOutBudget;
        private readonly FanOutCommandBuffer _instantFanOutCommands;
        private readonly Entity[] _instantResolverBuffer = new Entity[256];
        private readonly OrderTypeRegistry? _builtinOrderTypeRegistry;
        private readonly OrderRuleRegistry? _builtinOrderRuleRegistry;
        private readonly Ludots.Core.Engine.IClock _clock;
        private readonly int _builtinStepRateHz;

        private static readonly QueryDescription _listenersQuery = new QueryDescription().WithAll<ResponseChainListener>();

        private readonly List<Entity> _listeners = new(1024);
        private readonly ProposalWindow _window = new();
        private readonly ProposalResponseQueue _responseQueue = new();

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public int LastSliceProcessed { get; private set; }
        public int ListenerCacheRebuildCount { get; private set; }
        public byte DebugWindowPhase => (byte)_phase;

        private bool _sliceActive;
        private int _rootCursor;
        private int _rootCountSnapshot;
        private int _lastListenerRevision = -1;

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
        private bool _emitTelemetry;

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

            public void RemoveLast()
            {
                if (_count <= 0)
                {
                    throw new InvalidOperationException("GAS.RESPONSE_CHAIN.ERR.WindowRemoveLastEmpty");
                }

                _count--;
                _items[_count] = default;
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
            public int Count => _count;
            public int AvailableCapacity => _nodes.Length - _count;

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

            public void RemoveByProposalIndex(int proposalIndex)
            {
                int write = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (_nodes[i].Item.ProposalIndex == proposalIndex)
                    {
                        continue;
                    }

                    _nodes[write++] = _nodes[i];
                }

                for (int i = write; i < _count; i++)
                {
                    _nodes[i] = default;
                }

                _count = write;
                for (int i = (_count >> 1) - 1; i >= 0; i--)
                {
                    HeapifyDown(i);
                }
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

        public EffectProposalProcessingSystem(World world, EffectRequestQueue queue, int fanOutCommandCapacity, Ludots.Core.Engine.IClock clock, GasBudget? budget = null, EffectTemplateRegistry? templates = null, InputRequestQueue? inputRequests = null, OrderQueue? chainOrders = null, ResponseChainTelemetryBuffer? telemetry = null, OrderRequestQueue? orderRequests = null, ResponseChainOrderTypes? responseChainOrderTypes = null, GasPresentationEventBuffer? presentationEvents = null, EffectPhaseExecutor? phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi? graphApi = null, TagOps? tagOps = null, ISpatialQueryService? spatialQueries = null, RuntimeEntitySpawnQueue? spawnRequests = null, RuntimeEntityLifecycleQueue? lifecycleRequests = null, EntityLifecycleRuntimeServices? lifecycleServices = null, ExchangeRuntime? exchangeRuntime = null, ProgressionRequirementEvaluator? progressionEvaluator = null, OrderTypeRegistry? orderTypeRegistry = null, OrderRuleRegistry? orderRuleRegistry = null, int stepRateHz = 30, RelationshipRuntime? relationshipRuntime = null, KnowledgeAreaRevealRuntime? knowledgeAreaRevealRuntime = null, OrderQueue? orderIntake = null, RootBudgetTable? fanOutBudget = null, Ludots.Core.Movement.PoseAuthorityArbiter? poseAuthorityArbiter = null)
            : base(world)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _fanOutBudget = fanOutBudget ?? new RootBudgetTable(fanOutCommandCapacity);
            _ownsFanOutBudget = fanOutBudget == null;
            _instantFanOutCommands = new FanOutCommandBuffer(fanOutCommandCapacity);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _queue.TrackResponseChainListenerLifecycle(world);
            _budget = budget;
            _templates = templates;
            _inputRequests = inputRequests;
            _chainOrders = chainOrders;
            _telemetry = telemetry;
            _orderRequests = orderRequests;
            _responseChainOrderTypes = ResponseChainOrderTypes.RequireConfigured(
                responseChainOrderTypes,
                nameof(EffectProposalProcessingSystem));
            _presentationEvents = presentationEvents;
            _tagOps = tagOps;
            _phaseExecutor = phaseExecutor;
            _graphApi = graphApi;
            _graphApiHost = graphApi;
            _instantPhaseTransaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps,
                queue,
                spawnRequests,
                presentationEvents,
                Math.Max(1, fanOutCommandCapacity),
                _fanOutBudget,
                poseAuthorityArbiter);
            _builtinRuntime.SpatialQueries = spatialQueries;
            _builtinRuntime.FanOutBudget = _fanOutBudget;
            _builtinRuntime.FanOutCommands = _instantFanOutCommands;
            _builtinRuntime.ResolverBuffer = _instantResolverBuffer;
            _builtinRuntime.SpawnRequests = spawnRequests;
            _builtinRuntime.LifecycleRequests = lifecycleRequests;
            _builtinRuntime.LifecycleServices = lifecycleServices;
            _builtinRuntime.Exchange = exchangeRuntime;
            _builtinRuntime.ProgressionEvaluator = progressionEvaluator;
            _builtinRuntime.Relationships = relationshipRuntime;
            _builtinRuntime.KnowledgeAreaReveal = knowledgeAreaRevealRuntime;
            _builtinRuntime.TagOps = _tagOps;
            _builtinRuntime.OrderIntake = orderIntake;
            _builtinRuntime.PoseAuthorityArbiter = poseAuthorityArbiter;
            _builtinOrderTypeRegistry = orderTypeRegistry;
            _builtinOrderRuleRegistry = orderRuleRegistry;
            _builtinStepRateHz = GasStepRate.RequirePositive(stepRateHz, nameof(EffectProposalProcessingSystem));
        }

        public override void Update(in float dt)
        {
            int prev = MaxWorkUnitsPerSlice;
            MaxWorkUnitsPerSlice = int.MaxValue;
            while (!UpdateSlice(dt, int.MaxValue)) { }
            MaxWorkUnitsPerSlice = prev;
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            _templates?.RequireFinalized();
            LastSliceProcessed = 0;
            if (_queue == null || _queue.Count == 0)
            {
                _sliceActive = false;
                return true;
            }

            if (!_sliceActive)
            {
                _sliceActive = true;
                if (_ownsFanOutBudget)
                {
                    _fanOutBudget.NextFrame();
                }
                _instantFanOutCommands.Clear();
                _builtinRuntime.OrderTypeRegistry = _builtinOrderTypeRegistry;
                _builtinRuntime.OrderRuleRegistry = _builtinOrderRuleRegistry;
                _builtinRuntime.StepRateHz = _builtinStepRateHz;
                _builtinRuntime.CurrentStep = _clock.Now(Ludots.Core.Engine.ClockDomainId.Step);
                _rootCursor = 0;
                _rootCountSnapshot = _queue.Count;
                _phase = WindowPhase.None;

                int listenerRevision = _queue.ResponseChainListenerRevision;
                if (_lastListenerRevision != listenerRevision)
                {
                    _lastListenerRevision = listenerRevision;
                    _listeners.Clear();
                    var job = new CollectListenerEntitiesJob { Entities = _listeners };
                    World.InlineEntityQuery<CollectListenerEntitiesJob, ResponseChainListener>(in _listenersQuery, ref job);
                    if (_listeners.Count > 1) _listeners.Sort(EntityStableComparer.Instance);
                    ListenerCacheRebuildCount++;
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
                        ConsumeWork(ref workUnits);
                        continue;
                    }

                    if (_templates == null || req.TemplateId <= 0 || !_templates.TryGetRef(req.TemplateId, out int rootTplIdx))
                    {
                        ConsumeWork(ref workUnits);
                        continue;
                    }
                    ref readonly var rootTpl = ref _templates.GetRef(rootTplIdx);

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
                    _emitTelemetry = rootTpl.ParticipatesInResponse;

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
                        ClockId = req.ClockId,
                        HasClockId = req.HasClockId,
                        ParticipatesInResponse = rootTpl.ParticipatesInResponse,
                        Cancelled = false,
                        Modifiers = rootModifiers,
                        CallerParams = req.CallerParams,
                        HasCallerParams = req.HasCallerParams,
                    };
                    if (!_window.TryAdd(root))
                    {
                        ThrowWindowDepthExceeded(req.RootId, req.TemplateId, "Root");
                    }

                    if (_telemetry != null && _emitTelemetry)
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

                    // Execute OnPropose Phase Graphs (before ResponseChain)
                    if (!ExecuteOnProposePhase(in root, in rootTpl))
                    {
                        root.Cancelled = true;
                        _window[_window.Count - 1] = root;
                    }

                    if (rootTpl.ParticipatesInResponse)
                    {
                        EnqueueResponsesForEffect(proposalIndex: 0, effectTagId: rootTpl.TagId);
                        if (_budget != null) _budget.ResponseWindows++;
                    }

                    ConsumeWork(ref workUnits);
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
                                            ThrowCreateCapacityExceeded(_activeReq.RootId, response.EffectTemplateId, "Collect");
                                        }
                                        if (_templates == null || response.EffectTemplateId <= 0 || !_templates.TryGetRef(response.EffectTemplateId, out int tplIdx))
                                        {
                                            break;
                                        }
                                        ref readonly var tpl = ref _templates.GetRef(tplIdx);

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
                                            ThrowWindowDepthExceeded(_activeReq.RootId, response.EffectTemplateId, "Collect");
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

                        ConsumeWork(ref workUnits);
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
                    if (!_inputRequestSent)
                    {
                        if (_inputRequestTagId <= 0)
                        {
                            throw new InvalidOperationException(
                                $"{InputRequestTagMissingError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}.");
                        }
                        if (_window.Count <= 0)
                        {
                            ThrowWindowDepthExceeded(_activeReq.RootId, _activeReq.TemplateId, "WaitInput");
                        }
                        if (_inputRequests == null)
                        {
                            throw new InvalidOperationException(
                                $"{InputRequestQueueMissingError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, requestTagId={_inputRequestTagId}.");
                        }

                        // Prompt + optional OrderRequest are one visible transaction: preflight both
                        // capacities before publishing either, so a full OrderRequest queue cannot
                        // leave an orphan prompt the player cannot answer.
                        if (_inputRequests.Count >= _inputRequests.Capacity)
                        {
                            throw new InvalidOperationException(
                                $"{InputRequestQueueFullError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, requestTagId={_inputRequestTagId}, capacity={_inputRequests.Capacity}.");
                        }

                        var src = _window[0].Source;
                        OrderRequest orderRequest = default;
                        if (_orderRequests != null)
                        {
                            if (_orderRequests.Count >= _orderRequests.Capacity)
                            {
                                throw new InvalidOperationException(
                                    $"{OrderRequestQueueFullError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, requestTagId={_inputRequestTagId}, capacity={_orderRequests.Capacity}.");
                            }

                            if (!World.IsAlive(src) || !World.Has<PlayerOwner>(src))
                            {
                                throw new InvalidOperationException(
                                    $"Response-chain order request requires a live source with PlayerOwner: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}.");
                            }

                            int playerId = World.Get<PlayerOwner>(src).PlayerId;
                            if (playerId <= 0)
                            {
                                throw new InvalidOperationException(
                                    $"Response-chain order request requires a positive PlayerOwner.PlayerId: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, playerId={playerId}.");
                            }

                            orderRequest = new OrderRequest
                            {
                                RequestId = _activeReq.RootId,
                                PromptTagId = _inputRequestTagId,
                                PlayerId = playerId,
                                Actor = src,
                                Target = _window[0].Target,
                                TargetContext = _window[0].TargetContext,
                                AllowedCount = 0
                            };
                            orderRequest.AddAllowed(_responseChainOrderTypes.ChainPass);
                            orderRequest.AddAllowed(_responseChainOrderTypes.ChainNegate);
                            if (_inputRequestTagId > 0) orderRequest.AddAllowed(_responseChainOrderTypes.ChainActivateEffect);
                        }

                        var windowId = _nextWindowId++;
                        var inputRequest = new InputRequest
                        {
                            RequestId = windowId,
                            RequestTagId = _inputRequestTagId,
                            Source = _window[0].Source,
                            Target = _window[0].Target,
                            Context = _window[0].TargetContext,
                            PayloadA = 0,
                            PayloadB = 0
                        };
                        if (!_inputRequests.TryEnqueue(in inputRequest))
                        {
                            throw new InvalidOperationException(
                                $"{InputRequestQueueFullError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, requestTagId={_inputRequestTagId}, capacity={_inputRequests.Capacity}.");
                        }

                        if (_orderRequests != null && !_orderRequests.TryEnqueue(in orderRequest))
                        {
                            throw new InvalidOperationException(
                                $"{OrderRequestQueueFullError}: rootId={_activeReq.RootId}, templateId={_activeReq.TemplateId}, requestTagId={_inputRequestTagId}, capacity={_orderRequests.Capacity}.");
                        }

                        _inputRequestSent = true;

                        if (_telemetry != null && _emitTelemetry)
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
                    }

                    bool progressed = false;
                    if (_chainOrders != null)
                    {
                        while (_chainOrders.TryPeek(out var nextOrder))
                        {
                            if (workUnits >= MaxWorkUnitsPerSlice) return false;

                            if (nextOrder.OrderTypeId == _responseChainOrderTypes.ChainActivateEffect &&
                                nextOrder.Args.I0 > 0 &&
                                _creates >= GasConstants.MAX_CREATES_PER_ROOT)
                            {
                                ThrowCreateCapacityExceeded(_activeReq.RootId, nextOrder.Args.I0, "WaitInput");
                            }

                            if (!TryBeginResponseChainOrderConsumption(
                                    out var order,
                                    out OrderAdmissionReservation admissionReservation,
                                    out bool rejectedForAdmissionCapacity))
                            {
                                break;
                            }

                            progressed = true;
                            if (rejectedForAdmissionCapacity)
                            {
                                OrderSpatialPayloadOps.Release(World, in order);
                                ConsumeWork(ref workUnits);
                                continue;
                            }

                            bool admissionCommitted = false;
                            try
                            {
                                if (order.OrderTypeId == _responseChainOrderTypes.ChainPass)
                                {
                                    if (_telemetry != null && _emitTelemetry)
                                    {
                                        _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                        {
                                            Kind = ResponseChainTelemetryKind.OrderConsumed,
                                            RootId = _activeReq.RootId,
                                            TemplateId = _activeReq.TemplateId,
                                            TagId = _window[0].TagId,
                                            ProposalIndex = 0,
                                            OrderTypeId = order.OrderTypeId,
                                            Source = order.Actor,
                                            Target = order.Target,
                                            Context = order.TargetContext
                                        });
                                    }

                                    CompleteConsumedResponseChainOrder(
                                        in admissionReservation,
                                        in order,
                                        OrderSubmitResult.Activated,
                                        ref admissionCommitted);
                                    _passStreak++;
                                    if (_passStreak >= 2)
                                    {
                                        _closeRequested = true;
                                        break;
                                    }

                                    ConsumeWork(ref workUnits);
                                    continue;
                                }

                                _passStreak = 0;
                                if (order.OrderTypeId == _responseChainOrderTypes.ChainNegate)
                                {
                                    if (_telemetry != null && _emitTelemetry)
                                    {
                                        _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                        {
                                            Kind = ResponseChainTelemetryKind.OrderConsumed,
                                            RootId = _activeReq.RootId,
                                            TemplateId = _activeReq.TemplateId,
                                            TagId = _window[0].TagId,
                                            ProposalIndex = 0,
                                            OrderTypeId = order.OrderTypeId,
                                            Source = order.Actor,
                                            Target = order.Target,
                                            Context = order.TargetContext
                                        });
                                    }

                                    CompleteConsumedResponseChainOrder(
                                        in admissionReservation,
                                        in order,
                                        OrderSubmitResult.Activated,
                                        ref admissionCommitted);
                                    _pendingNegates++;
                                    ConsumeWork(ref workUnits);
                                    continue;
                                }

                                if (order.OrderTypeId == _responseChainOrderTypes.ChainActivateEffect && order.Args.I0 > 0)
                                {
                                    if (_telemetry != null && _emitTelemetry)
                                    {
                                        _telemetry.TryAdd(new ResponseChainTelemetryEvent
                                        {
                                            Kind = ResponseChainTelemetryKind.OrderConsumed,
                                            RootId = _activeReq.RootId,
                                            TemplateId = order.Args.I0,
                                            TagId = _window[0].TagId,
                                            ProposalIndex = 0,
                                            OrderTypeId = order.OrderTypeId,
                                            Source = order.Actor,
                                            Target = order.Target,
                                            Context = order.TargetContext
                                        });
                                    }

                                    if (_templates == null || !_templates.TryGetRef(order.Args.I0, out int tplIdx))
                                    {
                                        CompleteConsumedResponseChainOrder(
                                            in admissionReservation,
                                            in order,
                                            OrderSubmitResult.RejectedValidation,
                                            ref admissionCommitted);
                                        ConsumeWork(ref workUnits);
                                        continue;
                                    }

                                    ref readonly var tpl = ref _templates.GetRef(tplIdx);

                                    if (tpl.ParticipatesInResponse &&
                                        CountResponsesForEffect(tpl.TagId) > _responseQueue.AvailableCapacity)
                                    {
                                        if (_budget != null) _budget.ResponseQueueOverflowDropped++;
                                        CompleteConsumedResponseChainOrder(
                                            in admissionReservation,
                                            in order,
                                            OrderSubmitResult.RejectedQueueFull,
                                            ref admissionCommitted);
                                        ConsumeWork(ref workUnits);
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
                                        CompleteConsumedResponseChainOrder(
                                            in admissionReservation,
                                            in order,
                                            OrderSubmitResult.RejectedQueueFull,
                                            ref admissionCommitted);
                                        ConsumeWork(ref workUnits);
                                        continue;
                                    }

                                    try
                                    {
                                        _creates++;
                                        if (_budget != null) _budget.ResponseCreates++;
                                        if (_telemetry != null && _emitTelemetry)
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
                                    }
                                    catch (InvalidOperationException ex)
                                        when (ex.Message.StartsWith(ResponseQueueOverflowError, StringComparison.Ordinal) ||
                                              ex.Message.StartsWith(WindowDepthExceededError, StringComparison.Ordinal) ||
                                              ex.Message.StartsWith(CreateCapacityExceededError, StringComparison.Ordinal))
                                    {
                                        _responseQueue.RemoveByProposalIndex(newIndex);
                                        _window.RemoveLast();
                                        if (_creates > 0)
                                        {
                                            _creates--;
                                            if (_budget != null && _budget.ResponseCreates > 0)
                                            {
                                                _budget.ResponseCreates--;
                                            }
                                        }

                                        CompleteConsumedResponseChainOrder(
                                            in admissionReservation,
                                            in order,
                                            OrderSubmitResult.RejectedQueueFull,
                                            ref admissionCommitted);
                                        ConsumeWork(ref workUnits);
                                        continue;
                                    }
                                    catch
                                    {
                                        _responseQueue.RemoveByProposalIndex(newIndex);
                                        _window.RemoveLast();
                                        if (_creates > 0)
                                        {
                                            _creates--;
                                            if (_budget != null && _budget.ResponseCreates > 0)
                                            {
                                                _budget.ResponseCreates--;
                                            }
                                        }

                                        throw;
                                    }

                                    CompleteConsumedResponseChainOrder(
                                        in admissionReservation,
                                        in order,
                                        OrderSubmitResult.Activated,
                                        ref admissionCommitted);
                                    _phase = WindowPhase.Collect;
                                    ConsumeWork(ref workUnits);
                                    goto ContinueOuter;
                                }

                                CompleteConsumedResponseChainOrder(
                                    in admissionReservation,
                                    in order,
                                    OrderSubmitResult.RejectedInvalidOrderType,
                                    ref admissionCommitted);
                                ConsumeWork(ref workUnits);
                            }
                            finally
                            {
                                if (!admissionCommitted && admissionReservation.IsValid)
                                {
                                    CompleteConsumedResponseChainOrder(
                                        in admissionReservation,
                                        in order,
                                        OrderSubmitResult.RejectedValidation,
                                        ref admissionCommitted);
                                }
                            }
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

                    ConsumeWork(ref workUnits);
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
                            if (_telemetry != null && _emitTelemetry)
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
                            ConsumeWork(ref workUnits);
                            continue;
                        }

                        if (i > 0 && _resolveNegatesRemaining > 0)
                        {
                            _resolveNegatesRemaining--;
                            if (_telemetry != null && _emitTelemetry)
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
                            ConsumeWork(ref workUnits);
                            continue;
                        }

                        if (!World.IsAlive(e.Target))
                        {
                            if (_telemetry != null && _emitTelemetry)
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
                            ConsumeWork(ref workUnits);
                            continue;
                        }

                        if (_templates == null || e.TemplateId <= 0 || !_templates.TryGetRef(e.TemplateId, out int tplIdx))
                        {
                            if (_telemetry != null && _emitTelemetry)
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
                            ConsumeWork(ref workUnits);
                            continue;
                        }
                        ref readonly var tpl = ref _templates.GetRef(tplIdx);

                        // Execute OnCalculate Phase Graphs (after ResponseChain resolves)
                        ExecuteOnCalculatePhase(in e, in tpl);

                        if (CanExecuteInstantInline(in tpl))
                        {
                            ExecuteInstantInline(in e, in tpl);

                            if (_telemetry != null && _emitTelemetry)
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
                            if (_telemetry != null && _emitTelemetry)
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

                        ConsumeWork(ref workUnits);
                    }

                    if (workUnits >= MaxWorkUnitsPerSlice) return false;

                    if (_telemetry != null && _emitTelemetry)
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
                    ConsumeWork(ref workUnits);
                    continue;
                }
            }
        }

        private void ConsumeWork(ref int workUnits)
        {
            workUnits++;
            LastSliceProcessed++;
        }

        private bool TryBeginResponseChainOrderConsumption(
            out Order order,
            out OrderAdmissionReservation reservation,
            out bool rejectedForAdmissionCapacity)
        {
            order = default;
            reservation = default;
            rejectedForAdmissionCapacity = false;
            if (_chainOrders == null || !_chainOrders.TryPeek(out order))
            {
                return false;
            }

            OrderAdmissionResultBuffer admissionResults = _chainOrders.AdmissionResults;
            if (!admissionResults.LogicStepActive || !admissionResults.EntityIntakeOpen)
            {
                return false;
            }

            if (!admissionResults.CanReserve(OrderAdmissionStage.EntityIntake, 1))
            {
                if (!admissionResults.CanRecordCapacityFailures(OrderAdmissionStage.EntityIntake, 1))
                {
                    throw new InvalidOperationException(
                        $"{OrderAdmissionResultBuffer.RejectionCapacityExceededError}: stage={OrderAdmissionStage.EntityIntake}, batchCount=1, rejectionCapacity={admissionResults.RejectionCapacity}.");
                }

                if (!_chainOrders.TryDequeue(out order))
                {
                    throw new InvalidOperationException("GAS.RESPONSE_CHAIN.ERR.QueuedOrderDisappearedDuringAdmissionCapacityRejection");
                }

                Span<Order> rejected = stackalloc Order[1];
                rejected[0] = order;
                admissionResults.RecordCapacityFailures(rejected, OrderAdmissionStage.EntityIntake);
                rejectedForAdmissionCapacity = true;
                return true;
            }

            reservation = admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                order.OrderId,
                order.OrderTypeId);
            bool dequeued = false;
            try
            {
                if (!_chainOrders.TryDequeue(out Order dequeuedOrder))
                {
                    throw new InvalidOperationException("GAS.RESPONSE_CHAIN.ERR.QueuedOrderDisappearedDuringAdmission");
                }

                order = dequeuedOrder;
                dequeued = true;
                return true;
            }
            finally
            {
                if (!dequeued && reservation.IsValid)
                {
                    admissionResults.Cancel(in reservation);
                }
            }
        }

        private void CommitResponseChainOrderAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            OrderQueue chainOrders = _chainOrders
                ?? throw new InvalidOperationException("GAS.RESPONSE_CHAIN.ERR.ChainOrderQueueMissing");
            var outcome = new OrderAdmissionOutcome(
                order.OrderId,
                order.OrderTypeId,
                OrderAdmissionStage.EntityIntake,
                result);
            chainOrders.AdmissionResults.Commit(in reservation, in outcome);
        }

        private void CompleteConsumedResponseChainOrder(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result,
            ref bool admissionCommitted)
        {
            // Commit first and mark ownership closed before Release. If Release throws
            // (e.g. missing payload buffer), finally must not Commit the same reservation again.
            CommitResponseChainOrderAdmission(in reservation, in order, result);
            admissionCommitted = true;
            OrderSpatialPayloadOps.Release(World, in order);
        }

        private unsafe int CountResponsesForEffect(int effectTagId)
        {
            int matched = 0;
            for (int li = 0; li < _listeners.Count; li++)
            {
                var listenerEntity = _listeners[li];
                if (!World.IsAlive(listenerEntity))
                {
                    continue;
                }

                ref var listener = ref World.TryGetRef<ResponseChainListener>(listenerEntity, out bool hasListener);
                if (!hasListener)
                {
                    continue;
                }

                for (int i = 0; i < listener.Count; i++)
                {
                    int eventTagId = listener.EventTagIds[i];
                    if (eventTagId != 0 && effectTagId != eventTagId)
                    {
                        continue;
                    }

                    matched++;
                }
            }

            return matched;
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
                // Current root hasn't been resolved yet; safe to re-process it.
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
            _emitTelemetry = false;
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
                        throw new InvalidOperationException(
                            $"{ResponseQueueOverflowError}: proposalIndex={proposalIndex}, effectTagId={effectTagId}, responseType={responseType}, capacity={GasConstants.MAX_RESPONSES_PER_WINDOW}.");
                    }
                }
            }
        }

        private void ThrowWindowDepthExceeded(int rootId, int templateId, string phase)
        {
            if (_budget != null) _budget.ResponseDepthDropped++;
            throw new InvalidOperationException(
                $"{WindowDepthExceededError}: rootId={rootId}, templateId={templateId}, phase={phase}, capacity={GasConstants.MAX_DEPTH}.");
        }

        private void ThrowCreateCapacityExceeded(int rootId, int templateId, string phase)
        {
            if (_budget != null) _budget.ResponseCreatesDropped++;
            throw new InvalidOperationException(
                $"{CreateCapacityExceededError}: rootId={rootId}, templateId={templateId}, phase={phase}, capacity={GasConstants.MAX_CREATES_PER_ROOT}.");
        }

        private static void ApplyPresetModifiers(ref EffectModifiers modifiers, in EffectTemplateData tpl, in EffectRequest req)
        {
            switch (tpl.PresetType)
            {
                case EffectPresetType.None:
                    return;
                case EffectPresetType.ApplyForce2D:
                    {
                        EffectConfigParams mergedParams = ConfigParamsMerger.BuildMergedConfig(in tpl.ConfigParams, in req);
                        mergedParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx);
                        mergedParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy);
                        modifiers.Add(tpl.PresetAttribute0, ModifierOp.Add, fx);
                        modifiers.Add(tpl.PresetAttribute1, ModifierOp.Add, fy);
                        return;
                    }
            }
        }

        private void ExecuteInstantInline(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            bool hasPhaseRuntime = _phaseExecutor != null && _graphApi != null;
            ref readonly EffectExecutionPlanSet plans = ref _templates!.RequireExecutionPlans(proposal.TemplateId);
            EffectWindowExecutionPlan activationPlan = plans.Activation;
            if (activationPlan.Kind == EffectExecutionPlanKind.ExternalAtomicExclusive)
            {
                if (!hasPhaseRuntime)
                {
                    throw new InvalidOperationException(
                        $"GAS.INSTANT.ERR.MissingPhaseRuntime: templateId={proposal.TemplateId}, plan={activationPlan.Kind}.");
                }
                if (activationPlan.RequiresListenerPreflight &&
                    (HasMatchingActivationListener(in proposal, in tpl, EffectPhaseId.OnResolve) ||
                     HasMatchingActivationListener(in proposal, in tpl, EffectPhaseId.OnHit) ||
                     HasMatchingActivationListener(in proposal, in tpl, EffectPhaseId.OnApply)))
                {
                    throw new InvalidOperationException(
                        $"{EffectPhaseExecutor.ExternalAtomicListenerConflictError}: templateId={proposal.TemplateId}, domain={activationPlan.Domain}.");
                }
            }
            else if (activationPlan.Kind != EffectExecutionPlanKind.GasTransactional)
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_PLAN.ERR.InvalidRuntimePlan: templateId={proposal.TemplateId}, plan={activationPlan.Kind}.");
            }

            _builtinRuntime.ResetPerEffect();
            _builtinRuntime.SetModifierOverride(in proposal.Modifiers);
            bool useGasTransaction = activationPlan.Kind == EffectExecutionPlanKind.GasTransactional;
            if (useGasTransaction)
            {
                _instantPhaseTransaction.Begin();
                _builtinRuntime.EffectSideEffects = _instantPhaseTransaction;
            }
            bool graphTransactionBound = false;

            try
            {
                if (useGasTransaction && _graphApiHost != null)
                {
                    _graphApiHost.BeginEffectSideEffectTransaction(_instantPhaseTransaction);
                    graphTransactionBound = true;
                }

                if (!hasPhaseRuntime)
                {
                    if (tpl.PhaseGraphBindings.StepCount > 0 || tpl.HasTargetResolver ||
                        (tpl.PresetType != EffectPresetType.None &&
                         tpl.PresetType != EffectPresetType.InstantDamage &&
                         tpl.PresetType != EffectPresetType.Heal &&
                         tpl.PresetType != EffectPresetType.ApplyForce2D))
                    {
                        throw new InvalidOperationException(
                            $"GAS.INSTANT.ERR.MissingPhaseRuntime: templateId={proposal.TemplateId}, preset={tpl.PresetType}.");
                    }

                    ApplyInstantModifiersAndPublish(in proposal);
                    if (useGasTransaction)
                    {
                        _instantPhaseTransaction.Commit();
                    }
                    return;
                }

                EffectConfigParams mergedConfig = BuildMergedConfig(in tpl, in proposal);
                EffectPhaseExecutor phaseExecutor = _phaseExecutor
                    ?? throw new InvalidOperationException(
                        $"GAS.INSTANT.ERR.MissingPhaseRuntime: templateId={proposal.TemplateId}, preset={tpl.PresetType}.");
                Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi graphApi = _graphApi
                    ?? throw new InvalidOperationException(
                        $"GAS.INSTANT.ERR.MissingGraphRuntime: templateId={proposal.TemplateId}, preset={tpl.PresetType}.");
                var context = new EffectContext
                {
                    RootId = proposal.RootId,
                    Source = proposal.Source,
                    Target = proposal.Target,
                    TargetContext = proposal.TargetContext,
                };
                IntVector2 targetPosCm = PlacementPhaseTargetPosResolver.Resolve(World, in context, in mergedConfig);
                phaseExecutor.ExecutePhase(
                    World, graphApi, proposal.Source, proposal.Target, proposal.TargetContext, targetPosCm,
                    EffectPhaseId.OnResolve, in tpl.PhaseGraphBindings, tpl.EffectivePresetTypeId,
                    tpl.TagId, proposal.TemplateId, in mergedConfig, _builtinRuntime, BuildInstantExecutionSeed(in proposal, EffectPhaseId.OnResolve), proposal.RootId);
                phaseExecutor.ExecutePhase(
                    World, graphApi, proposal.Source, proposal.Target, proposal.TargetContext, targetPosCm,
                    EffectPhaseId.OnHit, in tpl.PhaseGraphBindings, tpl.EffectivePresetTypeId,
                    tpl.TagId, proposal.TemplateId, in mergedConfig, _builtinRuntime, BuildInstantExecutionSeed(in proposal, EffectPhaseId.OnHit), proposal.RootId);
                phaseExecutor.ExecutePhase(
                    World, graphApi, proposal.Source, proposal.Target, proposal.TargetContext, targetPosCm,
                    EffectPhaseId.OnApply, in tpl.PhaseGraphBindings, tpl.EffectivePresetTypeId,
                    tpl.TagId, proposal.TemplateId, in mergedConfig, _builtinRuntime, BuildInstantExecutionSeed(in proposal, EffectPhaseId.OnApply), proposal.RootId);

                if (_builtinRuntime.HasAttributeDelta)
                {
                    PublishInstantApplied(
                        in proposal,
                        _builtinRuntime.AttributeDeltaId,
                        _builtinRuntime.AttributeDelta);
                }
                else if (useGasTransaction)
                {
                    ApplyInstantModifiersAndPublish(in proposal);
                }

                PublishBuiltinFanOutCommands();
                if (useGasTransaction)
                {
                    _instantPhaseTransaction.Commit();
                }
            }
            catch
            {
                if (useGasTransaction)
                {
                    _instantPhaseTransaction.Rollback();
                }
                throw;
            }
            finally
            {
                if (graphTransactionBound)
                {
                    _graphApiHost!.EndEffectSideEffectTransaction(_instantPhaseTransaction);
                }
                _builtinRuntime.EffectSideEffects = null;
                _instantFanOutCommands.Clear();
            }
        }

        private bool HasMatchingActivationListener(
            in EffectProposal proposal,
            in EffectTemplateData template,
            EffectPhaseId phase)
        {
            return _phaseExecutor!.HasMatchingListener(
                World,
                proposal.Source,
                proposal.Target,
                phase,
                template.TagId,
                proposal.TemplateId);
        }

        private void ApplyInstantModifiersAndPublish(in EffectProposal proposal)
        {
            if (!World.IsAlive(proposal.Target) || !World.Has<AttributeBuffer>(proposal.Target)) return;

            int primaryAttributeId = proposal.Modifiers.Count > 0
                ? proposal.Modifiers.Get(0).AttributeId
                : -1;
            if (_instantPhaseTransaction.IsActive)
            {
                float stagedBefore = primaryAttributeId >= 0 &&
                    _instantPhaseTransaction.TryReadAttributeCurrent(proposal.Target, primaryAttributeId, out float currentBefore)
                        ? currentBefore
                        : 0f;
                _instantPhaseTransaction.StageModifiers(proposal.Target, in proposal.Modifiers);
                float stagedAfter = primaryAttributeId >= 0 &&
                    _instantPhaseTransaction.TryReadAttributeCurrent(proposal.Target, primaryAttributeId, out float currentAfter)
                        ? currentAfter
                        : 0f;
                PublishInstantApplied(in proposal, primaryAttributeId, stagedAfter - stagedBefore);
                return;
            }

            float before = primaryAttributeId >= 0
                ? World.Get<AttributeBuffer>(proposal.Target).GetCurrent(primaryAttributeId)
                : 0f;
            TagOps tagOps = _tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            AttributeMutationOps.ApplyModifiers(World, proposal.Target, in proposal.Modifiers, tagOps);
            float after = primaryAttributeId >= 0
                ? World.Get<AttributeBuffer>(proposal.Target).GetCurrent(primaryAttributeId)
                : 0f;
            PublishInstantApplied(in proposal, primaryAttributeId, after - before);
        }

        private void PublishInstantApplied(in EffectProposal proposal, int attributeId, float delta)
        {
            if (_presentationEvents == null || attributeId < 0) return;
            var presentationEvent = new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = proposal.Source,
                Target = proposal.Target,
                EffectTemplateId = proposal.TemplateId,
                AttributeId = attributeId,
                Delta = delta,
            };
            if (_instantPhaseTransaction.IsActive)
            {
                _instantPhaseTransaction.StagePresentationEvent(in presentationEvent);
            }
            else
            {
                _presentationEvents.Publish(presentationEvent);
            }
        }

        private static uint BuildInstantExecutionSeed(in EffectProposal proposal, EffectPhaseId phase)
        {
            uint hash = 2166136261u;
            hash = (hash ^ unchecked((uint)proposal.RootId)) * 16777619u;
            hash = (hash ^ unchecked((uint)proposal.Source.Id)) * 16777619u;
            hash = (hash ^ unchecked((uint)proposal.Target.Id)) * 16777619u;
            hash = (hash ^ unchecked((uint)proposal.TemplateId)) * 16777619u;
            hash = (hash ^ (uint)phase) * 16777619u;
            return hash == 0u ? 1u : hash;
        }

        private static bool CanExecuteInstantInline(in EffectTemplateData tpl)
        {
            if (tpl.LifetimeKind != EffectLifetimeKind.Instant) return false;
            if (tpl.PeriodTicks > 0) return false;
            if (tpl.ListenerSetup.Count > 0)
            {
                throw new InvalidOperationException(
                    "GAS.INSTANT.ERR.PersistentListenerRequiresCrossFrameLifetime");
            }
            return true;
        }

        private void CreateEntityEffect(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            EffectConfigParams mergedConfig = BuildMergedConfig(in tpl, in proposal);
            int durationTicks = ConfigParamsMerger.ResolveDurationTicks(in tpl, in mergedConfig);
            int periodTicks = ConfigParamsMerger.ResolvePeriodTicks(in tpl, in mergedConfig);

            // Stack merge: if template has stack policy and an existing effect exists on target, merge.
            if (tpl.HasStackPolicy && tpl.LifetimeKind != EffectLifetimeKind.Instant
                && World.IsAlive(proposal.Target) && World.Has<ActiveEffectContainer>(proposal.Target))
            {
                ref var container = ref World.Get<ActiveEffectContainer>(proposal.Target);
                Entity existing = FindExistingEffectByTemplate(in container, proposal.TemplateId);
                if (existing != Entity.Null && World.IsAlive(existing) && World.Has<EffectStack>(existing))
                {
                    EffectStack stackBefore = World.Get<EffectStack>(existing);
                    EffectStack stackAfter = stackBefore;
                    if (stackAfter.TryAddStack())
                    {
                        // Apply duration policy
                        GameplayEffect effectBefore = World.Get<GameplayEffect>(existing);
                        GameplayEffect effectAfter = effectBefore;
                        switch (tpl.StackPolicy)
                        {
                            case StackPolicy.RefreshDuration:
                                effectAfter.TotalTicks = durationTicks;
                                effectAfter.RemainingTicks = durationTicks;
                                effectAfter.ExpiresAtTick = 0; // Will be recomputed next tick
                                break;
                            case StackPolicy.AddDuration:
                                effectAfter.TotalTicks += durationTicks;
                                effectAfter.RemainingTicks += durationTicks;
                                if (effectAfter.ExpiresAtTick > 0)
                                {
                                    effectAfter.ExpiresAtTick += durationTicks;
                                }
                                else
                                {
                                    effectAfter.ExpiresAtTick = 0;
                                }
                                break;
                                // KeepDuration: do nothing
                        }

                        World.Get<EffectStack>(existing) = stackAfter;
                        World.Get<GameplayEffect>(existing) = effectAfter;

                        // Update tag contributions for the committed stack delta.
                        try
                        {
                            if (World.Has<EffectGrantedTags>(existing))
                            {
                                EffectGrantedTags grantedTags = World.Get<EffectGrantedTags>(existing);
                                TagOps tagOps = _tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
                                EffectTagContributionHelper.UpdateOnEntity(
                                    World,
                                    proposal.Target,
                                    in grantedTags,
                                    stackBefore.Count,
                                    stackAfter.Count,
                                    tagOps,
                                    _budget!);
                            }
                        }
                        catch
                        {
                            World.Get<EffectStack>(existing) = stackBefore;
                            World.Get<GameplayEffect>(existing) = effectBefore;
                            throw;
                        }
                        MarkAggregateDirtyIfNeeded(proposal.Target, existing);
                        return; // Merged into existing stack, no new entity
                    }
                    // TryAddStack returned false = stack full + RejectNew policy
                    return;
                }
            }

            GasClockId clockId = proposal.HasClockId ? proposal.ClockId : tpl.ClockId;
            var newEffect = GameplayEffectFactory.CreateEffect(World, proposal.RootId, proposal.Source, proposal.Target, durationTicks, tpl.LifetimeKind, periodTicks, proposal.TargetContext, clockId, tpl.ExpireCondition);
            World.Get<EffectModifiers>(newEffect) = proposal.Modifiers;

            ref var effectState = ref World.Get<GameplayEffect>(newEffect);
            effectState.State = EffectState.Pending;
            effectState.AggregatesModifiers = tpl.PresetType == EffectPresetType.Buff;

            World.Add(newEffect, new ExcludeFromChain());

            // Store template ID so EffectApplicationSystem can look up TargetResolver
            World.Add(newEffect, new EffectTemplateRef { TemplateId = proposal.TemplateId });

            // Pre-merge CallerParams with template ConfigParams at creation time,
            // storing the merged EffectConfigParams directly on the entity.
            if (mergedConfig.Count > 0)
            {
                World.Add(newEffect, mergedConfig);
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

        private void MarkAggregateDirtyIfNeeded(Entity target, Entity effect)
        {
            if (!World.IsAlive(target) || !World.IsAlive(effect))
            {
                return;
            }

            if (!World.Has<GameplayEffect>(effect) || !World.Get<GameplayEffect>(effect).AggregatesModifiers)
            {
                return;
            }

            if (!World.Has<AttributeAggregateDirty>(target))
            {
                World.Add(target, new AttributeAggregateDirty());
            }
        }

        /// <summary>
        /// Execute OnPropose phase graphs for a proposal.
        /// Called after EffectProposal is created, before ResponseChain window.
        /// Returns false when a validating OnPropose graph leaves B[0]=0
        /// (fail-closed placement/validation rejection). Vacant OnPropose phases pass.
        /// </summary>
        private bool ExecuteOnProposePhase(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            if (_phaseExecutor == null || _graphApi == null) return true;

            var mergedConfig = BuildMergedConfig(in tpl, in proposal);
            var context = new EffectContext
            {
                RootId = proposal.RootId,
                Source = proposal.Source,
                Target = proposal.Target,
                TargetContext = proposal.TargetContext,
            };
            IntVector2 targetPos = PlacementPhaseTargetPosResolver.Resolve(World, in context, in mergedConfig);
            return _phaseExecutor.ExecutePhaseWithValidationResult(
                World, _graphApi,
                proposal.Source, proposal.Target, proposal.TargetContext,
                targetPos,
                EffectPhaseId.OnPropose,
                in tpl.PhaseGraphBindings,
                tpl.PresetTypeId,
                proposal.TagId,
                proposal.TemplateId,
                in mergedConfig,
                rootId: proposal.RootId);
        }

        /// <summary>
        /// Execute OnCalculate phase graphs for a proposal.
        /// Called after ResponseChain resolves, before applying modifiers.
        /// </summary>
        private void ExecuteOnCalculatePhase(in EffectProposal proposal, in EffectTemplateData tpl)
        {
            if (_phaseExecutor == null || _graphApi == null) return;

            var mergedConfig = BuildMergedConfig(in tpl, in proposal);
            var context = new EffectContext
            {
                RootId = proposal.RootId,
                Source = proposal.Source,
                Target = proposal.Target,
                TargetContext = proposal.TargetContext,
            };
            IntVector2 targetPos = PlacementPhaseTargetPosResolver.Resolve(World, in context, in mergedConfig);
            _builtinRuntime.ResetPerEffect();
            _builtinRuntime.SetModifierOverride(in proposal.Modifiers);
            try
            {
                _phaseExecutor.ExecutePhase(
                    World, _graphApi,
                    proposal.Source, proposal.Target, proposal.TargetContext,
                    targetPos,
                    EffectPhaseId.OnCalculate,
                    in tpl.PhaseGraphBindings,
                    tpl.EffectivePresetTypeId,
                    proposal.TagId,
                    proposal.TemplateId,
                    in mergedConfig,
                    _builtinRuntime,
                    BuildInstantExecutionSeed(in proposal, EffectPhaseId.OnCalculate),
                    proposal.RootId);
                PublishBuiltinFanOutCommands();
            }
            finally
            {
                _instantFanOutCommands.Clear();
            }
        }

        private void PublishBuiltinFanOutCommands()
        {
            for (int i = 0; i < _instantFanOutCommands.Count; i++)
            {
                FanOutCommand command = _instantFanOutCommands[i];
                if (_instantPhaseTransaction.IsActive)
                {
                    _instantPhaseTransaction.StageFanOutCommand(in command);
                }
                else
                {
                    TargetResolverFanOutHelper.PublishCommand(in command, _queue);
                }
            }
            _instantFanOutCommands.Clear();
        }

        public override void Dispose()
        {
            _instantPhaseTransaction.Dispose();
            base.Dispose();
        }

        private EffectConfigParams BuildMergedConfig(in EffectTemplateData tpl, in EffectProposal proposal)
        {
            if (proposal.HasCallerParams)
            {
                var merged = tpl.ConfigParams;
                merged.MergeFrom(in proposal.CallerParams);
                return merged;
            }

            return tpl.ConfigParams;
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
