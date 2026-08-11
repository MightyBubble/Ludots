using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Fail-closed panel variable reader over AttributeBuffer and GraphOutputValueStore.
    /// Shared mouth for native surfaces and WPK producers — no silent zero.
    /// </summary>
    public sealed class PanelProjectionReader
    {
        private readonly World _world;
        private readonly GraphOutputValueStore? _graphOutputs;
        private readonly Func<string, int> _resolveAttributeId;

        public PanelProjectionReader(
            World world,
            GraphOutputValueStore? graphOutputs = null,
            Func<string, int>? resolveAttributeId = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _graphOutputs = graphOutputs;
            _resolveAttributeId = resolveAttributeId ?? AttributeRegistry.GetId;
        }

        public float ResolveFloat(Entity owner, in PanelVariableBinding binding)
        {
            return Resolve(owner, in binding).FloatValue;
        }

        public PanelProjectionValue Resolve(Entity owner, in PanelVariableBinding binding)
        {
            if (owner == Entity.Null || !_world.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' requires a live owner entity.");
            }

            return binding.SourceKind switch
            {
                PanelBindingSourceKind.SingleAttribute or PanelBindingSourceKind.DerivedAttribute
                    => ResolveAttribute(owner, in binding),
                PanelBindingSourceKind.AggregateProjection or PanelBindingSourceKind.GraphOutput
                    => ResolveGraphOutput(owner, in binding),
                _ => throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' has unsupported sourceKind '{binding.SourceKind}'."),
            };
        }

        private PanelProjectionValue ResolveAttribute(Entity owner, in PanelVariableBinding binding)
        {
            string attributeId = binding.AttributeId
                ?? throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' is missing attributeId.");

            int id = _resolveAttributeId(attributeId);
            if (id == AttributeRegistry.InvalidId || id < 0)
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' references unknown attribute '{attributeId}'.");
            }

            if (!_world.TryGet(owner, out AttributeBuffer buffer))
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' requires AttributeBuffer on owner #{owner.Id} for attribute '{attributeId}'.");
            }

            if (!buffer.HasAttribute(id))
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' attribute '{attributeId}' is not defined on owner #{owner.Id}.");
            }

            float value = buffer.GetCurrent(id);
            uint revision = (uint)BitConverter.SingleToInt32Bits(value);
            return new PanelProjectionValue(binding.VariableId, binding.SourceKind, value, revision);
        }

        private PanelProjectionValue ResolveGraphOutput(Entity owner, in PanelVariableBinding binding)
        {
            if (_graphOutputs == null)
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' requires GraphOutputValueStore for graphOutputKey '{binding.GraphOutputKey}'.");
            }

            string key = binding.GraphOutputKey
                ?? throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' is missing graphOutputKey.");

            if (!_graphOutputs.TryGet(owner, key, out GraphOutputValueHandle handle) ||
                !_graphOutputs.TryGetView(handle, out GraphOutputValueView view))
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' missing graph output '{key}' on owner #{owner.Id}. Silent zero is forbidden.");
            }

            float value = view.Kind switch
            {
                GraphOutputValueKind.Float => view.FloatValue,
                GraphOutputValueKind.Int => view.IntValue,
                GraphOutputValueKind.Bool => view.BoolValue ? 1f : 0f,
                _ => throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' graph output '{key}' has unsupported kind '{view.Kind}' for ResolveFloat."),
            };

            uint revision = view.Revision ^ (uint)BitConverter.SingleToInt32Bits(value);
            return new PanelProjectionValue(binding.VariableId, binding.SourceKind, value, revision);
        }
    }
}
