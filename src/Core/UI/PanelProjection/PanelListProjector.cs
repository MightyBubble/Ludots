using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Binds EntityCollection rows to scalar columns declared by <see cref="PanelListDeclaration"/>.
    /// Membership and order come from the query graph; this type does not filter or sort.
    /// </summary>
    public sealed class PanelListProjector
    {
        private readonly World _world;
        private readonly EntityCollectionStore _collections;

        public PanelListProjector(World world, EntityCollectionStore collections)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
        }

        public static void BindSymbols(PanelTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            foreach (PanelListDeclaration list in template.Lists)
            {
                foreach (PanelItemField field in list.Fields)
                {
                    field.SymbolId = field.Kind switch
                    {
                        PanelItemFieldKind.Attribute or PanelItemFieldKind.AttributeBase =>
                            AttributeRegistry.GetId(field.Symbol!),
                        PanelItemFieldKind.Tag => TagRegistry.GetId(field.Symbol!),
                        _ => -1,
                    };
                }
            }
        }

        public IReadOnlyList<PanelListProjection> Project(Entity scope, PanelTemplate template)
        {
            if (template.Lists.Count == 0)
            {
                return Array.Empty<PanelListProjection>();
            }

            var result = new List<PanelListProjection>(template.Lists.Count);
            foreach (PanelListDeclaration list in template.Lists)
            {
                result.Add(ProjectList(scope, list));
            }

            return result;
        }

        private PanelListProjection ProjectList(Entity scope, PanelListDeclaration list)
        {
            var items = new List<PanelListItemProjection>(16);
            if (_collections.TryGet(scope, list.CollectionKey, out EntityCollectionHandle handle) &&
                _collections.TryGetView(handle, out EntityCollectionView view))
            {
                for (int i = 0; i < view.Count; i++)
                {
                    if (!_collections.TryGetEntityAt(handle, i, out Entity entity) ||
                        entity == Entity.Null ||
                        !_world.IsAlive(entity))
                    {
                        continue;
                    }

                    items.Add(ProjectItem(entity, list.Fields));
                }
            }

            return new PanelListProjection(list.Name, items);
        }

        private PanelListItemProjection ProjectItem(Entity entity, IReadOnlyList<PanelItemField> fields)
        {
            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            var bools = new Dictionary<string, bool>(StringComparer.Ordinal);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < fields.Count; i++)
            {
                PanelItemField field = fields[i];
                switch (field.Kind)
                {
                    case PanelItemFieldKind.Attribute:
                        floats[field.Name] = ReadAttribute(entity, field.SymbolId);
                        break;
                    case PanelItemFieldKind.AttributeBase:
                        floats[field.Name] = ReadAttributeBase(entity, field.SymbolId);
                        break;
                    case PanelItemFieldKind.Tag:
                        bools[field.Name] = HasTag(entity, field.SymbolId);
                        break;
                    case PanelItemFieldKind.Name:
                        strings[field.Name] = ReadName(entity);
                        break;
                }
            }

            return new PanelListItemProjection(floats, bools, strings);
        }

        private float ReadAttribute(Entity entity, int attributeId)
        {
            if (attributeId < 0 || !_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private float ReadAttributeBase(Entity entity, int attributeId)
        {
            if (attributeId < 0 || !_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return _world.Get<AttributeBuffer>(entity).GetBase(attributeId);
        }

        private bool HasTag(Entity entity, int tagId)
        {
            if (tagId <= 0 || !_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            return _world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private string ReadName(Entity entity)
        {
            if (!_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return string.Empty;
            }

            string? value = _world.Get<Name>(entity).Value;
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }
    }
}
