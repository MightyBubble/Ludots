using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
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
        private readonly World _world;
        private readonly EntityCollectionStore _collections;
        private readonly PanelProjectionReader _reader;
        private readonly IPanelGraphEvaluator? _graphEvaluator;

        public PanelListProjector(
            World world,
            EntityCollectionStore collections,
            PanelProjectionReader reader,
            IPanelGraphEvaluator? graphEvaluator = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _graphEvaluator = graphEvaluator;
        }

        public static void BindElements(PanelTemplate host, PanelTemplateRegistry templates)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(templates);

            foreach (PanelCollectionBinding collection in host.Collections)
            {
                PanelTemplate element = templates.Require(collection.TemplateId);
                if (element.Subject == PanelSubjectKind.None)
                {
                    throw new InvalidOperationException(
                        $"Panel '{host.Id}' collection '{collection.Name}' template '{collection.TemplateId}' must declare subject (Entity/Task/Ability).");
                }

                if (element.Subject is not PanelSubjectKind.Entity)
                {
                    throw new InvalidOperationException(
                        $"Panel '{host.Id}' collection '{collection.Name}' template '{collection.TemplateId}' subject '{PanelSubjectKinds.ToId(element.Subject)}' is not wired for EntityCollection yet.");
                }

                collection.Template = element;
            }
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
            return Project(scope, template, PanelListViewWindow.All);
        }

        public IReadOnlyList<PanelListProjection> Project(
            Entity scope,
            PanelTemplate template,
            PanelListViewWindow window)
        {
            if (template.Collections.Count == 0)
            {
                return Array.Empty<PanelListProjection>();
            }

            var result = new List<PanelListProjection>(template.Collections.Count);
            foreach (PanelCollectionBinding collection in template.Collections)
            {
                result.Add(ProjectCollection(scope, collection, window));
            }

            return result;
        }

        public PanelListProjection ProjectCollectionWindow(
            Entity scope,
            PanelCollectionBinding collection,
            PanelListViewWindow window)
        {
            return ProjectCollection(scope, collection, window);
        }

        public int CountMembers(Entity scope, PanelCollectionBinding collection)
        {
            if (_collections.TryGet(scope, collection.CollectionKey, out EntityCollectionHandle handle) &&
                _collections.TryGetView(handle, out EntityCollectionView view))
            {
                return view.Count;
            }

            return 0;
        }

        private PanelListProjection ProjectCollection(
            Entity scope,
            PanelCollectionBinding collection,
            PanelListViewWindow window)
        {
            PanelTemplate element = collection.Template
                ?? throw new InvalidOperationException(
                    $"Collection '{collection.Name}' template '{collection.TemplateId}' is not bound.");

            if (!_collections.TryGet(scope, collection.CollectionKey, out EntityCollectionHandle handle) ||
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

                items.Add(ProjectElement(entity, element));
            }

            return new PanelListProjection(collection.Name, items, totalCount: total, startIndex: start);
        }

        private PanelListItemProjection ProjectElement(Entity member, PanelTemplate element)
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

            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            var bools = new Dictionary<string, bool>(StringComparer.Ordinal);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (PanelPin pin in element.Pins)
            {
                PanelProjectionValue value = _reader.Resolve(member, pin);
                floats[pin.Name] = value.FloatValue;
                bools[pin.Name] = value.FloatValue != 0f;
            }

            if (element.Subject == PanelSubjectKind.Entity)
            {
                strings[PanelSubjectKinds.EntityDisplayName] = ReadName(member);
            }

            return new PanelListItemProjection(floats, bools, strings);
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
