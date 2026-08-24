using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Modding;

namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandRouteStrategy : byte
    {
        None = 0,
        ExistingInstances = 1,
        ScopedInstance = 2,
        SingleRuntime = 3,
        CreatePresenter = 4,
        DestroyScope = 5,
    }

    public enum PresenterBehaviorExecutionLane : byte
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

    public readonly struct PresenterCommandView
    {
        public PresenterCommandView(in PresenterCommand command)
        {
            CommandKindId = command.CommandKindId;
            RouteStrategy = command.RouteStrategy;
            PresenterEntity = command.PresenterEntity;
            Source = command.Source;
            Target = command.Target;
            Viewer = command.Viewer;
            PresenterDefinitionId = command.PresenterDefinitionId;
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
        public PresenterCommandRouteStrategy RouteStrategy { get; }
        public Entity PresenterEntity { get; }
        public Entity Source { get; }
        public Entity Target { get; }
        public Entity Viewer { get; }
        public int PresenterDefinitionId { get; }
        public int ScopeTag { get; }
        public int TargetBehaviorSlot { get; }
        public int ParamKey { get; }
        public ParamLane ParamLane { get; }
        public float ParamValue { get; }
        public int IntValue { get; }
        public Vector4 VectorValue { get; }
        public bool HasParamPayload { get; }
    }

    public interface IPresenterCommandOps
    {
        bool HasRoutedPresenter { get; }
        void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default);
        void ClearParam(int paramKey, ParamLane lane);
        void ActivateBehavior(int slotIndex);
        void DeactivateBehavior(int slotIndex);
    }

    public readonly struct PresenterCommandExecutionContext
    {
        public PresenterCommandExecutionContext(in PresenterCommand command, IPresenterCommandOps ops)
        {
            Command = new PresenterCommandView(in command);
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
        }

        public PresenterCommandView Command { get; }
        public IPresenterCommandOps Ops { get; }
    }

    public delegate void PresenterCommandHandler(in PresenterCommandExecutionContext context);

    public readonly struct PresenterCommandExtensionDescriptor
    {
        public PresenterCommandExtensionDescriptor(
            PresenterCommandRouteStrategy routeStrategy,
            PresenterCommandHandler handler)
        {
            if (routeStrategy == PresenterCommandRouteStrategy.None)
            {
                throw new ArgumentOutOfRangeException(nameof(routeStrategy), "Extension presenter command route must be explicit.");
            }

            RouteStrategy = routeStrategy;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public PresenterCommandRouteStrategy RouteStrategy { get; }
        public PresenterCommandHandler Handler { get; }
    }

    public sealed class PresenterCommandKindRegistry
    {
        public const int FirstModCommandKindId = 1024;
        public const int MaxCommandKinds = 2048;

        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModCommandKindId,
            maxIdExclusive: MaxCommandKinds,
            comparer: StringComparer.Ordinal);
        private readonly PresenterCommandExtensionDescriptor[] _descriptors = new PresenterCommandExtensionDescriptor[MaxCommandKinds];
        private readonly bool[] _registeredHandlers = new bool[MaxCommandKinds];

        public PresenterCommandKindRegistry()
        {
            RegisterBuiltinKeys();
        }

        private void RegisterBuiltinKeys()
        {
            foreach (PresenterCommandKind kind in Enum.GetValues<PresenterCommandKind>())
            {
                if (kind != PresenterCommandKind.None)
                {
                    _keys.RegisterFixed(kind.ToString(), (byte)kind);
                }
            }
        }

        public int Register(string key, PresenterCommandHandler handler)
        {
            throw new InvalidOperationException(
                "Presenter command extensions must register a descriptor with an explicit route strategy.");
        }

        public int Register(string key, in PresenterCommandExtensionDescriptor descriptor)
        {
            if (Enum.TryParse(key, ignoreCase: false, out PresenterCommandKind reserved) &&
                reserved != PresenterCommandKind.None &&
                Enum.IsDefined(typeof(PresenterCommandKind), reserved))
            {
                throw new InvalidOperationException(
                    $"Presenter command kind '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            int id = _keys.RegisterDynamic(key);
            if (_registeredHandlers[id])
            {
                throw new InvalidOperationException($"Presenter command kind '{key}' is already registered.");
            }

            _descriptors[id] = descriptor;
            _registeredHandlers[id] = true;
            return id;
        }

        public bool TryGetId(string key, out int id)
        {
            if (Enum.TryParse(key, ignoreCase: false, out PresenterCommandKind builtin) &&
                builtin != PresenterCommandKind.None &&
                Enum.IsDefined(typeof(PresenterCommandKind), builtin))
            {
                id = (byte)builtin;
                return true;
            }

            return _keys.TryGetId(key, out id);
        }

        public int GetId(string key) => TryGetId(key, out int id) ? id : 0;

        public bool TryGetDescriptor(int id, out PresenterCommandExtensionDescriptor descriptor)
        {
            if ((uint)id < (uint)_descriptors.Length && _registeredHandlers[id])
            {
                descriptor = _descriptors[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        public bool TryGetHandler(int id, out PresenterCommandHandler handler)
        {
            if (TryGetDescriptor(id, out PresenterCommandExtensionDescriptor descriptor))
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

    public readonly struct PresenterBehaviorView
    {
        public PresenterBehaviorView(
            Entity presenter,
            Entity owner,
            int definitionId,
            int slotIndex,
            int kindId,
            PresenterBehaviorExecutionLane lane,
            bool firstFrame,
            float deltaTime)
        {
            Presenter = presenter;
            Owner = owner;
            DefinitionId = definitionId;
            SlotIndex = slotIndex;
            KindId = kindId;
            Lane = lane;
            FirstFrame = firstFrame;
            DeltaTime = deltaTime;
        }

        public Entity Presenter { get; }
        public Entity Owner { get; }
        public int DefinitionId { get; }
        public int SlotIndex { get; }
        public int KindId { get; }
        public PresenterBehaviorExecutionLane Lane { get; }
        public bool FirstFrame { get; }
        public float DeltaTime { get; }
    }

    public interface IPresenterBehaviorOps
    {
        bool TryResolveFloat(int paramKey, out float value);
        bool TryResolveInt(int paramKey, out int value);
        bool TryResolveVector(int paramKey, out Vector4 value);
        void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default);
        void ClearParam(int paramKey, ParamLane lane);
    }

    public readonly struct PresenterBehaviorExecutionContext
    {
        public PresenterBehaviorExecutionContext(in PresenterBehaviorView behavior, IPresenterBehaviorOps ops)
        {
            Behavior = behavior;
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
        }

        public PresenterBehaviorView Behavior { get; }
        public IPresenterBehaviorOps Ops { get; }
    }

    public delegate void PresenterBehaviorHandler(in PresenterBehaviorExecutionContext context);

    public readonly struct PresenterBehaviorExtensionDescriptor
    {
        public PresenterBehaviorExtensionDescriptor(
            PresenterBehaviorExecutionLane lane,
            PresenterBehaviorHandler handler)
        {
            if (lane == PresenterBehaviorExecutionLane.None)
            {
                throw new ArgumentOutOfRangeException(nameof(lane), "Extension presenter behavior lane must be explicit.");
            }

            Lane = lane;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public PresenterBehaviorExecutionLane Lane { get; }
        public PresenterBehaviorHandler Handler { get; }
    }

    public sealed class PresenterBehaviorKindRegistry
    {
        public const int FirstModBehaviorKindId = 1024;
        public const int MaxBehaviorKinds = 2048;

        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModBehaviorKindId,
            maxIdExclusive: MaxBehaviorKinds,
            comparer: StringComparer.Ordinal);
        private readonly PresenterBehaviorExtensionDescriptor[] _descriptors = new PresenterBehaviorExtensionDescriptor[MaxBehaviorKinds];
        private readonly bool[] _registeredHandlers = new bool[MaxBehaviorKinds];

        public PresenterBehaviorKindRegistry()
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

        public int Register(string key, PresenterBehaviorHandler handler)
        {
            throw new InvalidOperationException(
                "Presenter behavior extensions must register a descriptor with an explicit execution lane.");
        }

        public int Register(string key, in PresenterBehaviorExtensionDescriptor descriptor)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BehaviorKind reserved) &&
                reserved != BehaviorKind.None &&
                Enum.IsDefined(typeof(BehaviorKind), reserved))
            {
                throw new InvalidOperationException(
                    $"Presenter behavior kind '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            int id = _keys.RegisterDynamic(key);
            if (_registeredHandlers[id])
            {
                throw new InvalidOperationException($"Presenter behavior kind '{key}' is already registered.");
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

        public bool TryGetDescriptor(int id, out PresenterBehaviorExtensionDescriptor descriptor)
        {
            if ((uint)id < (uint)_descriptors.Length && _registeredHandlers[id])
            {
                descriptor = _descriptors[id];
                return true;
            }

            descriptor = default;
            return false;
        }

        public bool TryGetHandler(int id, out PresenterBehaviorHandler handler)
        {
            if (TryGetDescriptor(id, out PresenterBehaviorExtensionDescriptor descriptor))
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
