using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;

namespace ResponseChainShowcaseMod.Runtime
{
    internal sealed class ResponseChainShowcaseRuntime
    {
        private const int OverlayPanelWidth = 520;
        private const int OverlayPanelHeight = 248;

        private readonly Dictionary<string, ShowcaseSnapshot> _snapshots = new(StringComparer.Ordinal);
        private string _lastMapId = string.Empty;
        private bool _selectionSeeded;
        private bool _listenersInstalled;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is GameEngine engine)
            {
                EnsureScenarioState(engine);
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (ResponseChainShowcaseIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
            {
                Disable();
            }

            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (!ResponseChainShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable();
                return;
            }

            EnsureScenarioState(engine);
            if (ConsumeResetRequest(engine))
            {
                ResetScenario(engine);
            }

            DrawOverlay(engine);
        }

        private void EnsureScenarioState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.Equals(_lastMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                _lastMapId = mapId;
                _selectionSeeded = false;
                _listenersInstalled = false;
                _snapshots.Clear();
            }

            CaptureSnapshots(engine);
            EnsureInitialSelection(engine);
            EnsureResponseListeners(engine);
        }

        private void CaptureSnapshots(GameEngine engine)
        {
            CaptureSnapshot(engine, ResponseChainShowcaseIds.ConductorName);
            CaptureSnapshot(engine, ResponseChainShowcaseIds.ComboRaiderName);
            CaptureSnapshot(engine, ResponseChainShowcaseIds.CounterRaiderName);
            CaptureSnapshot(engine, ResponseChainShowcaseIds.ScholarName);
            CaptureSnapshot(engine, ResponseChainShowcaseIds.ProtectorName);
        }

        private void CaptureSnapshot(GameEngine engine, string name)
        {
            if (_snapshots.ContainsKey(name))
            {
                return;
            }

            Entity entity = FindEntityByName(engine.World, name);
            if (entity == Entity.Null)
            {
                return;
            }

            int healthId = AttributeRegistry.GetId("Health");
            float health = 0f;
            if (healthId >= 0 && engine.World.Has<AttributeBuffer>(entity))
            {
                health = engine.World.Get<AttributeBuffer>(entity).GetCurrent(healthId);
            }

            Fix64Vec2 position = engine.World.Has<WorldPositionCm>(entity)
                ? engine.World.Get<WorldPositionCm>(entity).Value
                : default;

            _snapshots[name] = new ShowcaseSnapshot(position, health);
        }

        private void EnsureInitialSelection(GameEngine engine)
        {
            if (_selectionSeeded)
            {
                return;
            }

            Entity conductor = FindEntityByName(engine.World, ResponseChainShowcaseIds.ConductorName);
            if (conductor == Entity.Null)
            {
                return;
            }

            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = conductor;

            SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
            if (selection != null)
            {
                Span<Entity> one = stackalloc Entity[1];
                one[0] = conductor;
                selection.ReplaceSelection(conductor, SelectionSetKeys.Ambient, one);
                selection.TryBindView(conductor, SelectionViewKeys.Primary, conductor, SelectionSetKeys.Ambient);
                engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = conductor;
                engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            }

            _selectionSeeded = true;
        }

        private void EnsureResponseListeners(GameEngine engine)
        {
            if (_listenersInstalled)
            {
                return;
            }

            Entity conductor = FindEntityByName(engine.World, ResponseChainShowcaseIds.ConductorName);
            Entity scholar = FindEntityByName(engine.World, ResponseChainShowcaseIds.ScholarName);
            if (conductor == Entity.Null || scholar == Entity.Null)
            {
                return;
            }

            int comboTagId = TagRegistry.GetId(ResponseChainShowcaseIds.ComboOpenerEffect);
            int counterTagId = TagRegistry.GetId(ResponseChainShowcaseIds.CounterSwingEffect);
            int redirectTagId = TagRegistry.GetId(ResponseChainShowcaseIds.RedirectBoltEffect);
            int comboFollowUpEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.ComboFollowUpEffect);
            int counterTakeHitEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.CounterTakeHitEffect);
            int counterRiposteEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.CounterRiposteEffect);
            int counterFlourishEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.CounterFlourishEffect);
            int redirectHitScholarEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.RedirectHitScholarEffect);
            int redirectEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.RedirectToGuardEffect);
            int redirectFlourishEffectId = EffectTemplateIdRegistry.GetId(ResponseChainShowcaseIds.RedirectFlourishEffect);
            if (comboTagId <= 0 || counterTagId <= 0 || redirectTagId <= 0 ||
                comboFollowUpEffectId <= 0 || counterTakeHitEffectId <= 0 || counterRiposteEffectId <= 0 ||
                counterFlourishEffectId <= 0 || redirectHitScholarEffectId <= 0 || redirectEffectId <= 0 ||
                redirectFlourishEffectId <= 0)
            {
                return;
            }

            var conductorListener = new ResponseChainListener();
            conductorListener.Add(comboTagId, ResponseType.PromptInput, priority: 100, effectTemplateId: comboFollowUpEffectId);
            conductorListener.Add(counterTagId, ResponseType.Chain, priority: 30, effectTemplateId: counterRiposteEffectId);
            conductorListener.Add(counterTagId, ResponseType.Chain, priority: 20, effectTemplateId: counterTakeHitEffectId);
            conductorListener.Add(counterTagId, ResponseType.PromptInput, priority: 100, effectTemplateId: counterFlourishEffectId);
            WriteListener(engine.World, conductor, conductorListener);

            var scholarListener = new ResponseChainListener();
            scholarListener.Add(redirectTagId, ResponseType.Chain, priority: 30, effectTemplateId: redirectEffectId);
            scholarListener.Add(redirectTagId, ResponseType.Chain, priority: 20, effectTemplateId: redirectHitScholarEffectId);
            scholarListener.Add(redirectTagId, ResponseType.PromptInput, priority: 100, effectTemplateId: redirectFlourishEffectId);
            WriteListener(engine.World, scholar, scholarListener);

            _listenersInstalled = true;
        }

        private void ResetScenario(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ResponseChainUiState) is ResponseChainUiState uiState &&
                uiState.Visible)
            {
                return;
            }

            int healthId = AttributeRegistry.GetId("Health");
            foreach ((string name, ShowcaseSnapshot snapshot) in _snapshots)
            {
                Entity entity = FindEntityByName(engine.World, name);
                if (entity == Entity.Null)
                {
                    continue;
                }

                if (engine.World.Has<WorldPositionCm>(entity))
                {
                    var position = engine.World.Get<WorldPositionCm>(entity);
                    position.Value = snapshot.Position;
                    engine.World.Set(entity, position);
                }

                if (healthId >= 0 && engine.World.Has<AttributeBuffer>(entity))
                {
                    var attributes = engine.World.Get<AttributeBuffer>(entity);
                    attributes.SetBase(healthId, snapshot.Health);
                    attributes.SetCurrent(healthId, snapshot.Health);
                    engine.World.Set(entity, attributes);
                }

                if (engine.World.Has<OrderBuffer>(entity))
                {
                    engine.World.Set(entity, OrderBuffer.CreateEmpty());
                }
            }
        }

        private void DrawOverlay(GameEngine engine)
        {
            ScreenOverlayBuffer? overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            if (overlay == null)
            {
                return;
            }

            float comboCurrent = ReadHealth(engine, ResponseChainShowcaseIds.ComboRaiderName);
            float comboBase = ReadBaseHealth(ResponseChainShowcaseIds.ComboRaiderName);
            float counterCurrent = ReadHealth(engine, ResponseChainShowcaseIds.CounterRaiderName);
            float counterBase = ReadBaseHealth(ResponseChainShowcaseIds.CounterRaiderName);
            float conductorCurrent = ReadHealth(engine, ResponseChainShowcaseIds.ConductorName);
            float conductorBase = ReadBaseHealth(ResponseChainShowcaseIds.ConductorName);
            float scholarCurrent = ReadHealth(engine, ResponseChainShowcaseIds.ScholarName);
            float scholarBase = ReadBaseHealth(ResponseChainShowcaseIds.ScholarName);
            float protectorCurrent = ReadHealth(engine, ResponseChainShowcaseIds.ProtectorName);
            float protectorBase = ReadBaseHealth(ResponseChainShowcaseIds.ProtectorName);

            string comboStatus = comboCurrent < comboBase ? "Resolved" : "Ready";
            string counterStatus = counterCurrent < counterBase && conductorCurrent >= conductorBase ? "Resolved" : "Ready";
            string redirectStatus = protectorCurrent < protectorBase && scholarCurrent >= scholarBase ? "Resolved" : "Ready";
            bool windowOpen = engine.GetService(CoreServiceKeys.ResponseChainUiState) is ResponseChainUiState uiState && uiState.Visible;

            Vector4 panelFill = new(0.04f, 0.07f, 0.09f, 0.88f);
            Vector4 panelBorder = new(0.34f, 0.58f, 0.79f, 0.95f);
            Vector4 title = new(0.97f, 0.84f, 0.42f, 1f);
            Vector4 text = new(0.91f, 0.95f, 0.99f, 1f);
            Vector4 hint = new(0.73f, 0.83f, 0.92f, 1f);
            Vector4 good = new(0.48f, 0.94f, 0.58f, 1f);

            int x = 18;
            int y = 96;
            overlay.AddRect(x, y, OverlayPanelWidth, OverlayPanelHeight, panelFill, panelBorder, stableId: 48100, dirtySerial: 1);
            overlay.AddText(x + 16, y + 18, "Response Chain Showcase", 22, title, stableId: 48101, dirtySerial: 1);
            overlay.AddText(x + 16, y + 48, "Q -> 1 -> Space -> Space | W/E -> N -> Space -> Space | F4 reset", 15, text, stableId: 48102, dirtySerial: 1);
            overlay.AddText(x + 16, y + 72, "Window: 1 adds the combo follow-up, N negates the latest branch, double-Space resolves the window", 14, hint, stableId: 48103, dirtySerial: 1);
            overlay.AddText(x + 16, y + 102, $"Combo Drill [{comboStatus}]  target={ResponseChainShowcaseIds.ComboRaiderName}  hp {comboCurrent:0}/{comboBase:0}", 14, comboStatus == "Resolved" ? good : text, stableId: 48104, dirtySerial: 1);
            overlay.AddText(x + 16, y + 126, $"Counter Drill [{counterStatus}]  raider {counterCurrent:0}/{counterBase:0}  conductor {conductorCurrent:0}/{conductorBase:0}", 14, counterStatus == "Resolved" ? good : text, stableId: 48105, dirtySerial: 1);
            overlay.AddText(x + 16, y + 150, $"Redirect Drill [{redirectStatus}]  scholar {scholarCurrent:0}/{scholarBase:0}  protector {protectorCurrent:0}/{protectorBase:0}", 14, redirectStatus == "Resolved" ? good : text, stableId: 48106, dirtySerial: 1);
            overlay.AddText(x + 16, y + 182, "Play loop: click Conductor, hover marked ally/enemy, press ability, then answer the window.", 14, hint, stableId: 48107, dirtySerial: 1);
            overlay.AddText(x + 16, y + 206, windowOpen ? "Response window open." : "Response window idle.", 14, windowOpen ? title : hint, stableId: 48108, dirtySerial: 1);
        }

        private float ReadHealth(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            if (entity == Entity.Null || !engine.World.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            int healthId = AttributeRegistry.GetId("Health");
            return healthId >= 0 ? engine.World.Get<AttributeBuffer>(entity).GetCurrent(healthId) : 0f;
        }

        private float ReadBaseHealth(string name)
        {
            return _snapshots.TryGetValue(name, out ShowcaseSnapshot snapshot) ? snapshot.Health : 0f;
        }

        private static void WriteListener(World world, Entity entity, ResponseChainListener listener)
        {
            if (world.Has<ResponseChainListener>(entity))
            {
                world.Set(entity, listener);
                return;
            }

            world.Add(entity, listener);
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found != Entity.Null || !string.Equals(entityName.Value, name, StringComparison.Ordinal))
                {
                    return;
                }

                found = entity;
            });
            return found;
        }

        private static bool ConsumeResetRequest(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(ResponseChainShowcaseIds.ResetRequestKey, out object? requestedObj) ||
                requestedObj is not bool requested ||
                !requested)
            {
                return false;
            }

            engine.GlobalContext.Remove(ResponseChainShowcaseIds.ResetRequestKey);
            return true;
        }

        private void Disable()
        {
            _lastMapId = string.Empty;
            _selectionSeeded = false;
            _listenersInstalled = false;
            _snapshots.Clear();
        }

        private readonly record struct ShowcaseSnapshot(Fix64Vec2 Position, float Health);
    }
}
