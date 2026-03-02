using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime.Processors;
using Ludots.Core.Input.Runtime.Composites;

namespace Ludots.Core.Input.Runtime
{
    public class PlayerInputHandler
    {
        private readonly IInputBackend _backend;
        private readonly List<InputContextDef> _activeContexts = new List<InputContextDef>();
        private readonly Dictionary<string, InputActionInstance> _actionStates = new Dictionary<string, InputActionInstance>();
        private readonly Dictionary<string, Vector3> _tempValues;
        private readonly string[] _actionIds;
        private readonly InputConfigRoot _config;

        public bool InputBlocked { get; set; } = false;

        // Per-action injection buffer: actionId → injected raw value.
        // Written by InjectAction(), consumed and cleared in Update().
        // This bypasses IInputBackend entirely — the action sees the
        // injected value as if it came from a physical device.
        private readonly Dictionary<string, Vector3> _injections = new();

        public PlayerInputHandler(IInputBackend backend, InputConfigRoot config)
        {
            _backend = backend;
            _config = config;
            
            // Initialize Action States
            _actionIds = new string[config.Actions.Count];
            for (int i = 0; i < config.Actions.Count; i++)
            {
                var actionDef = config.Actions[i];
                _actionStates[actionDef.Id] = new InputActionInstance(actionDef);
                _actionIds[i] = actionDef.Id;
            }

            _tempValues = new Dictionary<string, Vector3>(_actionIds.Length);
            for (int i = 0; i < _actionIds.Length; i++)
            {
                _tempValues[_actionIds[i]] = Vector3.Zero;
            }
        }

        public void PushContext(string contextId)
        {
            var context = _config.Contexts.Find(c => c.Id == contextId);
            if (context != null && !_activeContexts.Contains(context))
            {
                _activeContexts.Add(context);
                // Sort by priority descending
                _activeContexts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public void PopContext(string contextId)
        {
            _activeContexts.RemoveAll(c => c.Id == contextId);
        }

        public bool HasAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;
            return _actionStates.ContainsKey(actionId);
        }

        public bool HasContext(string contextId)
        {
            if (string.IsNullOrWhiteSpace(contextId)) return false;
            return _config.Contexts.Exists(c => c.Id == contextId);
        }

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (_actionStates.TryGetValue(actionId, out var instance))
            {
                return instance.ReadValue<T>();
            }
            return default;
        }

        public bool IsDown(string actionId)
        {
            return _actionStates.TryGetValue(actionId, out var instance) && instance.Triggered;
        }

        public bool PressedThisFrame(string actionId)
        {
            return _actionStates.TryGetValue(actionId, out var instance) && instance.PressedThisFrame;
        }

        public bool ReleasedThisFrame(string actionId)
        {
            return _actionStates.TryGetValue(actionId, out var instance) && instance.ReleasedThisFrame;
        }

        /// <summary>
        /// Inject a raw value for an action, bypassing IInputBackend.
        /// The value is consumed on the next Update() call and auto-cleared.
        /// Use Vector3.One for button press, Vector3.Zero for release.
        /// </summary>
        public void InjectAction(string actionId, Vector3 value)
        {
            _injections[actionId] = value;
        }

        /// <summary>Convenience: inject a button press (one frame).</summary>
        public void InjectButtonPress(string actionId) => InjectAction(actionId, Vector3.One);

        /// <summary>Convenience: inject a button release (clear injection).</summary>
        public void InjectButtonRelease(string actionId) => _injections.Remove(actionId);

        public void Update()
        {
            if (InputBlocked)
            {
                foreach (var state in _actionStates.Values)
                {
                    state.ClearSuppressed();
                }
                return;
            }

            for (int i = 0; i < _actionIds.Length; i++)
            {
                _tempValues[_actionIds[i]] = Vector3.Zero;
            }

            // Iterate active contexts (Highest Priority First)
            foreach (var context in _activeContexts)
            {
                foreach (var binding in context.Bindings)
                {
                    if (!_tempValues.TryGetValue(binding.ActionId, out var acc)) continue;

                    Vector3 rawValue = ResolveBindingValue(binding);
                    
                    // Apply Processors
                    foreach (var procDef in binding.Processors)
                    {
                        rawValue = ApplyProcessor(rawValue, procDef);
                    }

                    // Accumulate or Override?
                    // Unity Input System usually accumulates for same action in same frame unless blocking.
                    // For now, simple accumulation.
                    acc += rawValue;
                    _tempValues[binding.ActionId] = acc;
                }
            }

            // Merge injections (override backend values for injected actions)
            foreach (var kvp in _injections)
            {
                if (_tempValues.ContainsKey(kvp.Key))
                    _tempValues[kvp.Key] = kvp.Value;
            }
            _injections.Clear();

            // Update States
            for (int i = 0; i < _actionIds.Length; i++)
            {
                string actionId = _actionIds[i];
                _actionStates[actionId].Update(_tempValues[actionId]);
            }
        }

        private Vector3 ResolveBindingValue(InputBindingDef binding)
        {
            if (!string.IsNullOrEmpty(binding.CompositeType))
            {
                // Handle Composite
                InputComposite composite = GetComposite(binding.CompositeType);
                if (composite != null)
                {
                    return composite.Evaluate(index => 
                    {
                        if (index < binding.CompositeParts.Count)
                        {
                            return ResolveBindingValue(binding.CompositeParts[index]);
                        }
                        return Vector3.Zero;
                    });
                }
            }
            else
            {
                // Direct Binding
                if (binding.Path.StartsWith("<Mouse>/Pos"))
                {
                    var pos = _backend.GetMousePosition();
                    return new Vector3(pos.X, pos.Y, 0);
                }
                else if (binding.Path.StartsWith("<Mouse>/Scroll"))
                {
                    // Map wheel to X so it can be read as a scalar float/Axis1D
                    return new Vector3(_backend.GetMouseWheel(), 0, 0); 
                }
                else if (binding.Path.StartsWith("<Keyboard>") || binding.Path.StartsWith("<Mouse>"))
                {
                    bool isDown = _backend.GetButton(binding.Path);
                    // if (isDown) Console.WriteLine($"[Input] Key Down: {binding.Path}");
                    return isDown ? Vector3.One : Vector3.Zero;
                }
                // Add Gamepad etc.
            }
            return Vector3.Zero;
        }

        private Vector3 ApplyProcessor(Vector3 value, InputModifierDef def)
        {
            // Simple factory
            InputProcessor processor = def.Type switch
            {
                "Normalize" => new NormalizeProcessor(),
                "Deadzone" => new DeadzoneProcessor(),
                "Scale" => new ScaleProcessor(),
                _ => null
            };

            return processor != null ? processor.Process(value, def.Parameters) : value;
        }

        private InputComposite GetComposite(string type)
        {
            return type switch
            {
                "Vector2" => new Vector2Composite(),
                _ => null
            };
        }
    }
}
