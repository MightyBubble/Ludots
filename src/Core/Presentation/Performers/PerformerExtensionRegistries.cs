using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Modding;

namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerCommandRouteStrategy : byte
    {
        None = 0,
        ExistingInstances = 1,
        ScopedInstance = 2,
        SingleRuntime = 3,
        CreatePerformer = 4,
        DestroyScope = 5,
    }

    public enum PerformerBehaviorExecutionLane : byte
    {
        None = 0,
        Bootstrap = 1,
        ContinuousTick = 2,
        OwnerAttributeDirty = 3,
        OwnerTagDirty = 4,
        ParamDirty = 5,
        Activation = 6,
        Destroy = 7,
    }

    public readonly struct PerformerCommandView
    {
        public PerformerCommandView(in PerformerCommand command)
        {
            CommandKindId = command.CommandKindId;
            RouteStrategy = command.RouteStrategy;
            PerformerEntity = command.PerformerEntity;
            Source = command.Source;
            Target = command.Target;
            Viewer = command.Viewer;
            PerformerDefinitionId = command.PerformerDefinitionId;
            ScopeTag = command.ScopeTag;
            TargetBehaviorSlot = command.TargetBehaviorSlot;
            ParamKey = command.ParamKey;
            ParamLane = command.ParamLane;
            ParamValue = command.ParamValue;
            IntValue = command.IntValue;
            VectorValue = command.VectorValue;
            HasParamPayload = command.HasParamPayload;
        }

        public int CommandKindId { get; }
        public PerformerCommandRouteStrategy RouteStrategy { get; }
        public Entity PerformerEntity { get; }
        public Entity Source { get; }
        public Entity Target { get; }
        public Entity Viewer { get; }
        public int PerformerDefinitionId { get; }
        public int ScopeTag { get; }
        public int TargetBehaviorSlot { get; }
        public int ParamKey { get; }
        public ParamLane ParamLane { get; }
        public float ParamValue { get; }
        public int IntValue { get; }
        public Vector4 VectorValue { get; }
        public bool HasParamPayload { get; }
    }

    public interface IPerformerCommandOps
    {
        bool HasRoutedPerformer { get; }
        void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default);
        void ClearParam(int paramKey, ParamLane lane);
        void ActivateBehavior(int slotIndex);
        void DeactivateBehavior(int slotIndex);
    }

    public readonly struct PerformerCommandExecutionContext
    {
        public PerformerCommandExecutionContext(in PerformerCommand command, IPerformerCommandOps ops)
        {
            Command = new PerformerCommandView(in command);
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
        }

        public PerformerCommandView Command { get; }
        public IPerformerCommandOps Ops { get; }
    }

    public delegate void PerformerCommandHandler(in PerformerCommandExecutionContext context);

    public readonly struct PerformerCommandExtensionDescriptor
    {
        public PerformerCommandExtensionDescriptor(
            PerformerCommandRouteStrategy routeStrategy,
            PerformerCommandHandler handler)
        {
            if (routeStrategy == PerformerCommandRouteStrategy.None)
            {
                throw new ArgumentOutOfRangeException(nameof(routeStrategy), "Extension performer command route must be explicit.");
            }

            RouteStrategy = routeStrategy;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public PerformerCommandRouteStrategy RouteStrategy { get; }
        public PerformerCommandHandler Handler { get; }
    }

    public sealed class PerformerCommandKindRegistry
    {
        public const int FirstModCommandKindId = 1024;
        public const int MaxCommandKinds = 2048;

        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModCommandKindId,
            maxIdExclusive: MaxCommandKinds,
            comparer: StringComparer.Ordinal);
        private readonly PerformerCommandExtensionDescriptor[] _descriptors = new PerformerCommandExtensionDescriptor[MaxCommandKinds];
        private readonly bool[] _registeredHandlers = new bool[MaxCommandKinds];

        public PerformerCommandKindRegistry()
        {
            RegisterBuiltinKeys();
        }

        private void RegisterBuiltinKeys()
        {
            foreach (PerformerCommandKind kind in Enum.GetValues<PerformerCommandKind>())
            {
                if (kind != PerformerCommandKind.None)
                {
                    _keys.RegisterFixed(kind.ToString(), (byte)kind);
                }
            }
        }

        public int Register(string key, PerformerCommandHandler handler)
        {
            throw new InvalidOperationException(
                "Performer command extensions must register a descriptor with an explicit route strategy.");
        }

        public int Register(string key, in PerformerCommandExtensionDescriptor descriptor)
        {
            if (Enum.TryParse(key, ignoreCase: false, out PerformerCommandKind reserved) &&
                reserved != PerformerCommandKind.None &&
                Enum.IsDefined(typeof(PerformerCommandKind), reserved))
            {
                throw new InvalidOperationException(
                    $"Performer command kind '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            int id = _keys.RegisterDynamic(key);
            if (_registeredHandlers[id])
            {
                throw new InvalidOperationException($"Performer command kind '{key}' is already registered.");
            }

            _descriptors[id] = descriptor;
            _registeredHandlers[id] = true;
            return id;
        }

        public bool TryGetId(string key, out int id)
        {
            if (Enum.TryParse(key, ignoreCase: false, out PerformerCommandKind builtin) &&
                builtin != PerformerCommandKind.None &&
                Enum.IsDefined(typeof(PerformerCommandKind), builtin))
            {
                id = (byte)builtin;
                return true;
            }

            return _keys.TryGetId(key, out id);
        }

        public int GetId(string key) => TryGetId(key, out int id) ? id : 0;

        public bool TryGetDescriptor(int id, out PerformerCommandExtensionDescriptor descriptor)
        {
            if ((uint)id < (uint)_descriptors.Length && _registeredHandlers[id])
            {
                descriptor = _descriptors[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        public bool TryGetHandler(int id, out PerformerCommandHandler handler)
        {
            if (TryGetDescriptor(id, out PerformerCommandExtensionDescriptor descriptor))
            {
                handler = descriptor.Handler;
                return true;
            }

            handler = null!;
            return false;
        }

        public void Freeze() => _keys.Freeze();

        public void Clear()
        {
            Array.Clear(_descriptors, 0, _descriptors.Length);
            Array.Clear(_registeredHandlers, 0, _registeredHandlers.Length);
            _keys.Clear();
            RegisterBuiltinKeys();
        }
    }

    public readonly struct PerformerBehaviorView
    {
        public PerformerBehaviorView(
            Entity performer,
            Entity owner,
            int definitionId,
            int slotIndex,
            int kindId,
            PerformerBehaviorExecutionLane lane,
            bool firstFrame,
            float deltaTime)
        {
            Performer = performer;
            Owner = owner;
            DefinitionId = definitionId;
            SlotIndex = slotIndex;
            KindId = kindId;
            Lane = lane;
            FirstFrame = firstFrame;
            DeltaTime = deltaTime;
        }

        public Entity Performer { get; }
        public Entity Owner { get; }
        public int DefinitionId { get; }
        public int SlotIndex { get; }
        public int KindId { get; }
        public PerformerBehaviorExecutionLane Lane { get; }
        public bool FirstFrame { get; }
        public float DeltaTime { get; }
    }

    public interface IPerformerBehaviorOps
    {
        bool TryResolveFloat(int paramKey, out float value);
        bool TryResolveInt(int paramKey, out int value);
        bool TryResolveVector(int paramKey, out Vector4 value);
        void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default);
        void ClearParam(int paramKey, ParamLane lane);
    }

    public readonly struct PerformerBehaviorExecutionContext
    {
        public PerformerBehaviorExecutionContext(in PerformerBehaviorView behavior, IPerformerBehaviorOps ops)
        {
            Behavior = behavior;
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
        }

        public PerformerBehaviorView Behavior { get; }
        public IPerformerBehaviorOps Ops { get; }
    }

    public delegate void PerformerBehaviorHandler(in PerformerBehaviorExecutionContext context);

    public readonly struct PerformerBehaviorExtensionDescriptor
    {
        public PerformerBehaviorExtensionDescriptor(
            PerformerBehaviorExecutionLane lane,
            PerformerBehaviorHandler handler)
        {
            if (lane == PerformerBehaviorExecutionLane.None)
            {
                throw new ArgumentOutOfRangeException(nameof(lane), "Extension performer behavior lane must be explicit.");
            }

            Lane = lane;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public PerformerBehaviorExecutionLane Lane { get; }
        public PerformerBehaviorHandler Handler { get; }
    }

    public sealed class PerformerBehaviorKindRegistry
    {
        public const int FirstModBehaviorKindId = 1024;
        public const int MaxBehaviorKinds = 2048;

        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModBehaviorKindId,
            maxIdExclusive: MaxBehaviorKinds,
            comparer: StringComparer.Ordinal);
        private readonly PerformerBehaviorExtensionDescriptor[] _descriptors = new PerformerBehaviorExtensionDescriptor[MaxBehaviorKinds];
        private readonly bool[] _registeredHandlers = new bool[MaxBehaviorKinds];

        public PerformerBehaviorKindRegistry()
        {
            RegisterBuiltinKeys();
        }

        private void RegisterBuiltinKeys()
        {
            foreach (BehaviorKind kind in Enum.GetValues<BehaviorKind>())
            {
                if (kind != BehaviorKind.None)
                {
                    _keys.RegisterFixed(kind.ToString(), (byte)kind);
                }
            }
        }

        public int Register(string key, PerformerBehaviorHandler handler)
        {
            throw new InvalidOperationException(
                "Performer behavior extensions must register a descriptor with an explicit execution lane.");
        }

        public int Register(string key, in PerformerBehaviorExtensionDescriptor descriptor)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BehaviorKind reserved) &&
                reserved != BehaviorKind.None &&
                Enum.IsDefined(typeof(BehaviorKind), reserved))
            {
                throw new InvalidOperationException(
                    $"Performer behavior kind '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            int id = _keys.RegisterDynamic(key);
            if (_registeredHandlers[id])
            {
                throw new InvalidOperationException($"Performer behavior kind '{key}' is already registered.");
            }

            _descriptors[id] = descriptor;
            _registeredHandlers[id] = true;
            return id;
        }

        public bool TryGetId(string key, out int id)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BehaviorKind builtin) &&
                builtin != BehaviorKind.None &&
                Enum.IsDefined(typeof(BehaviorKind), builtin))
            {
                id = (byte)builtin;
                return true;
            }

            return _keys.TryGetId(key, out id);
        }

        public int GetId(string key) => TryGetId(key, out int id) ? id : 0;

        public bool TryGetDescriptor(int id, out PerformerBehaviorExtensionDescriptor descriptor)
        {
            if ((uint)id < (uint)_descriptors.Length && _registeredHandlers[id])
            {
                descriptor = _descriptors[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        public bool TryGetHandler(int id, out PerformerBehaviorHandler handler)
        {
            if (TryGetDescriptor(id, out PerformerBehaviorExtensionDescriptor descriptor))
            {
                handler = descriptor.Handler;
                return true;
            }

            handler = null!;
            return false;
        }

        public void Freeze() => _keys.Freeze();

        public void Clear()
        {
            Array.Clear(_descriptors, 0, _descriptors.Length);
            Array.Clear(_registeredHandlers, 0, _registeredHandlers.Length);
            _keys.Clear();
            RegisterBuiltinKeys();
        }
    }
}
