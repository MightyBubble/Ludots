using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineMod;

public sealed class RtsMultiplayerFrontlineModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        FrontlineAttributeRegistration.RegisterConfiguredAttributes(context);
        FrontlineComponentAuthoring.Register(context.ModId);
        var runtime = new FrontlineRuntime(context);
        var replication = new FrontlineReplicationLifecycle(runtime);
        context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
        context.OnEvent(GameEvents.GameStart, replication.HandleGameStartAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapLoaded, replication.HandleMapLoadedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, replication.HandleMapResumedAsync);
        context.OnEvent(GameEvents.MapUnloaded, replication.HandleMapUnloadedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        context.Log("[RtsMultiplayerFrontlineMod] Loaded");
    }

    public void OnUnload()
    {
    }
}

public static class FrontlineAttributeRegistration
{
    // AttributeRegistry freezes when engine init completes; the gameplay attribute names live in
    // this mod's config file, so read both names raw at load time and register them up front.
    public static void RegisterConfiguredAttributes(IModContext context)
    {
        using var stream = context.VFS.GetStream("RtsMultiplayerFrontlineMod:assets/RtsMultiplayerFrontlineConfig.json");
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        var root = doc.RootElement;
        Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(RequireText(root, "crystalAttribute"));
        Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(RequireText(root, "healthAttribute"));
    }

    private static string RequireText(System.Text.Json.JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new InvalidOperationException($"RtsMultiplayerFrontlineConfig.json requires string property '{property}'.");
        }

        return value.GetString()!;
    }
}
