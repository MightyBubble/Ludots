using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using GenreInfoShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace GenreInfoShowcaseMod.Runtime
{
    internal sealed class GenreInfoShowcaseRuntime
    {
        private static readonly string[] Group1Names = { "Governor Aurelia" };
        private static readonly string[] Group2Names = { "Captain Nyx" };
        private static readonly string[] Group3Names =
        {
            "Marine 01",
            "Marine 02",
            "Marine 03",
            "Marine 04",
            "Marine 05",
            "Marine 06",
            "Marine 07",
            "Marine 08",
            "Marine 09",
            "Marine 10",
            "Marine 11",
            "Marine 12",
            "Marine 13",
            "Marine 14",
            "Marine 15",
            "Marine 16",
            "Marine 17",
            "Marine 18",
            "Siege Tank 01",
            "Siege Tank 02",
            "Siege Tank 03",
            "Siege Tank 04",
            "Sky Vessel 01",
            "Sky Vessel 02",
            "Sky Vessel 03",
            "Sky Vessel 04"
        };

        private static readonly string[] Group4Names = { "Field Barracks" };
        private const string ShowcaseRelationshipType = "ShowcaseAffinity";
        private const string ShowcaseRelationshipMetric = "Affinity";
        private const string ShowcaseRelationshipReason = "GenreInfoShowcase.Seed";

        private readonly GenreInfoShowcasePanelController _panelController;

        public GenreInfoShowcaseRuntime()
        {
            _panelController = new GenreInfoShowcasePanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!IsShowcaseMap(activeMapId))
            {
                CloseEntityInfoPanels(context);
                ClearPanelIfOwned(context);
                return Task.CompletedTask;
            }

            EnsurePresentationStableIds(engine);
            EnsureSeeded(engine);
            EnsureEntityInfoPanels(context, engine);
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value;
            if (!IsShowcaseMap(mapId))
            {
                return Task.CompletedTask;
            }

            CloseEntityInfoPanels(context);
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!IsShowcaseMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            EnsureInsightPanelTarget(engine);
            _panelController.MountOrRefresh(root, engine, activeMapId!);
        }

        public bool SetLocale(GameEngine engine, string localeKey)
        {
            if (engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection) is not PresentationTextLocaleSelection localeSelection)
            {
                return false;
            }

            return localeSelection.TrySetActiveLocale(localeKey);
        }

        public bool SaveControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity viewer))
            {
                return false;
            }

            bool saved = SelectionControlGroupRuntime.TrySaveViewedSelectionToGroup(
                engine.World,
                engine.GlobalContext,
                selection,
                viewer,
                groupIndex,
                mirrorToFormation: true);
            if (saved)
            {
                engine.GlobalContext[GenreInfoShowcaseIds.ActiveControlGroupKey] = groupIndex;
            }

            return saved;
        }

        public bool RecallControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity viewer))
            {
                return false;
            }

            bool recalled = SelectionControlGroupRuntime.TryRecallGroupToLive(
                engine.World,
                engine.GlobalContext,
                selection,
                viewer,
                groupIndex,
                mirrorToFormation: true);
            if (recalled)
            {
                engine.GlobalContext[GenreInfoShowcaseIds.ActiveControlGroupKey] = groupIndex;
            }

            return recalled;
        }

        public bool ShowLiveSelection(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity viewer))
            {
                return false;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            return true;
        }

        public bool ShowFormationSelection(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity viewer))
            {
                return false;
            }

            selection.TryGetOrCreateContainer(viewer, SelectionSetKeys.FormationPrimary, SelectionContainerKind.Formation, out _);
            selection.TryBindView(viewer, SelectionViewKeys.Formation, viewer, SelectionSetKeys.FormationPrimary);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Formation;
            return true;
        }

        private void EnsureSeeded(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!IsShowcaseMap(mapId))
            {
                return;
            }

            if (engine.GlobalContext.TryGetValue(GenreInfoShowcaseIds.SeededMapKey, out object? seededMapObj) &&
                seededMapObj is string seededMapId &&
                string.Equals(seededMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyShowcaseEntityState(engine.World);
            SeedShowcaseRelationships(engine);
            SeedSelectionGroups(engine);
            RecallControlGroup(engine, 3);
            ShowLiveSelection(engine);
            engine.GlobalContext[GenreInfoShowcaseIds.ActiveControlGroupKey] = 3;
            engine.GlobalContext[GenreInfoShowcaseIds.SeededMapKey] = mapId;
        }

        private static void ApplyShowcaseEntityState(World world)
        {
            SetEntityAttributes(world, "Governor Aurelia", ("Health", 100f, 82f), ("Gold", 180f, 145f), ("Production", 22f, 18f), ("TechProgress", 80f, 62f), ("FoodProduction", 14f, 12f));
            SetEntityTags(world, "Governor Aurelia", "Status.CanColonize");

            SetEntityAttributes(world, "Captain Nyx", ("Health", 920f, 640f), ("Energy", 450f, 310f), ("AttackSpeed", 115f, 115f));
            SetEntityTags(world, "Captain Nyx", "Cooldown.Skill.W");

            SetEntityAttributes(world, "Field Barracks", ("Health", 1000f, 760f));

            for (int i = 1; i <= 18; i++)
            {
                SetEntityAttributes(world, $"Marine {i:00}", ("Health", 40f, 24f + i), ("AttackSpeed", 100f, 100f));
            }

            for (int i = 1; i <= 4; i++)
            {
                SetEntityAttributes(world, $"Siege Tank {i:00}", ("Health", 150f, 112f + (i * 6)), ("Shield", 0f, 0f));
                SetEntityAttributes(world, $"Sky Vessel {i:00}", ("Health", 200f, 168f + (i * 8)), ("Energy", 200f, 120f + (i * 18)));
            }
        }

        private static void SetEntityAttributes(World world, string entityName, params (string Name, float Base, float Current)[] values)
        {
            Entity entity = FindNamedEntity(world, entityName);
            if (entity == Entity.Null)
            {
                return;
            }

            AttributeBuffer attributes = world.TryGet(entity, out AttributeBuffer existing) ? existing : new AttributeBuffer();
            for (int i = 0; i < values.Length; i++)
            {
                int attributeId = AttributeRegistry.Register(values[i].Name);
                attributes.SetBase(attributeId, values[i].Base);
                attributes.SetCurrent(attributeId, values[i].Current);
            }

            if (world.Has<AttributeBuffer>(entity))
            {
                world.Set(entity, attributes);
            }
            else
            {
                world.Add(entity, attributes);
            }
        }

        private static void SetEntityTags(World world, string entityName, params string[] tagNames)
        {
            Entity entity = FindNamedEntity(world, entityName);
            if (entity == Entity.Null)
            {
                return;
            }

            GameplayTagContainer tags = world.TryGet(entity, out GameplayTagContainer existing) ? existing : new GameplayTagContainer();
            tags.Clear();
            for (int i = 0; i < tagNames.Length; i++)
            {
                tags.AddTag(TagRegistry.Register(tagNames[i]));
            }

            if (world.Has<GameplayTagContainer>(entity))
            {
                world.Set(entity, tags);
            }
            else
            {
                world.Add(entity, tags);
            }
        }

        private static void SeedShowcaseRelationships(GameEngine engine)
        {
            RelationshipRuntime runtime = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("GenreInfoShowcase requires RelationshipRuntime.");
            RelationshipTypeRegistry types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("GenreInfoShowcase requires RelationshipTypeRegistry.");
            RelationshipMetricRegistry metrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("GenreInfoShowcase requires RelationshipMetricRegistry.");
            RelationshipReasonRegistry reasons = engine.GetService(CoreServiceKeys.RelationshipReasonRegistry)
                ?? throw new InvalidOperationException("GenreInfoShowcase requires RelationshipReasonRegistry.");

            int typeId = types.GetId(ShowcaseRelationshipType);
            int metricId = metrics.GetId(ShowcaseRelationshipMetric);
            if (!reasons.TryGetId(ShowcaseRelationshipReason, out int reasonId) || reasonId <= 0)
            {
                throw new InvalidOperationException($"GenreInfoShowcase requires relationship reason '{ShowcaseRelationshipReason}' to be registered.");
            }

            Entity viewer = EnsureSelectionViewer(engine);
            SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, "Governor Aurelia", 100);
            SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, "Captain Nyx", 0);
            SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, "Field Barracks", 100);

            for (int i = 1; i <= 18; i++)
            {
                SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, $"Marine {i:00}", 100);
            }

            for (int i = 1; i <= 4; i++)
            {
                SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, $"Siege Tank {i:00}", -100);
                SetAffinity(engine.World, runtime, typeId, metricId, reasonId, viewer, $"Sky Vessel {i:00}", 0);
            }
        }

        private static void SetAffinity(
            World world,
            RelationshipRuntime runtime,
            int typeId,
            int metricId,
            int reasonId,
            Entity source,
            string targetName,
            int value)
        {
            Entity target = FindNamedEntity(world, targetName);
            if (target != Entity.Null)
            {
                runtime.SetMetric(source, target, typeId, metricId, value, reasonId);
            }
        }

        private static void EnsurePresentationStableIds(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.PresentationStableIdAllocator) is not PresentationStableIdAllocator allocator)
            {
                return;
            }

            var query = new QueryDescription().WithAll<VisualTransform>();
            engine.World.Query(in query, (Entity entity, ref VisualTransform _) =>
            {
                if (engine.World.Has<PresentationStableId>(entity))
                {
                    return;
                }

                engine.World.Add(entity, new PresentationStableId { Value = allocator.Allocate() });
            });
        }

        private void SeedSelectionGroups(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity viewer))
            {
                return;
            }

            SeedGroup(engine.World, selection, viewer, 1, Group1Names);
            SeedGroup(engine.World, selection, viewer, 2, Group2Names);
            SeedGroup(engine.World, selection, viewer, 3, Group3Names);
            SeedGroup(engine.World, selection, viewer, 4, Group4Names);
        }

        private static void SeedGroup(World world, SelectionRuntime selection, Entity viewer, int groupIndex, IReadOnlyList<string> names)
        {
            var entities = new Entity[names.Count];
            int written = 0;
            for (int i = 0; i < names.Count; i++)
            {
                Entity entity = FindNamedEntity(world, names[i]);
                if (entity != Entity.Null)
                {
                    entities[written++] = entity;
                }
            }

            selection.ReplaceSelection(viewer, SelectionSetKeys.ControlGroup(groupIndex), entities.AsSpan(0, written));
        }

        private static Entity FindNamedEntity(World world, string entityName)
        {
            Entity match = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (match == Entity.Null &&
                    string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    match = entity;
                }
            });
            return match;
        }

        private static bool TryResolveSelectionContext(GameEngine engine, out SelectionRuntime selection, out Entity viewer)
        {
            selection = engine.GetService(CoreServiceKeys.SelectionRuntime)!;
            viewer = EnsureSelectionViewer(engine);
            return selection != null && viewer != Entity.Null && engine.World.IsAlive(viewer);
        }

        private static Entity EnsureSelectionViewer(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) &&
                viewerObj is Entity localViewer &&
                engine.World.IsAlive(localViewer))
            {
                return localViewer;
            }

            Entity viewer = engine.World.Create(new Name { Value = "GenreInfo Showcase Viewer" });
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = viewer;
            return viewer;
        }

        private static void EnsureEntityInfoPanels(ScriptContext context, GameEngine engine)
        {
            if (engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            EntityInfoPanelTarget? selectedTarget = TryResolveSelectedTarget(engine);
            if (!selectedTarget.HasValue)
            {
                CloseIfPresent(context, handles, GenreInfoShowcaseIds.InsightHandleKey);
                return;
            }

            OpenOrUpdate(
                context,
                handles,
                GenreInfoShowcaseIds.InsightHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.InsightBrief,
                    EntityInfoPanelSurface.Ui,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopRight, 16f, 16f, 484f, 636f),
                    EntityInfoGasDetailFlags.None,
                    true));
        }

        private static void EnsureInsightPanelTarget(GameEngine engine)
        {
            if (engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles ||
                !handles.TryGet(GenreInfoShowcaseIds.InsightHandleKey, out EntityInfoPanelHandle handle) ||
                engine.GetService(EntityInfoPanelServiceKeys.Service) is not EntityInfoPanelService service)
            {
                return;
            }

            EntityInfoPanelTarget? selectedTarget = TryResolveSelectedTarget(engine);
            if (selectedTarget.HasValue)
            {
                service.UpdateTarget(handle, selectedTarget.Value);
                service.SetVisible(handle, true);
            }
            else
            {
                service.SetVisible(handle, false);
            }
        }

        private static EntityInfoPanelTarget? TryResolveSelectedTarget(GameEngine engine)
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) ||
                selected == Entity.Null ||
                !engine.World.IsAlive(selected))
            {
                return null;
            }

            return EntityInfoPanelTarget.Fixed(selected);
        }

        private static bool OpenOrUpdate(ScriptContext context, EntityInfoPanelHandleStore handles, string handleKey, EntityInfoPanelRequest request)
        {
            if (handles.TryGet(handleKey, out _))
            {
                new UpdateEntityInfoPanelCommand
                {
                    HandleSlotKey = handleKey,
                    Visible = true,
                    Layout = request.Layout,
                    Target = request.Target,
                    GasDetailFlags = request.GasDetailFlags
                }.ExecuteAsync(context).GetAwaiter().GetResult();
                return false;
            }

            new OpenEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey,
                Request = request
            }.ExecuteAsync(context).GetAwaiter().GetResult();
            return true;
        }

        private static void CloseEntityInfoPanels(ScriptContext context)
        {
            if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseIfPresent(context, handles, GenreInfoShowcaseIds.InsightHandleKey);
        }

        private static void CloseIfPresent(ScriptContext context, EntityInfoPanelHandleStore handles, string handleKey)
        {
            if (!handles.TryGet(handleKey, out _))
            {
                return;
            }

            new CloseEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey
            }.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private static bool IsShowcaseMap(string? mapId) =>
            string.Equals(mapId, GenreInfoShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase);
    }
}
