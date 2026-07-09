using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using GenreInfoShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) ||
                !TryResolveActiveCollection(engine, collections, viewer, out EntityCollectionHandle activeHandle, out EntityCollectionView activeView))
            {
                return false;
            }

            var members = new Entity[activeView.Count];
            int written = collections.CopyEntities(activeHandle, 0, members);
            if (written != members.Length)
            {
                Array.Resize(ref members, written);
            }

            ReplaceCollection(
                collections,
                viewer,
                ControlGroupCollectionKey(groupIndex),
                EntityCollectionRoleKind.Display,
                members,
                $"Control group {groupIndex}");
            engine.GlobalContext[GenreInfoShowcaseIds.ActiveControlGroupKey] = groupIndex;
            return true;
        }

        public bool RecallControlGroup(GameEngine engine, int groupIndex)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) ||
                !collections.TryGet(viewer, ControlGroupCollectionKey(groupIndex), out EntityCollectionHandle groupHandle) ||
                !collections.TryGetView(groupHandle, out EntityCollectionView groupView))
            {
                return false;
            }

            var members = new Entity[groupView.Count];
            int written = collections.CopyEntities(groupHandle, 0, members);
            if (written != members.Length)
            {
                Array.Resize(ref members, written);
            }

            ReplaceCommandSource(collections, viewer, members, $"Recall control group {groupIndex}");
            ReplaceCollection(
                collections,
                viewer,
                GenreInfoShowcaseIds.FormationCollectionKey,
                EntityCollectionRoleKind.Display,
                members,
                "Formation view");
            engine.GlobalContext[GenreInfoShowcaseIds.ActiveCollectionKey] = EntityCollectionKeys.CommandSource;
            engine.GlobalContext[GenreInfoShowcaseIds.ActiveControlGroupKey] = groupIndex;
            return true;
        }

        public bool ShowLiveSelection(GameEngine engine)
        {
            if (!TryResolveCollectionContext(engine, out _, out _))
            {
                return false;
            }

            engine.GlobalContext[GenreInfoShowcaseIds.ActiveCollectionKey] = EntityCollectionKeys.CommandSource;
            return true;
        }

        public bool ShowFormationSelection(GameEngine engine)
        {
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer))
            {
                return false;
            }

            if (!collections.TryGetView(viewer, GenreInfoShowcaseIds.FormationCollectionKey, out _))
            {
                Entity[] members = SnapshotCollection(collections, viewer, EntityCollectionKeys.CommandSource);
                ReplaceCollection(
                    collections,
                    viewer,
                    GenreInfoShowcaseIds.FormationCollectionKey,
                    EntityCollectionRoleKind.Display,
                    members,
                    "Formation view");
            }

            engine.GlobalContext[GenreInfoShowcaseIds.ActiveCollectionKey] = GenreInfoShowcaseIds.FormationCollectionKey;
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
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer))
            {
                return;
            }

            SeedGroup(engine.World, collections, viewer, 1, Group1Names);
            SeedGroup(engine.World, collections, viewer, 2, Group2Names);
            SeedGroup(engine.World, collections, viewer, 3, Group3Names);
            SeedGroup(engine.World, collections, viewer, 4, Group4Names);
        }

        private static void SeedGroup(World world, EntityCollectionStore collections, Entity viewer, int groupIndex, IReadOnlyList<string> names)
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

            ReplaceCollection(
                collections,
                viewer,
                ControlGroupCollectionKey(groupIndex),
                EntityCollectionRoleKind.Display,
                entities.AsSpan(0, written),
                $"Control group {groupIndex}");
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

        private static bool TryResolveCollectionContext(GameEngine engine, out EntityCollectionStore collections, out Entity viewer)
        {
            collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)!;
            viewer = EnsureCollectionViewer(engine);
            return collections != null && viewer != Entity.Null && engine.World.IsAlive(viewer);
        }

        private static Entity EnsureCollectionViewer(GameEngine engine)
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

        private static bool TryResolveActiveCollection(
            GameEngine engine,
            EntityCollectionStore collections,
            Entity viewer,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            handle = EntityCollectionHandle.Invalid;
            view = default;
            string key = ResolveActiveCollectionKey(engine);
            return collections.TryGet(viewer, key, out handle) &&
                   collections.TryGetView(handle, out view);
        }

        private static string ResolveActiveCollectionKey(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(GenreInfoShowcaseIds.ActiveCollectionKey, out object? keyObj) &&
                   keyObj is string key &&
                   !string.IsNullOrWhiteSpace(key)
                ? key
                : EntityCollectionKeys.CommandSource;
        }

        private static Entity[] SnapshotCollection(EntityCollectionStore collections, Entity viewer, string key)
        {
            if (!collections.TryGet(viewer, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return Array.Empty<Entity>();
            }

            var members = new Entity[view.Count];
            int written = collections.CopyEntities(handle, 0, members);
            if (written != members.Length)
            {
                Array.Resize(ref members, written);
            }

            return members;
        }

        private static void ReplaceCommandSource(
            EntityCollectionStore collections,
            Entity viewer,
            ReadOnlySpan<Entity> members,
            string summary)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                viewer,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                summary);
            collections.Replace(viewer, descriptor, members, viewer);
        }

        private static void ReplaceCollection(
            EntityCollectionStore collections,
            Entity viewer,
            string key,
            EntityCollectionRoleKind role,
            ReadOnlySpan<Entity> members,
            string title)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                key,
                EntityCollectionSourceKind.Explicit,
                role,
                viewer,
                members.Length > 0 ? members[0] : Entity.Null,
                title,
                $"{members.Length} entity(s)");
            collections.Replace(viewer, descriptor, members, viewer);
        }

        private static string ControlGroupCollectionKey(int groupIndex) =>
            $"{GenreInfoShowcaseIds.ControlGroupCollectionPrefix}{groupIndex}";

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
            if (!TryResolveCollectionContext(engine, out EntityCollectionStore collections, out Entity viewer) ||
                !TryResolveActiveCollection(engine, collections, viewer, out EntityCollectionHandle activeHandle, out _) ||
                !collections.TryGetEntityAt(activeHandle, 0, out Entity selected) ||
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
