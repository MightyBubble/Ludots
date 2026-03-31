using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using NarrativeShowcaseMod.Input;
using NarrativeShowcaseMod.Systems;
using NarrativeShowcaseMod.UI;

namespace NarrativeShowcaseMod.Runtime
{
    internal sealed class NarrativeShowcaseRuntime
    {
        private readonly IModContext _context;
        private readonly NarrativeShowcasePanelController _panelController;
        private bool _narrativeInputActive;
        private bool _interactionInputActive;

        internal NarrativeShowcaseRuntime(IModContext context)
        {
            _context = context;
            _panelController = new NarrativeShowcasePanelController();
        }

        public Task HandleGameStartAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue("NarrativeShowcase.SystemsInstalled", out var installedObj) && installedObj is bool installed && installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["NarrativeShowcase.SystemsInstalled"] = true;
            engine.RegisterSystem(new NarrativeShowcaseInteractionSystem(engine, this), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new NarrativeShowcasePanelPresentationSystem(engine, this));
            return Task.CompletedTask;
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            bool showcaseActive = string.Equals(activeMapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase);
            var input = context.Get(CoreServiceKeys.InputHandler);
            if (showcaseActive)
            {
                ActivateInputContexts(input);
                EnsureViewMode(engine);
                EnsureBootstrapped(engine);
                RebindEntities(engine);
                RefreshPanel(engine);
                engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = true;
            }
            else
            {
                DeactivateInputContexts(input);
                ClearPanelIfOwned(context);
                engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = false;
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value ?? string.Empty;
            if (!string.Equals(mapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            DeactivateInputContexts(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            engine.GlobalContext[NarrativeShowcaseIds.ActiveMapKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BootstrappedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = false;
            return Task.CompletedTask;
        }

        public Task HandleNarrativeSignalAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            string signalId = context.Get(NarrativeServiceKeys.SignalId) ?? string.Empty;
            if (string.Equals(signalId, NarrativeShowcaseIds.SpawnBeastSignal, StringComparison.OrdinalIgnoreCase))
            {
                SpawnBeast(engine);
            }
            else if (string.Equals(signalId, NarrativeShowcaseIds.RewardSignal, StringComparison.OrdinalIgnoreCase))
            {
                ApplyReward(engine);
            }

            return Task.CompletedTask;
        }

        public Task HandleCinematicCompletedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsShowcaseActive(engine))
            {
                return Task.CompletedTask;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return Task.CompletedTask;
            }

            string cinematicId = context.Get(NarrativeServiceKeys.CinematicId) ?? string.Empty;
            if (string.Equals(cinematicId, NarrativeShowcaseIds.IntroCinematicId, StringComparison.OrdinalIgnoreCase))
            {
                director.StartDialogue(NarrativeShowcaseIds.BriefingDialogueId);
            }
            else if (string.Equals(cinematicId, NarrativeShowcaseIds.TrialRevealCinematicId, StringComparison.OrdinalIgnoreCase))
            {
                director.EmitSignal(NarrativeShowcaseIds.SpawnBeastSignal);
            }

            return Task.CompletedTask;
        }

        internal void RefreshPanel(GameEngine engine)
        {
            if (!IsShowcaseActive(engine) || engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            RebindEntities(engine);
            _panelController.MountOrRefresh(root, engine);
        }

        internal void RebindEntities(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            TryRenameSpawnedBeast(engine);
            BindByName(engine, director, NarrativeShowcaseIds.PlayerAlias, NarrativeShowcaseIds.PlayerName);
            BindByName(engine, director, NarrativeShowcaseIds.ElderAlias, NarrativeShowcaseIds.ElderName);
            BindByName(engine, director, NarrativeShowcaseIds.ShrineAlias, NarrativeShowcaseIds.ShrineName);
            BindByName(engine, director, NarrativeShowcaseIds.BeastAlias, NarrativeShowcaseIds.BeastName);
        }

        internal bool IsShowcaseActive(GameEngine engine)
        {
            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            return string.Equals(activeMapId, NarrativeShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase);
        }

        internal bool BeastSpawned(GameEngine engine)
            => engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BeastSpawnedKey, out var value) && value is bool spawned && spawned;

        internal bool BeastDefeated(GameEngine engine)
            => engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BeastDefeatedKey, out var value) && value is bool defeated && defeated;

        internal void MarkBeastDefeated(GameEngine engine)
        {
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = true;
        }

        private void EnsureBootstrapped(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BootstrappedKey, out var bootObj) && bootObj is bool booted && booted)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            director.ResetState();
            RebindEntities(engine);
            director.StartQuest(NarrativeShowcaseIds.QuestId);
            director.StartCinematic(NarrativeShowcaseIds.IntroCinematicId);
            engine.GlobalContext[NarrativeShowcaseIds.BootstrappedKey] = true;
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.BeastDefeatedKey] = false;
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = false;
        }

        private void SpawnBeast(GameEngine engine)
        {
            if (BeastSpawned(engine) || engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue queue)
            {
                return;
            }

            queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "interaction_enemy_bruiser",
                MapId = new Ludots.Core.Map.MapId(NarrativeShowcaseIds.MapId),
                HasWorldPosition = 1,
                WorldPositionCm = Fix64Vec2.FromInt(1960, 940),
                HasFacing = 1,
                FacingAngleRad = 3.14159f
            });
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = true;
        }

        private void ApplyReward(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.RewardAppliedKey, out var rewardObj) && rewardObj is bool rewardApplied && rewardApplied)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue queue ||
                !TryFindEntityByName(engine.World, NarrativeShowcaseIds.PlayerName, out Entity player))
            {
                return;
            }

            int healEffectId = EffectTemplateIdRegistry.GetId("Effect.Narrative.BlessingHeal");
            int speedEffectId = EffectTemplateIdRegistry.GetId("Effect.Narrative.BlessingSpeed");
            if (healEffectId > 0)
            {
                queue.Publish(new EffectRequest { Source = player, Target = player, TemplateId = healEffectId });
            }

            if (speedEffectId > 0)
            {
                queue.Publish(new EffectRequest { Source = player, Target = player, TemplateId = speedEffectId });
            }

            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = true;
        }

        private void ActivateInputContexts(Ludots.Core.Input.Runtime.PlayerInputHandler input)
        {
            if (input == null)
            {
                return;
            }

            if (!_narrativeInputActive && input.HasContext(NarrativeShowcaseInputContexts.Showcase))
            {
                input.PushContext(NarrativeShowcaseInputContexts.Showcase);
                _narrativeInputActive = true;
            }

            if (!_interactionInputActive && input.HasContext(InteractionShowcaseIds.InputContextId))
            {
                input.PushContext(InteractionShowcaseIds.InputContextId);
                _interactionInputActive = true;
            }
        }

        private void DeactivateInputContexts(Ludots.Core.Input.Runtime.PlayerInputHandler input)
        {
            if (input == null)
            {
                return;
            }

            if (_interactionInputActive)
            {
                input.PopContext(InteractionShowcaseIds.InputContextId);
                _interactionInputActive = false;
            }

            if (_narrativeInputActive)
            {
                input.PopContext(NarrativeShowcaseInputContexts.Showcase);
                _narrativeInputActive = false;
            }
        }

        private void EnsureViewMode(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (!InteractionShowcaseIds.IsShowcaseMode(manager.ActiveMode?.Id))
            {
                manager.SwitchTo(InteractionShowcaseIds.LolModeId);
            }
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }
        }

        private void BindByName(GameEngine engine, NarrativeDirector director, string alias, string name)
        {
            if (TryFindEntityByName(engine.World, name, out Entity entity))
            {
                director.BindEntity(alias, entity);
            }
        }

        private void TryRenameSpawnedBeast(GameEngine engine)
        {
            if (TryFindEntityByName(engine.World, NarrativeShowcaseIds.BeastName, out _))
            {
                return;
            }

            if (TryFindEntityByName(engine.World, NarrativeShowcaseIds.SpawnedBeastTemplateName, out Entity entity) && engine.World.TryGet(entity, out Name name))
            {
                name.Value = NarrativeShowcaseIds.BeastName;
                engine.World.Set(entity, name);
            }
        }

        private static bool TryFindEntityByName(World world, string name, out Entity result)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found == Entity.Null && string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = entity;
                }
            });

            result = found;
            return found != Entity.Null;
        }
    }
}

