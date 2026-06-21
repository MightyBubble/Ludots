using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Scripting;
using ParticipantViewCapabilityMod.Runtime;

namespace ParticipantViewCapabilityMod;

public sealed class ParticipantViewCapabilityModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[ParticipantViewCapabilityMod] Loaded");
        var runtime = new ParticipantViewCapabilityRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            RelationshipTypeRegistry registry = ctx.GetEngine()?.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("ParticipantViewCapabilityMod requires RelationshipTypeRegistry.");
            registry.Register(ParticipantViewCapabilityIds.RelationshipType, isSymmetric: false);
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
