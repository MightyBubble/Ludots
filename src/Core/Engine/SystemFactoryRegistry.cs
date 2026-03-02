using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Scripting;

namespace Ludots.Core.Engine
{
    /// <summary>
    /// Registry for system factories. Mods register factories at OnLoad time;
    /// map triggers activate them on demand via TryActivate.
    /// </summary>
    public sealed class SystemFactoryRegistry
    {
        private readonly struct Entry
        {
            public readonly SystemGroup Group;
            public readonly Func<ScriptContext, ISystem<float>> Factory;
            public readonly bool IsPresentation;

            public Entry(SystemGroup group, Func<ScriptContext, ISystem<float>> factory, bool isPresentation)
            {
                Group = group;
                Factory = factory;
                IsPresentation = isPresentation;
            }
        }

        private readonly Dictionary<string, Entry> _factories
            = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _activated
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a simulation system factory. The system will be created and
        /// registered into the specified SystemGroup when TryActivate is called.
        /// </summary>
        public void Register(string name, SystemGroup group, Func<ScriptContext, ISystem<float>> factory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("System factory name must not be empty.", nameof(name));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (_factories.ContainsKey(name))
            {
                Log.Warn(in LogChannels.Engine, $"SystemFactory '{name}' already registered. Overwriting.");
            }

            _factories[name] = new Entry(group, factory, isPresentation: false);
        }

        /// <summary>
        /// Register a presentation system factory. Presentation systems run after
        /// the simulation loop and are not bound to a SystemGroup.
        /// </summary>
        public void RegisterPresentation(string name, Func<ScriptContext, ISystem<float>> factory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("System factory name must not be empty.", nameof(name));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (_factories.ContainsKey(name))
            {
                Log.Warn(in LogChannels.Engine, $"SystemFactory '{name}' already registered (presentation). Overwriting.");
            }

            _factories[name] = new Entry(default, factory, isPresentation: true);
        }

        /// <summary>
        /// Activate a registered system factory. Idempotent — returns true on first
        /// activation, false on subsequent calls for the same name.
        /// </summary>
        public bool TryActivate(string name, ScriptContext context, GameEngine engine)
        {
            if (_activated.Contains(name))
                return false;

            if (!_factories.TryGetValue(name, out var entry))
            {
                Log.Warn(in LogChannels.Engine, $"SystemFactory '{name}' not found. Cannot activate.");
                return false;
            }

            var system = entry.Factory(context);
            if (system == null)
            {
                Log.Warn(in LogChannels.Engine, $"SystemFactory '{name}' returned null. Skipping.");
                return false;
            }

            if (entry.IsPresentation)
            {
                engine.RegisterPresentationSystem(system);
            }
            else
            {
                engine.RegisterSystem(system, entry.Group);
            }

            _activated.Add(name);
            Log.Info(in LogChannels.Engine, $"Activated system '{name}' (group={entry.Group}, presentation={entry.IsPresentation}).");
            return true;
        }

        /// <summary>
        /// Check whether a system has already been activated.
        /// </summary>
        public bool IsActivated(string name) => _activated.Contains(name);
    }
}
