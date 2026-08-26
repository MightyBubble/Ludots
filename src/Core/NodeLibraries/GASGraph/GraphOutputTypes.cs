using System;
using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphOutputDestinationKind : byte
    {
        Summary = 0,
        EntityCollection = 1,
        EffectTemplateCollection = 2,
        AbilitySlotCollection = 3,
        AbilityDefinitionCollection = 4,
        ItemInstanceCollection = 5,
        ItemDefinitionCollection = 6,
        TagIdCollection = 7,
        TaskInstanceCollection = 8,
        ActivityInstanceCollection = 9,
        ProgressionNodeCollection = 10,
    }

    public enum GraphOutputValueKind : byte
    {
        Bool = 1,
        Int = 2,
        Float = 3,
        Entity = 4,
        TargetList = 5,
        IntIdList = 6,
    }

    public static class GraphOutputDestinationKinds
    {
        public static bool IsEntityBagDestination(GraphOutputDestinationKind destination) =>
            destination is GraphOutputDestinationKind.EntityCollection
                or GraphOutputDestinationKind.ItemInstanceCollection
                or GraphOutputDestinationKind.TaskInstanceCollection
                or GraphOutputDestinationKind.ActivityInstanceCollection;

        public static bool IsIntIdBagDestination(GraphOutputDestinationKind destination) =>
            destination is GraphOutputDestinationKind.EffectTemplateCollection
                or GraphOutputDestinationKind.AbilitySlotCollection
                or GraphOutputDestinationKind.AbilityDefinitionCollection
                or GraphOutputDestinationKind.ItemDefinitionCollection
                or GraphOutputDestinationKind.TagIdCollection
                or GraphOutputDestinationKind.ProgressionNodeCollection;
    }

    public readonly struct GraphOutputBinding
    {
        public GraphOutputBinding(
            string id,
            GraphOutputDestinationKind destination,
            GraphOutputValueKind valueKind,
            byte register,
            int keyId,
            string key,
            string collectionKey,
            EntityCollectionRoleKind collectionRole,
            string title,
            string summary,
            int collectionKeyId = 0)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Graph output id is required.", nameof(id))
                : id;
            Destination = destination;
            ValueKind = valueKind;
            Register = register;
            KeyId = keyId;
            Key = key ?? string.Empty;
            CollectionKey = collectionKey ?? string.Empty;
            CollectionKeyId = collectionKeyId;
            CollectionRole = collectionRole;
            Title = title ?? string.Empty;
            Summary = summary ?? string.Empty;
        }

        public string Id { get; }
        public GraphOutputDestinationKind Destination { get; }
        public GraphOutputValueKind ValueKind { get; }
        public byte Register { get; }
        public int KeyId { get; }
        public string Key { get; }
        public string CollectionKey { get; }

        /// <summary>Resolved from <see cref="CollectionKey"/> through the collection key registry at load time; 0 when unresolved.</summary>
        public int CollectionKeyId { get; }

        public EntityCollectionRoleKind CollectionRole { get; }
        public string Title { get; }
        public string Summary { get; }

        public GraphOutputBinding WithResolvedKeyId(int keyId)
        {
            return new GraphOutputBinding(
                Id,
                Destination,
                ValueKind,
                Register,
                keyId,
                Key,
                CollectionKey,
                CollectionRole,
                Title,
                Summary,
                CollectionKeyId);
        }

        public GraphOutputBinding WithResolvedCollectionKeyId(int collectionKeyId)
        {
            return new GraphOutputBinding(
                Id,
                Destination,
                ValueKind,
                Register,
                KeyId,
                Key,
                CollectionKey,
                CollectionRole,
                Title,
                Summary,
                collectionKeyId);
        }
    }

    public sealed class GraphOutputSchema
    {
        public static readonly GraphOutputSchema Empty = new(Array.Empty<GraphOutputBinding>());

        public GraphOutputSchema(GraphOutputBinding[] bindings)
        {
            Bindings = bindings ?? Array.Empty<GraphOutputBinding>();
        }

        public GraphOutputBinding[] Bindings { get; }
        public bool HasBindings => Bindings.Length > 0;
    }

    public sealed class GraphOutputSchemaRegistry
    {
        private GraphOutputSchema[] _schemas = new GraphOutputSchema[32];

        public void Clear()
        {
            Array.Clear(_schemas, 0, _schemas.Length);
        }

        public void Register(int graphId, GraphOutputSchema schema)
        {
            if (graphId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(graphId));
            }

            EnsureCapacity(graphId + 1);
            _schemas[graphId] = schema ?? GraphOutputSchema.Empty;
        }

        public GraphOutputSchema Get(int graphId)
        {
            if ((uint)graphId >= (uint)_schemas.Length)
            {
                return GraphOutputSchema.Empty;
            }

            return _schemas[graphId] ?? GraphOutputSchema.Empty;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _schemas.Length)
            {
                return;
            }

            int next = _schemas.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _schemas, next);
        }
    }
}
