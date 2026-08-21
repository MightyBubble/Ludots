using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
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
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GraphLookupTableRegistry? _lookupTables;
        private readonly Func<string, int> _resolveAttributeId;

        public PanelProjectionReader(
            World world,
            GraphOutputValueStore? graphOutputs = null,
            Func<string, int>? resolveAttributeId = null,
            Ludots.Core.NodeLibraries.GASGraph.Host.GraphLookupTableRegistry? lookupTables = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _graphOutputs = graphOutputs;
            _resolveAttributeId = resolveAttributeId ?? AttributeRegistry.GetId;
            _lookupTables = lookupTables;
        }

        public float ResolveFloat(Entity owner, in PanelVariableBinding binding)
        {
            return Resolve(owner, in binding).FloatValue;
        }

        public bool IsOwnerLive(Entity owner)
        {
            return owner != Entity.Null && _world.IsAlive(owner);
        }

        public bool IsOwnerInMap(Entity owner, MapId mapId)
        {
            return IsOwnerLive(owner) &&
                _world.Has<MapEntity>(owner) &&
                _world.Get<MapEntity>(owner).MapId == mapId;
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
                    => ResolveAttribute(owner, in binding, readBase: false),
                PanelBindingSourceKind.AttributeBase
                    => ResolveAttribute(owner, in binding, readBase: true),
                PanelBindingSourceKind.AggregateProjection or PanelBindingSourceKind.GraphOutput
                    => ResolveGraphOutput(owner, in binding),
                PanelBindingSourceKind.TableLookup
                    => ResolveTableLookup(owner, in binding),
                _ => throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' has unsupported sourceKind '{binding.SourceKind}'."),
            };
        }

        private PanelProjectionValue ResolveAttribute(Entity owner, in PanelVariableBinding binding, bool readBase)
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

            float value = readBase ? buffer.GetBase(id) : buffer.GetCurrent(id);
            uint revision = (uint)BitConverter.SingleToInt32Bits(value);
            return new PanelProjectionValue(binding.VariableId, binding.SourceKind, value, revision);
        }

        private PanelProjectionValue ResolveTableLookup(Entity owner, in PanelVariableBinding binding)
        {
            if (_lookupTables == null)
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' requires GraphLookupTableRegistry for table '{binding.LookupTable}'.");
            }

            string keyAttribute = binding.KeyAttribute
                ?? throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' is missing keyAttribute.");
            int keyAttributeId = _resolveAttributeId(keyAttribute);
            if (keyAttributeId == AttributeRegistry.InvalidId || keyAttributeId < 0)
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' references unknown key attribute '{keyAttribute}'.");
            }

            if (!_world.TryGet(owner, out AttributeBuffer buffer) || !buffer.HasAttribute(keyAttributeId))
            {
                throw new InvalidOperationException(
                    $"Panel binding '{binding.VariableId}' key attribute '{keyAttribute}' is not defined on owner #{owner.Id}.");
            }

            int key = (int)buffer.GetCurrent(keyAttributeId);
            string tableId = binding.LookupTable!;
            string fieldId = binding.LookupField!;
            int resolvedTable = _lookupTables.GetTableId(tableId);
            int row = _lookupTables.ResolveRow(resolvedTable, key);
            int resolvedField = _lookupTables.GetFieldId(tableId, fieldId);
            float value = binding.ValueKind == PanelTemplateVariableKind.Int
                ? _lookupTables.ReadInt(row, resolvedField)
                : _lookupTables.ReadFloat(row, resolvedField);
            uint revision = (uint)key ^ (uint)BitConverter.SingleToInt32Bits(value);
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
