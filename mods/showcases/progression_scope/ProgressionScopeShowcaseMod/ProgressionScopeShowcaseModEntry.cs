using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Modding;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ProgressionScopeShowcaseMod
{
    public sealed class ProgressionScopeShowcaseModEntry : IMod
    {
        private const string MapId = "progression_scope_showcase";
        private const string DefaultSelectionName = "Chang An Barracks";

        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.MapLoaded, InstallShowcasePanelAsync);
            context.Log("[ProgressionScopeShowcaseMod] Loaded - entity-scoped progression tree showcase");
        }

        public void OnUnload()
        {
        }

        private static Task InstallShowcasePanelAsync(ScriptContext scriptContext)
        {
            GameEngine? engine = scriptContext.GetEngine();
            if (engine == null ||
                !string.Equals(engine.CurrentMapSession?.MapConfig?.Id, MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            Entity target = FindEntityByName(engine.World, DefaultSelectionName);
            if (target == Entity.Null)
            {
                return Task.CompletedTask;
            }

            BindCommandSource(engine, target);
            OpenCommandPanel(engine, target);
            return Task.CompletedTask;
        }

        private static void BindCommandSource(GameEngine engine, Entity target)
        {
            EntityCollectionStore? collections = engine.GetService(CoreServiceKeys.EntityCollectionStore);
            if (collections == null)
            {
                return;
            }

            Entity owner = RequireSolePossessedRep(engine);
            Span<Entity> selected = stackalloc Entity[1];
            selected[0] = target;
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                target,
                "Progression scope command source",
                "Default entity for progression showcase commands.");
            collections.Replace(owner, descriptor, selected, owner);
        }

        private static void OpenCommandPanel(GameEngine engine, Entity target)
        {
            IEntityCommandPanelService? panels = engine.GetService(CoreServiceKeys.EntityCommandPanelService);
            if (panels == null)
            {
                return;
            }

            panels.Open(new EntityCommandPanelOpenRequest
            {
                TargetEntity = target,
                SourceId = "gas.ability-slots",
                InstanceKey = "progression-scope-showcase",
                Anchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomRight, -28f, -28f),
                Size = new EntityCommandPanelSize(420f, 240f),
                LayoutPreset = EntityCommandPanelLayoutPreset.CommandDeck,
                InitialGroupIndex = 0,
                StartVisible = true
            });
        }

        private static Entity RequireSolePossessedRep(GameEngine engine)
        {
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "ProgressionScope showcase requires a live sole ClientLocalSeat possession from map launch / startupLocalSeats.");
            }

            return owner;
        }

        private static Entity FindEntityByName(World world, string value)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null &&
                    string.Equals(name.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            return result;
        }
    }
}
