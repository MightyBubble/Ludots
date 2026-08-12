using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Client
{
    /// <summary>Resolve <see cref="ClientLocalSeatRegistry"/> / possessed reps without global LocalPlayer slots.</summary>
    public static class ClientLocalSeatAccess
    {
        public static ClientLocalSeatRegistry RequireRegistry(IReadOnlyDictionary<string, object> globals)
        {
            if (globals == null)
            {
                throw new ArgumentNullException(nameof(globals));
            }

            if (!globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? obj) ||
                obj is not ClientLocalSeatRegistry registry)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.ClientLocalSeatRegistry.Name} must be registered.");
            }

            return registry;
        }

        public static ClientLocalSeatRegistry RequireRegistry(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException(
                    $"{CoreServiceKeys.ClientLocalSeatRegistry.Name} must be registered.");
        }

        public static LogicViewRegistry RequireLogicViews(IReadOnlyDictionary<string, object> globals)
        {
            if (globals == null)
            {
                throw new ArgumentNullException(nameof(globals));
            }

            if (!globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? obj) ||
                obj is not LogicViewRegistry registry)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered.");
            }

            return registry;
        }

        public static LogicViewRegistry RequireLogicViews(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return engine.GetService(CoreServiceKeys.LogicViewRegistry)
                ?? throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered.");
        }

        public static Entity RequireSolePossessedRep(IReadOnlyDictionary<string, object> globals) =>
            RequireRegistry(globals).RequireSolePossessedRep();

        public static Entity RequireSolePossessedRep(GameEngine engine) =>
            RequireRegistry(engine).RequireSolePossessedRep();

        public static bool TryGetSolePossessedRep(IReadOnlyDictionary<string, object> globals, out Entity rep)
        {
            rep = Entity.Null;
            if (globals == null ||
                !globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? obj) ||
                obj is not ClientLocalSeatRegistry registry)
            {
                return false;
            }

            return registry.TryGetSolePossessedRep(out rep);
        }

        public static bool TryGetSolePossessedRep(GameEngine engine, out Entity rep)
        {
            rep = Entity.Null;
            if (engine == null ||
                !engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null)
            {
                return false;
            }

            return registry.TryGetSolePossessedRep(out rep);
        }
    }
}
