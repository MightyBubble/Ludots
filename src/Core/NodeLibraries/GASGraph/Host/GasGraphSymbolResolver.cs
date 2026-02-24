using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Resolves graph symbol names to runtime ids by delegating to GAS registries.
    /// Keeps <see cref="GraphProgramLoader"/> decoupled from concrete registry types.
    /// </summary>
    public sealed class GasGraphSymbolResolver : IGraphSymbolResolver
    {
        public int ResolveTag(string name)
        {
            int id = TagRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown tag '{name}'. Register tags before loading graph programs.");
            }
            return id;
        }

        public int ResolveAttribute(string name)
        {
            int id = AttributeRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown attribute '{name}'. Register attributes before loading graph programs.");
            }
            return id;
        }

        public int ResolveEffectTemplate(string name)
        {
            int id = EffectTemplateIdRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown effect template '{name}'.");
            }
            return id;
        }
    }
}
