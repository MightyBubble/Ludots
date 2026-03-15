using System;
using System.Threading.Tasks;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using InteractionShowcaseMod;
using Ludots.Core.Commands;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelShowcaseMod.Runtime
{
    internal sealed class EntityCommandPanelShowcaseRuntime
    {
        private const string GasSourceId = "gas.ability-slots";
        private const string ArcweaverAlias = "showcase.arcweaver";
        private const string VanguardAlias = "showcase.vanguard";
        private const string CommanderAlias = "showcase.commander";
        private const string FormsAlias = "showcase.forms";
        private const string FocusAlias = "showcase.focus";

        private Entity _lastFocusTarget = Entity.Null;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (!InteractionShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                DisableShowcase(context, engine);
                return Task.CompletedTask;
            }

            EnableShowcase(context, engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            DisableShowcase(context, engine);
            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (engine == null || !InteractionShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                _lastFocusTarget = Entity.Null;
                return;
            }

            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = true;

            Entity selected = engine.GetService(CoreServiceKeys.SelectedEntity);
            if (selected == Entity.Null || !engine.World.IsAlive(selected))
            {
                if (_lastFocusTarget != Entity.Null)
                {
                    var context = engine.CreateContext();
                    Execute(context, new SetEntityCommandPanelVisibilityCommand(FocusAlias, visible: false));
                    _lastFocusTarget = Entity.Null;
                }

                return;
            }

            if (_lastFocusTarget == selected)
            {
                return;
            }

            var updateContext = engine.CreateContext();
            Execute(updateContext, new SetEntityCommandPanelAnchorCommand(
                FocusAlias,
                new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.Center, 0f, -28f)));
            Execute(updateContext, new SetEntityCommandPanelSizeCommand(
                FocusAlias,
                new EntityCommandPanelSize(420f, 250f)));
            Execute(updateContext, new RebindEntityCommandPanelTargetCommand(FocusAlias, CoreServiceKeys.SelectedEntity.Name));
            Execute(updateContext, new SetEntityCommandPanelVisibilityCommand(FocusAlias, visible: true));
            _lastFocusTarget = selected;
        }

        private void EnableShowcase(ScriptContext context, GameEngine engine)
        {
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = true;
            CloseInteractionEntityInfoPanels(context);
            OpenPinnedPanels(context, engine);
            _lastFocusTarget = Entity.Null;
        }

        private void DisableShowcase(ScriptContext context, GameEngine engine)
        {
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = false;
            ClosePinnedPanels(context);
            _lastFocusTarget = Entity.Null;
        }

        private void OpenPinnedPanels(ScriptContext context, GameEngine engine)
        {
            ExecuteOpen(context, engine, ArcweaverAlias, InteractionShowcaseIds.ArcweaverName,
                new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.TopLeft, 16f, 64f),
                new EntityCommandPanelSize(340f, 280f),
                initialGroupIndex: 0,
                startVisible: true);

            ExecuteOpen(context, engine, VanguardAlias, InteractionShowcaseIds.VanguardName,
                new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomLeft, 16f, 16f),
                new EntityCommandPanelSize(340f, 280f),
                initialGroupIndex: 0,
                startVisible: true);

            ExecuteOpen(context, engine, CommanderAlias, InteractionShowcaseIds.CommanderName,
                new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.TopRight, 16f, 16f),
                new EntityCommandPanelSize(360f, 292f),
                initialGroupIndex: 0,
                startVisible: true);

            ExecuteOpen(context, engine, FormsAlias, InteractionShowcaseIds.ArcweaverFormsDemoName,
                new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 16f),
                new EntityCommandPanelSize(420f, 300f),
                initialGroupIndex: 2,
                startVisible: true);

            Entity focusTarget = ResolveTargetEntity(engine, InteractionShowcaseIds.ArcweaverName);
            if (focusTarget != Entity.Null)
            {
                context.Set("EntityCommandPanelShowcase.FocusTarget", focusTarget);
                Execute(context, new OpenEntityCommandPanelCommand(
                    FocusAlias,
                    "EntityCommandPanelShowcase.FocusTarget",
                    GasSourceId,
                    FocusAlias,
                    new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.Center, 0f, -28f),
                    new EntityCommandPanelSize(420f, 250f),
                    initialGroupIndex: 0,
                    startVisible: false));
            }
        }

        private static void ClosePinnedPanels(ScriptContext context)
        {
            Execute(context, new CloseEntityCommandPanelCommand(ArcweaverAlias));
            Execute(context, new CloseEntityCommandPanelCommand(VanguardAlias));
            Execute(context, new CloseEntityCommandPanelCommand(CommanderAlias));
            Execute(context, new CloseEntityCommandPanelCommand(FormsAlias));
            Execute(context, new CloseEntityCommandPanelCommand(FocusAlias));
        }

        private static void CloseInteractionEntityInfoPanels(ScriptContext context)
        {
            if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.ArcweaverOverlayHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.VanguardOverlayHandleKey);
        }

        private static void CloseEntityInfoHandle(
            ScriptContext context,
            EntityInfoPanelHandleStore handles,
            string handleKey)
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

        private static void ExecuteOpen(
            ScriptContext context,
            GameEngine engine,
            string alias,
            string entityName,
            EntityCommandPanelAnchor anchor,
            EntityCommandPanelSize size,
            int initialGroupIndex,
            bool startVisible)
        {
            Entity target = ResolveTargetEntity(engine, entityName);
            if (target == Entity.Null)
            {
                return;
            }

            string contextKey = $"EntityCommandPanelShowcase.Target.{alias}";
            context.Set(contextKey, target);
            Execute(context, new OpenEntityCommandPanelCommand(
                alias,
                contextKey,
                GasSourceId,
                alias,
                anchor,
                size,
                initialGroupIndex,
                startVisible));
        }

        private static Entity ResolveTargetEntity(GameEngine engine, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Ludots.Core.Components.Name>();
            engine.World.Query(in query, (Entity entity, ref Ludots.Core.Components.Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static void Execute(ScriptContext context, GameCommand command)
        {
            command.ExecuteAsync(context).GetAwaiter().GetResult();
        }
    }
}
