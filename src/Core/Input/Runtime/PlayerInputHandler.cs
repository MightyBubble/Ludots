using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;

namespace Ludots.Core.Input.Runtime
{
    public class PlayerInputHandler : IInputActionReader
    {
        private readonly IInputBackend _backend;
        private readonly List<CompiledContext> _activeContexts = new();
        private readonly Dictionary<string, CompiledContext> _contextsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _actionIndices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _injections = new(StringComparer.Ordinal);
        private readonly InputActionInstance[] _actionStates;
        private readonly Vector3[] _tempValues;

        public bool InputBlocked { get; set; } = false;
        public long UpdateRevision { get; private set; }

        public PlayerInputHandler(IInputBackend backend, InputConfigRoot config)
        {
            _backend = backend;
            if (config == null) throw new ArgumentNullException(nameof(config));

            _actionStates = new InputActionInstance[config.Actions.Count];
            _tempValues = new Vector3[config.Actions.Count];
            for (int i = 0; i < config.Actions.Count; i++)
            {
                var actionDef = config.Actions[i];
                _actionIndices[actionDef.Id] = i;
                _actionStates[i] = new InputActionInstance(actionDef);
            }

            for (int i = 0; i < config.Contexts.Count; i++)
            {
                var context = config.Contexts[i];
                _contextsById[context.Id] = CompileContext(context);
            }
        }

        public void PushContext(string contextId)
        {
            if (!_contextsById.TryGetValue(contextId, out var context))
            {
                return;
            }

            if (_activeContexts.Contains(context))
            {
                return;
            }

            _activeContexts.Add(context);
            _activeContexts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public void PopContext(string contextId)
        {
            _activeContexts.RemoveAll(c => string.Equals(c.Id, contextId, StringComparison.Ordinal));
        }

        public bool HasAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;
            return _actionIndices.ContainsKey(actionId);
        }

        public bool HasContext(string contextId)
        {
            if (string.IsNullOrWhiteSpace(contextId)) return false;
            return _contextsById.ContainsKey(contextId);
        }

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (_actionIndices.TryGetValue(actionId, out int actionIndex))
            {
                return _actionStates[actionIndex].ReadValue<T>();
            }
            return default;
        }

        public bool IsDown(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].Triggered;
        }

        public bool PressedThisFrame(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].PressedThisFrame;
        }

        public bool ReleasedThisFrame(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].ReleasedThisFrame;
        }

        /// <summary>
        /// Inject action value for next Update() tick.
        /// Primarily for automation and deterministic test driving.
        /// </summary>
        public void InjectAction(string actionId, Vector3 value)
        {
            _injections[actionId] = value;
        }

        public void InjectButtonPress(string actionId) => InjectAction(actionId, Vector3.One);

        public void InjectButtonRelease(string actionId) => _injections.Remove(actionId);

        public void Update()
        {
            UpdateRevision++;

            if (InputBlocked)
            {
                for (int i = 0; i < _actionStates.Length; i++)
                {
                    _actionStates[i].ClearSuppressed();
                }
                return;
            }

            Array.Clear(_tempValues, 0, _tempValues.Length);

            for (int contextIndex = 0; contextIndex < _activeContexts.Count; contextIndex++)
            {
                var context = _activeContexts[contextIndex];
                var bindings = context.Bindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    ref readonly var binding = ref bindings[bindingIndex];
                    if (binding.ActionIndex < 0)
                    {
                        continue;
                    }

                    Vector3 value = ResolveBindingValue(in binding);
                    _tempValues[binding.ActionIndex] += value;
                }
            }

            foreach (var (actionId, value) in _injections)
            {
                if (_actionIndices.TryGetValue(actionId, out int actionIndex))
                {
                    _tempValues[actionIndex] = value;
                }
            }
            _injections.Clear();

            for (int i = 0; i < _actionStates.Length; i++)
            {
                _actionStates[i].Update(_tempValues[i]);
            }
        }

        private CompiledContext CompileContext(InputContextDef context)
        {
            var bindings = context.Bindings ?? new List<InputBindingDef>();
            var compiledBindings = new CompiledBinding[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                compiledBindings[i] = CompileBinding(bindings[i]);
            }

            return new CompiledContext
            {
                Id = context.Id,
                Priority = context.Priority,
                Bindings = compiledBindings,
            };
        }

        private CompiledBinding CompileBinding(InputBindingDef binding)
        {
            int actionIndex = -1;
            if (!string.IsNullOrWhiteSpace(binding.ActionId))
            {
                _actionIndices.TryGetValue(binding.ActionId, out actionIndex);
            }

            var compiled = new CompiledBinding
            {
                ActionIndex = actionIndex,
                Path = binding.Path ?? string.Empty,
                Processors = CompileProcessors(binding.Processors),
            };

            if (!string.IsNullOrEmpty(binding.CompositeType))
            {
                var parts = binding.CompositeParts ?? new List<InputBindingDef>();
                compiled.CompositeParts = new CompiledBinding[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                {
                    compiled.CompositeParts[i] = CompileBinding(parts[i]);
                }

                compiled.SourceKind = string.Equals(binding.CompositeType, "Vector2", StringComparison.OrdinalIgnoreCase)
                    ? BindingSourceKind.CompositeVector2
                    : BindingSourceKind.Unsupported;
                return compiled;
            }

            if (compiled.Path.StartsWith("<Mouse>/Pos", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.MousePosition;
            }
            else if (compiled.Path.StartsWith("<Mouse>/Scroll", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.MouseScroll;
            }
            else if (compiled.Path.StartsWith("<Keyboard>", StringComparison.Ordinal) || compiled.Path.StartsWith("<Mouse>", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.Button;
            }
            else
            {
                compiled.SourceKind = BindingSourceKind.Unsupported;
            }

            return compiled;
        }

        private static CompiledProcessor[] CompileProcessors(List<InputModifierDef> processorDefs)
        {
            if (processorDefs == null || processorDefs.Count == 0)
            {
                return Array.Empty<CompiledProcessor>();
            }

            var compiled = new CompiledProcessor[processorDefs.Count];
            for (int i = 0; i < processorDefs.Count; i++)
            {
                var def = processorDefs[i];
                compiled[i] = def.Type switch
                {
                    "Normalize" => new CompiledProcessor(ProcessorKind.Normalize, 0f),
                    "Deadzone" => new CompiledProcessor(ProcessorKind.Deadzone, GetParameter(def.Parameters, "Min", 0.1f)),
                    "Scale" => new CompiledProcessor(ProcessorKind.Scale, GetParameter(def.Parameters, "Factor", 1f)),
                    _ => new CompiledProcessor(ProcessorKind.Unknown, 0f)
                };
            }

            return compiled;
        }

        private static float GetParameter(IReadOnlyList<InputParameterDef> parameters, string name, float fallback)
        {
            if (parameters == null)
            {
                return fallback;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
                {
                    return parameter.Value;
                }
            }

            return fallback;
        }

        private Vector3 ResolveBindingValue(in CompiledBinding binding)
        {
            Vector3 rawValue = ReadRawBindingValue(in binding);
            var processors = binding.Processors;
            for (int i = 0; i < processors.Length; i++)
            {
                rawValue = ApplyProcessor(rawValue, in processors[i]);
            }

            return rawValue;
        }

        private Vector3 ReadRawBindingValue(in CompiledBinding binding)
        {
            switch (binding.SourceKind)
            {
                case BindingSourceKind.MousePosition:
                {
                    var pos = _backend.GetMousePosition();
                    return new Vector3(pos.X, pos.Y, 0f);
                }
                case BindingSourceKind.MouseScroll:
                    return new Vector3(_backend.GetMouseWheel(), 0f, 0f);
                case BindingSourceKind.Button:
                    return _backend.GetButton(binding.Path) ? Vector3.One : Vector3.Zero;
                case BindingSourceKind.CompositeVector2:
                {
                    float up = ReadCompositeScalar(binding.CompositeParts, 0);
                    float down = ReadCompositeScalar(binding.CompositeParts, 1);
                    float left = ReadCompositeScalar(binding.CompositeParts, 2);
                    float right = ReadCompositeScalar(binding.CompositeParts, 3);
                    return new Vector3(right - left, up - down, 0f);
                }
                default:
                    return Vector3.Zero;
            }
        }

        private float ReadCompositeScalar(CompiledBinding[] parts, int index)
        {
            if ((uint)index >= (uint)parts.Length)
            {
                return 0f;
            }

            return ResolveBindingValue(in parts[index]).X;
        }

        private static Vector3 ApplyProcessor(Vector3 value, in CompiledProcessor processor)
        {
            switch (processor.Kind)
            {
                case ProcessorKind.Normalize:
                    if (value.LengthSquared() > 1f)
                    {
                        return Vector3.Normalize(value);
                    }
                    return value;
                case ProcessorKind.Deadzone:
                    return value.Length() < processor.Scalar ? Vector3.Zero : value;
                case ProcessorKind.Scale:
                    return value * processor.Scalar;
                default:
                    return value;
            }
        }

        private sealed class CompiledContext
        {
            public string Id { get; init; } = string.Empty;
            public int Priority { get; init; }
            public CompiledBinding[] Bindings { get; init; } = Array.Empty<CompiledBinding>();
        }

        private sealed class CompiledBinding
        {
            public int ActionIndex { get; init; }
            public string Path { get; init; } = string.Empty;
            public BindingSourceKind SourceKind { get; set; }
            public CompiledBinding[] CompositeParts { get; set; } = Array.Empty<CompiledBinding>();
            public CompiledProcessor[] Processors { get; init; } = Array.Empty<CompiledProcessor>();
        }

        private readonly record struct CompiledProcessor(ProcessorKind Kind, float Scalar);

        private enum BindingSourceKind : byte
        {
            Unsupported = 0,
            MousePosition = 1,
            MouseScroll = 2,
            Button = 3,
            CompositeVector2 = 4,
        }

        private enum ProcessorKind : byte
        {
            Unknown = 0,
            Normalize = 1,
            Deadzone = 2,
            Scale = 3,
        }
    }
}
