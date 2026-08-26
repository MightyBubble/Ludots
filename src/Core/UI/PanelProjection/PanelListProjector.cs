using System;
using System.Collections.Generic;
using System.Globalization;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.TypedCollections;
using Ludots.Core.UI.PanelHosting;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// For each collection member, evaluates the element template graph with that
    /// member as scope and materializes pin bags. Membership/order come from the
    /// query graph; this type does not filter or sort. Supports windowed projection
    /// for virtualized lists.
    /// </summary>
    public sealed class PanelListProjector
    {
        private const int MaxCollectionDepth = 2;

        private readonly World _world;
        private readonly EntityCollectionStore _collections;
        private readonly IntIdCollectionStore _intIdCollections;
        private readonly ItemDefinitionRegistry _itemDefinitions;
        private readonly PanelProjectionReader _reader;
        private readonly IPanelGraphEvaluator? _graphEvaluator;

        public PanelListProjector(
            World world,
            EntityCollectionStore collections,
            IntIdCollectionStore intIdCollections,
            ItemDefinitionRegistry itemDefinitions,
            PanelProjectionReader reader,
            IPanelGraphEvaluator? graphEvaluator = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _intIdCollections = intIdCollections ?? throw new ArgumentNullException(nameof(intIdCollections));
            _itemDefinitions = itemDefinitions ?? throw new ArgumentNullException(nameof(itemDefinitions));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _graphEvaluator = graphEvaluator;
        }

        public static void BindElements(PanelTemplate host, PanelTemplateRegistry templates)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(templates);
            BindElements(host, templates, depth: 0, new HashSet<string>(StringComparer.Ordinal));
        }

        private static void BindElements(
            PanelTemplate host,
            PanelTemplateRegistry templates,
            int depth,
            HashSet<string> path)
        {
            if (host.Collections.Count == 0)
            {
                return;
            }

            if (depth >= MaxCollectionDepth)
            {
                throw new InvalidOperationException(
                    $"Panel '{host.Id}' collections exceed the maximum projection depth {MaxCollectionDepth}.");
            }

            if (!path.Add(host.Id))
            {
                throw new InvalidOperationException(
                    $"Panel collection templates contain a cycle at '{host.Id}'.");
            }

            foreach (PanelCollectionBinding collection in host.Collections)
            {
                PanelTemplate element = templates.Require(collection.TemplateId);
                if (element.Subject == PanelSubjectKind.None)
                {
                    throw new InvalidOperationException(
                        $"Panel '{host.Id}' collection '{collection.Name}' template '{collection.TemplateId}' must declare subject (Entity/EffectInstance/…).");
                }

                if (!PanelSubjectKinds.IsEntityBagSubject(element.Subject) &&
                    !PanelSubjectKinds.IsIntIdBagSubject(element.Subject))
                {
                    throw new InvalidOperationException(
                        $"Panel '{host.Id}' collection '{collection.Name}' template '{collection.TemplateId}' subject '{PanelSubjectKinds.ToId(element.Subject)}' is not a collection subject.");
                }

                if (collection.Source == PanelCollectionSourceKind.Input)
                {
                    PanelInputBinding input = RequireInputBinding(host, collection);
                    if (!InputTypeMatchesSubject(input.Type, element.Subject))
                    {
                        throw new InvalidOperationException(
                            $"Panel '{host.Id}' collection '{collection.Name}' input '{input.Name}' type '{input.Type}' does not match element subject '{PanelSubjectKinds.ToId(element.Subject)}'.");
                    }
                }

                collection.Template = element;
                BindElements(element, templates, depth + 1, path);
            }

            path.Remove(host.Id);
        }

        public static bool TemplateUsesVirtualizedList(PanelTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (template.Layout == null)
            {
                return false;
            }

            for (int i = 0; i < template.Layout.Controls.Count; i++)
            {
                if (template.Layout.Controls[i].Type == PanelLayoutControlType.List &&
                    template.Layout.Controls[i].Virtualize)
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<PanelListProjection> Project(Entity scope, PanelTemplate template)
        {
            return Project(new PanelProjectionContext(scope, scope), template, PanelListViewWindow.All);
        }

        public IReadOnlyList<PanelListProjection> Project(
            Entity scope,
            PanelTemplate template,
            PanelListViewWindow window)
        {
            return Project(new PanelProjectionContext(scope, scope), template, window);
        }

        public IReadOnlyList<PanelListProjection> Project(
            PanelProjectionContext context,
            PanelTemplate template)
        {
            return Project(context, template, PanelListViewWindow.All);
        }

        public IReadOnlyList<PanelListProjection> Project(
            PanelProjectionContext context,
            PanelTemplate template,
            PanelListViewWindow window)
        {
            return Project(context, template, window, depth: 0);
        }

        private IReadOnlyList<PanelListProjection> Project(
            PanelProjectionContext context,
            PanelTemplate template,
            PanelListViewWindow window,
            int depth)
        {
            if (template.Collections.Count == 0)
            {
                return Array.Empty<PanelListProjection>();
            }

            if (depth >= MaxCollectionDepth)
            {
                throw new InvalidOperationException(
                    $"Panel '{template.Id}' collections exceed the maximum projection depth {MaxCollectionDepth}.");
            }

            var result = new List<PanelListProjection>(template.Collections.Count);
            foreach (PanelCollectionBinding collection in template.Collections)
            {
                result.Add(ProjectCollection(context, template, collection, window, depth));
            }

            return result;
        }

        public PanelListProjection ProjectCollectionWindow(
            Entity scope,
            PanelTemplate template,
            PanelCollectionBinding collection,
            PanelListViewWindow window)
        {
            return ProjectCollection(
                new PanelProjectionContext(scope, scope),
                template,
                collection,
                window,
                depth: 0);
        }

        public int CountMembers(
            Entity scope,
            PanelTemplate template,
            PanelCollectionBinding collection)
        {
            PanelTemplate element = RequireElement(collection);
            (Entity owner, string key) = ResolveCollectionLocation(
                new PanelProjectionContext(scope, scope),
                template,
                collection);
            if (PanelSubjectKinds.IsEntityBagSubject(element.Subject))
            {
                return _collections.TryGet(owner, key, out EntityCollectionHandle entityHandle) &&
                       _collections.TryGetView(entityHandle, out EntityCollectionView entityView)
                    ? entityView.Count
                    : 0;
            }

            return _intIdCollections.TryGet(owner, key, out IntIdCollectionHandle intHandle) &&
                   _intIdCollections.TryGetView(intHandle, out IntIdCollectionView intView)
                ? intView.Count
                : 0;
        }

        private PanelListProjection ProjectCollection(
            PanelProjectionContext context,
            PanelTemplate host,
            PanelCollectionBinding collection,
            PanelListViewWindow window,
            int depth)
        {
            PanelTemplate element = RequireElement(collection);
            (Entity owner, string key) = ResolveCollectionLocation(context, host, collection);
            if (PanelSubjectKinds.IsEntityBagSubject(element.Subject))
            {
                return ProjectEntityCollection(context.HostScope, owner, key, collection, element, window, depth);
            }

            return ProjectIntIdCollection(context.HostScope, owner, key, collection, element, window, depth);
        }

        private PanelListProjection ProjectEntityCollection(
            Entity hostScope,
            Entity owner,
            string key,
            PanelCollectionBinding collection,
            PanelTemplate element,
            PanelListViewWindow window,
            int depth)
        {
            if (!_collections.TryGet(owner, key, out EntityCollectionHandle handle) ||
                !_collections.TryGetView(handle, out EntityCollectionView view))
            {
                return new PanelListProjection(collection.Name, Array.Empty<PanelListItemProjection>(), totalCount: 0);
            }

            int total = view.Count;
            int start = Math.Clamp(window.StartIndex, 0, total);
            int end = Math.Clamp(window.ClampEnd(total), start, total);
            var items = new List<PanelListItemProjection>(Math.Max(0, end - start));
            for (int i = start; i < end; i++)
            {
                if (!_collections.TryGetEntityAt(handle, i, out Entity entity) ||
                    entity == Entity.Null ||
                    !_world.IsAlive(entity))
                {
                    continue;
                }

                items.Add(ProjectEntityElement(hostScope, entity, element, depth));
            }

            return new PanelListProjection(collection.Name, items, totalCount: total, startIndex: start);
        }

        private PanelListProjection ProjectIntIdCollection(
            Entity hostScope,
            Entity owner,
            string key,
            PanelCollectionBinding collection,
            PanelTemplate element,
            PanelListViewWindow window,
            int depth)
        {
            if (!_intIdCollections.TryGet(owner, key, out IntIdCollectionHandle handle) ||
                !_intIdCollections.TryGetView(handle, out IntIdCollectionView view))
            {
                return new PanelListProjection(collection.Name, Array.Empty<PanelListItemProjection>(), totalCount: 0);
            }

            int total = view.Count;
            int start = Math.Clamp(window.StartIndex, 0, total);
            int end = Math.Clamp(window.ClampEnd(total), start, total);
            var items = new List<PanelListItemProjection>(Math.Max(0, end - start));
            for (int i = start; i < end; i++)
            {
                if (!_intIdCollections.TryGetIdAt(handle, i, out int memberIntId) ||
                    memberIntId < 0 ||
                    (memberIntId == 0 && element.Subject != PanelSubjectKind.AbilitySlot))
                {
                    continue;
                }

                items.Add(ProjectIntIdElement(hostScope, owner, memberIntId, element, depth));
            }

            return new PanelListProjection(collection.Name, items, totalCount: total, startIndex: start);
        }

        private PanelListItemProjection ProjectEntityElement(
            Entity hostScope,
            Entity member,
            PanelTemplate element,
            int depth)
        {
            if (_graphEvaluator != null && element.GraphId >= 0)
            {
                try
                {
                    _graphEvaluator.Evaluate(element.GraphId, member);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log.Error(
                        in Diagnostics.LogChannels.Engine,
                        $"[PanelListProjector] element graph '{element.Graph}' failed for '{element.Id}': {ex.Message}");
                }
            }

            (Dictionary<string, float> floats, Dictionary<string, bool> bools) =
                ReadPins(member, element);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PanelSubjectKinds.EntityDisplayName] = ReadEntityDisplayName(member, element.Subject)
            };
            IReadOnlyList<PanelListProjection> nested = ProjectNested(
                new PanelProjectionContext(hostScope, member),
                element,
                depth);
            return new PanelListItemProjection(floats, bools, strings, nested);
        }

        private PanelListItemProjection ProjectIntIdElement(
            Entity hostScope,
            Entity owner,
            int memberIntId,
            PanelTemplate element,
            int depth)
        {
            if (_graphEvaluator != null && element.GraphId >= 0)
            {
                try
                {
                    _graphEvaluator.Evaluate(element.GraphId, owner, memberIntId);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log.Error(
                        in Diagnostics.LogChannels.Engine,
                        $"[PanelListProjector] element graph '{element.Graph}' failed for '{element.Id}': {ex.Message}");
                }
            }

            (Dictionary<string, float> floats, Dictionary<string, bool> bools) =
                ReadPins(owner, element);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PanelSubjectKinds.EntityDisplayName] =
                    ReadIntIdDisplayName(owner, memberIntId, element.Subject)
            };
            IReadOnlyList<PanelListProjection> nested = ProjectNested(
                new PanelProjectionContext(hostScope, owner),
                element,
                depth);
            return new PanelListItemProjection(floats, bools, strings, nested, memberIntId);
        }

        private (Dictionary<string, float> Floats, Dictionary<string, bool> Bools) ReadPins(
            Entity owner,
            PanelTemplate element)
        {
            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            var bools = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (PanelPin pin in element.Pins)
            {
                PanelProjectionValue value = _reader.Resolve(owner, pin);
                floats[pin.Name] = value.FloatValue;
                bools[pin.Name] = value.FloatValue != 0f;
            }

            return (floats, bools);
        }

        private IReadOnlyList<PanelListProjection> ProjectNested(
            PanelProjectionContext context,
            PanelTemplate element,
            int depth)
        {
            return element.Collections.Count == 0
                ? Array.Empty<PanelListProjection>()
                : Project(context, element, PanelListViewWindow.All, depth + 1);
        }

        private string ReadEntityDisplayName(Entity member, PanelSubjectKind subject)
        {
            return subject switch
            {
                PanelSubjectKind.EffectInstance => ReadEffectDisplayName(member),
                PanelSubjectKind.ItemInstance => ReadItemDisplayName(member),
                _ => ReadName(member),
            };
        }

        private string ReadIntIdDisplayName(
            Entity owner,
            int memberIntId,
            PanelSubjectKind subject)
        {
            return subject switch
            {
                PanelSubjectKind.EffectTemplate => EffectTemplateIdRegistry.GetName(memberIntId),
                PanelSubjectKind.ItemDefinition => ReadItemDefinitionDisplayName(memberIntId),
                PanelSubjectKind.AbilitySlot => ReadAbilitySlotDisplayName(owner, memberIntId),
                PanelSubjectKind.AbilityDefinition => AbilityIdRegistry.GetName(memberIntId),
                PanelSubjectKind.Tag => TagRegistry.GetName(memberIntId),
                PanelSubjectKind.ProgressionNode => ProgressionIdRegistry.GetName(memberIntId),
                _ => throw new InvalidOperationException(
                    $"Panel int-id projection does not support subject '{PanelSubjectKinds.ToId(subject)}'."),
            };
        }

        private string ReadItemDefinitionDisplayName(int definitionId)
        {
            if (definitionId > 0 &&
                _itemDefinitions.TryGet(definitionId, out ItemDefinition definition) &&
                !string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                return definition.DisplayName;
            }

            return definitionId > 0 ? _itemDefinitions.GetName(definitionId) : string.Empty;
        }

        private string ReadAbilitySlotDisplayName(Entity owner, int slotIndex)
        {
            if (AbilitySlotResolver.TryResolve(_world, owner, slotIndex, out AbilitySlotState slot))
            {
                string abilityName = AbilityIdRegistry.GetName(slot.AbilityId);
                if (!string.IsNullOrWhiteSpace(abilityName))
                {
                    return abilityName;
                }
            }

            return slotIndex.ToString(CultureInfo.InvariantCulture);
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

        private string ReadEffectDisplayName(Entity effectEntity)
        {
            if (!_world.IsAlive(effectEntity) || !_world.Has<EffectTemplateRef>(effectEntity))
            {
                return string.Empty;
            }

            int templateId = _world.Get<EffectTemplateRef>(effectEntity).TemplateId;
            if (templateId <= 0)
            {
                return string.Empty;
            }

            string name = EffectTemplateIdRegistry.GetName(templateId);
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        private string ReadItemDisplayName(Entity itemEntity)
        {
            if (!_world.IsAlive(itemEntity) || !_world.Has<ItemInstanceCm>(itemEntity))
            {
                return string.Empty;
            }

            int definitionId = _world.Get<ItemInstanceCm>(itemEntity).DefinitionId;
            return ReadItemDefinitionDisplayName(definitionId);
        }

        private static PanelTemplate RequireElement(PanelCollectionBinding collection)
        {
            return collection.Template
                ?? throw new InvalidOperationException(
                    $"Collection '{collection.Name}' template '{collection.TemplateId}' is not bound.");
        }

        private static (Entity Owner, string Key) ResolveCollectionLocation(
            PanelProjectionContext context,
            PanelTemplate host,
            PanelCollectionBinding collection)
        {
            if (collection.Source == PanelCollectionSourceKind.SelfGraph)
            {
                return (context.MemberScope, collection.CollectionKey);
            }

            PanelInputBinding input = RequireInputBinding(host, collection);
            return (context.HostScope, input.FromOutput);
        }

        private static PanelInputBinding RequireInputBinding(
            PanelTemplate host,
            PanelCollectionBinding collection)
        {
            if (string.IsNullOrWhiteSpace(collection.InputName))
            {
                throw new InvalidOperationException(
                    $"Panel '{host.Id}' collection '{collection.Name}' source=input has no input name.");
            }

            for (int i = 0; i < host.Inputs.Count; i++)
            {
                PanelInputBinding input = host.Inputs[i];
                if (string.Equals(input.Name, collection.InputName, StringComparison.Ordinal))
                {
                    return input;
                }
            }

            throw new InvalidOperationException(
                $"Panel '{host.Id}' collection '{collection.Name}' input '{collection.InputName}' is not declared.");
        }

        private static bool InputTypeMatchesSubject(string inputType, PanelSubjectKind subject)
        {
            return inputType switch
            {
                "EntityCollection" => PanelSubjectKinds.IsEntityBagSubject(subject),
                "EffectInstanceCollection" => subject == PanelSubjectKind.EffectInstance,
                "EffectTemplateCollection" => subject == PanelSubjectKind.EffectTemplate,
                "AbilitySlotCollection" => subject == PanelSubjectKind.AbilitySlot,
                "AbilityDefinitionCollection" => subject == PanelSubjectKind.AbilityDefinition,
                "ItemInstanceCollection" => subject == PanelSubjectKind.ItemInstance,
                "ItemDefinitionCollection" => subject == PanelSubjectKind.ItemDefinition,
                "TagIdCollection" => subject == PanelSubjectKind.Tag,
                "TaskInstanceCollection" => subject == PanelSubjectKind.Task,
                "ActivityInstanceCollection" => subject == PanelSubjectKind.Activity,
                "ProgressionNodeCollection" => subject == PanelSubjectKind.ProgressionNode,
                _ => false,
            };
        }
    }
}
