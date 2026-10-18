using Arch.System;
using AuditPlaygroundMod.Commands;
using AuditPlaygroundMod.Systems;
using AuditPlaygroundMod.Triggers;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AuditPlaygroundMod
{
    public sealed class AuditPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[AuditPlaygroundMod] Loaded.");

            context.OnEvent(GameEvents.MapLoaded, new AuditGlobalMapLoadedTrigger().ExecuteAsync);

            context.SystemFactoryRegistry.RegisterPresentation("AuditMapControlPresentation", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return new NoopSystem();
                return new AuditMapControlPresentationSystem(engine);
            });

            context.TriggerDecorators.Register<AuditScopedMapLoadedTrigger>(trigger =>
            {
                trigger.Priority = -20;
            });

            context.TriggerDecorators.Register("AuditScopedMapLoadedTrigger", trigger =>
            {
                trigger.AddAction(new IncrementGlobalCounterCommand("Audit.NamedDecoratorCount", "named decorator"));
            });

            context.TriggerDecorators.RegisterAnchor("audit.after_load",
                new IncrementGlobalCounterCommand("Audit.AnchorDecoratorCount", "anchor decorator"));
        }

        public void OnUnload()
        {
        }

        private sealed class NoopSystem : ISystem<float>
        {
            public void Initialize() { }
            public void BeforeUpdate(in float t) { }
            public void Update(in float t) { }
            public void AfterUpdate(in float t) { }
            public void Dispose() { }
        }
    }
}
