using System;
using System.Collections.Generic;
using Ludots.Core.Commands;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Registry for trigger decorators. Mods register decorators at OnLoad time;
    /// the engine applies them to map triggers after instantiation.
    /// </summary>
    public sealed class TriggerDecoratorRegistry
    {
        private readonly List<(Type Type, Action<Trigger> Decorator)> _typedDecorators
            = new List<(Type, Action<Trigger>)>();

        private readonly Dictionary<string, List<Action<Trigger>>> _namedDecorators
            = new Dictionary<string, List<Action<Trigger>>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<GameCommand>> _anchorDecorators
            = new Dictionary<string, List<GameCommand>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a decorator that matches triggers of a specific type.
        /// </summary>
        public void Register<T>(Action<T> decorator) where T : Trigger
        {
            if (decorator == null) throw new ArgumentNullException(nameof(decorator));

            _typedDecorators.Add((typeof(T), trigger =>
            {
                if (trigger is T typed)
                    decorator(typed);
            }));
        }

        /// <summary>
        /// Register a decorator that matches triggers by type name (for JSON-declared triggers).
        /// </summary>
        public void Register(string triggerTypeName, Action<Trigger> decorator)
        {
            if (string.IsNullOrWhiteSpace(triggerTypeName))
                throw new ArgumentException("Trigger type name must not be empty.", nameof(triggerTypeName));
            if (decorator == null)
                throw new ArgumentNullException(nameof(decorator));

            if (!_namedDecorators.TryGetValue(triggerTypeName, out var list))
            {
                list = new List<Action<Trigger>>();
                _namedDecorators[triggerTypeName] = list;
            }
            list.Add(decorator);
        }

        /// <summary>
        /// Register a command to be inserted after any AnchorCommand with the specified key.
        /// Works across all triggers that contain that anchor.
        /// </summary>
        public void RegisterAnchor(string anchorKey, GameCommand command)
        {
            if (string.IsNullOrWhiteSpace(anchorKey))
                throw new ArgumentException("Anchor key must not be empty.", nameof(anchorKey));
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (!_anchorDecorators.TryGetValue(anchorKey, out var list))
            {
                list = new List<GameCommand>();
                _anchorDecorators[anchorKey] = list;
            }
            list.Add(command);
        }

        /// <summary>
        /// Apply all matching decorators to the given trigger.
        /// Called by the engine after trigger instantiation and before registration.
        /// </summary>
        public void Apply(Trigger trigger)
        {
            if (trigger == null) return;

            var triggerType = trigger.GetType();

            // 1. Type-matched decorators
            for (int i = 0; i < _typedDecorators.Count; i++)
            {
                var (type, decorator) = _typedDecorators[i];
                if (type.IsAssignableFrom(triggerType))
                {
                    try
                    {
                        decorator(trigger);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.Engine, $"Error applying typed decorator to trigger '{trigger.Name}': {ex.Message}");
                    }
                }
            }

            // 2. Name-matched decorators
            string typeName = triggerType.Name;
            if (_namedDecorators.TryGetValue(typeName, out var namedList))
            {
                for (int i = 0; i < namedList.Count; i++)
                {
                    try
                    {
                        namedList[i](trigger);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.Engine, $"Error applying named decorator '{typeName}' to trigger '{trigger.Name}': {ex.Message}");
                    }
                }
            }

            // 3. Anchor-matched decorators — inject commands after matching AnchorCommand
            if (_anchorDecorators.Count > 0)
            {
                // Walk actions in reverse to handle insertions without shifting indices
                for (int a = trigger.Actions.Count - 1; a >= 0; a--)
                {
                    if (trigger.Actions[a] is AnchorCommand anchor && anchor.Key is string anchorKey)
                    {
                        if (_anchorDecorators.TryGetValue(anchorKey, out var commands))
                        {
                            for (int c = commands.Count - 1; c >= 0; c--)
                            {
                                trigger.Actions.Insert(a + 1, commands[c]);
                            }
                        }
                    }
                }
            }
        }
    }
}
