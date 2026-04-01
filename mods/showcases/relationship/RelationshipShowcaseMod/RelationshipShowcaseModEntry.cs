using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RelationshipShowcaseMod.Runtime;
using RelationshipShowcaseMod.Triggers;

namespace RelationshipShowcaseMod
{
    public sealed class RelationshipShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            var config = LoadConfig(context);
            var state = new RelationshipShowcaseScenarioState(config);
            state.FrontendConfig = LoadFrontendConfig(context);
            context.OnEvent(GameEvents.GameStart, new InstallRelationshipShowcaseOnGameStartTrigger(context, state, config).ExecuteAsync);
            context.OnEvent(new EventKey(config.Events.TrustedUnlocked), ctx => OnTrustedUnlockedAsync(ctx, state));
            context.OnEvent(new EventKey(config.Events.OathBondUnlocked), ctx => OnOathBondUnlockedAsync(ctx, state));
            context.OnEvent(new EventKey(config.Events.SynergyActivated), ctx => OnSynergyActivatedAsync(state));
            context.OnEvent(new EventKey(config.Events.FocusLocked), ctx => OnFocusLockedAsync(state));
        }

        public void OnUnload()
        {
        }

        private static RelationshipShowcaseConfig LoadConfig(IModContext context)
        {
            using var stream = context.GetResource($"{context.ModId}:assets/RelationshipShowcaseConfig.json");
            return RelationshipShowcaseConfig.Load(stream);
        }

        private static RelationshipShowcaseFrontendConfig LoadFrontendConfig(IModContext context)
        {
            using var stream = context.GetResource($"{context.ModId}:assets/Frontend/relationship_frontend.json");
            return RelationshipShowcaseFrontendConfig.Load(stream);
        }

        private static Task OnTrustedUnlockedAsync(ScriptContext context, RelationshipShowcaseScenarioState state)
        {
            if (context.GetEngine()?.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue queue)
            {
                return Task.CompletedTask;
            }

            var target = context.Get(CoreServiceKeys.RelationshipEventTarget);
            if (target == Arch.Core.Entity.Null)
            {
                return Task.CompletedTask;
            }

            queue.Publish(new EffectRequest
            {
                Source = target,
                Target = target,
                TargetContext = target,
                TemplateId = EffectTemplateIdRegistry.GetId(state.Config.Effects.TrustedGuard),
            });
            state.TrustedUnlocked = true;
            state.AddLog(state.Config.Logs.TriggerTrustedUnlocked);
            return Task.CompletedTask;
        }

        private static Task OnOathBondUnlockedAsync(ScriptContext context, RelationshipShowcaseScenarioState state)
        {
            if (context.GetEngine()?.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue queue)
            {
                return Task.CompletedTask;
            }

            var source = context.Get(CoreServiceKeys.RelationshipEventSource);
            var target = context.Get(CoreServiceKeys.RelationshipEventTarget);
            int templateId = EffectTemplateIdRegistry.GetId(state.Config.Effects.OathSpark);
            if (source != Arch.Core.Entity.Null)
            {
                queue.Publish(new EffectRequest { Source = source, Target = source, TargetContext = target, TemplateId = templateId });
            }

            if (target != Arch.Core.Entity.Null)
            {
                queue.Publish(new EffectRequest { Source = source, Target = target, TargetContext = source, TemplateId = templateId });
            }

            state.OathBondUnlocked = true;
            state.AddLog(state.Config.Logs.TriggerOathBondUnlocked);
            return Task.CompletedTask;
        }

        private static Task OnSynergyActivatedAsync(RelationshipShowcaseScenarioState state)
        {
            state.AddLog(state.Config.Logs.TriggerSynergyActivated);
            return Task.CompletedTask;
        }

        private static Task OnFocusLockedAsync(RelationshipShowcaseScenarioState state)
        {
            state.AddLog(state.Config.Logs.TriggerFocusLocked);
            return Task.CompletedTask;
        }
    }
}
