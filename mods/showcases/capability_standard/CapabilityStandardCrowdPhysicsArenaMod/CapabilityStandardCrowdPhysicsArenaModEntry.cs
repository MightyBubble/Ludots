using System;
using System.Threading.Tasks;
using CapabilityStandardCrowdPhysicsArenaMod.Runtime;
using CapabilityStandardCrowdPhysicsArenaMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;

namespace CapabilityStandardCrowdPhysicsArenaMod;

public sealed class CapabilityStandardCrowdPhysicsArenaModEntry : IMod
{
    private const string ObserverVisibilitySystemInstalledKey =
        "CapabilityStandardCrowdPhysicsArena.ObserverVisibilitySystemInstalled";
    private const string LocalOrderSourceSystemInstalledKey =
        "CapabilityStandardCrowdPhysicsArena.LocalOrderSourceSystemInstalled";
    private const string PressurePlateDoorSystemInstalledKey =
        "CapabilityStandardCrowdPhysicsArena.PressurePlateDoorSystemInstalled";
    private IModContext? _context;

    /// <summary>Queryable plate/door state for tests and HUD (installed once per engine).</summary>
    public static readonly ServiceKey<CrowdPhysicsArenaPressurePlateDoorSystem> PressurePlateDoorSystemKey =
        new("CapabilityStandardCrowdPhysicsArena.PressurePlateDoorSystem");

    public void OnLoad(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        context.Log("[CapabilityStandardCrowdPhysicsArenaMod] Loaded");
        CrowdPhysicsArenaComponentAuthoring.Register(context.ModId);
        context.OnEvent(GameEvents.GameStart, ConfigureArenaShowcaseAsync);
        context.OnEvent(GameEvents.MapLoaded, ConfigureArenaShowcaseAsync);
        context.OnEvent(GameEvents.MapResumed, ConfigureArenaShowcaseAsync);
    }

    public void OnUnload()
    {
    }

    private Task ConfigureArenaShowcaseAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        IModContext modContext = _context
            ?? throw new InvalidOperationException("CapabilityStandardCrowdPhysicsArenaMod requires IModContext.");
        EnsureObserverVisibilitySystem(engine);
        EnsureLocalOrderSourceSystem(engine, modContext);
        EnsurePressurePlateDoorAndHudSystems(engine);
        bool mapFocused = CapabilityStandardCrowdPhysicsArenaMapFocus.IsStartupMapFocused(engine);
        engine.SetService(CoreServiceKeys.PresentationAudienceRevealHidden, mapFocused);
        if (!mapFocused)
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimap)
        {
            minimap.Visible = true;
            minimap.SetRotateWithCamera(false);
            minimap.UseRtsFullMapPreset();
        }

        return Task.CompletedTask;
    }

    private static void EnsureObserverVisibilitySystem(GameEngine engine)
    {
        if (engine.GlobalContext.ContainsKey(ObserverVisibilitySystemInstalledKey))
        {
            return;
        }

        engine.RegisterSystem(
            new CrowdPhysicsArenaObserverVisibilityBindingSystem(engine),
            SystemGroup.RuntimeEntityBinding);
        engine.GlobalContext[ObserverVisibilitySystemInstalledKey] = true;
    }

    private static void EnsurePressurePlateDoorAndHudSystems(GameEngine engine)
    {
        if (engine.GlobalContext.ContainsKey(PressurePlateDoorSystemInstalledKey))
        {
            return;
        }

        ContactEventRouter2D router = engine.GetService(MovementPhysics2DBridgeKeys.ContactEventRouter)
            ?? throw new InvalidOperationException(
                "CapabilityStandardCrowdPhysicsArenaMod requires the massnav→kinematic bridge contact event router; " +
                "Physics2D + Ludots.Movement.Physics2DBridge must be installed.");

        var plateSystem = new CrowdPhysicsArenaPressurePlateDoorSystem(engine.World);
        router.RegisterConsumer(CrowdPhysicsArenaLayerNames.Plate, plateSystem);
        engine.RegisterSystem(plateSystem, SystemGroup.InputCollection);
        engine.SetService(PressurePlateDoorSystemKey, plateSystem);

        engine.RegisterSystem(new CrowdPhysicsArenaHudSystem(engine, plateSystem), SystemGroup.EventDispatch);
        engine.GlobalContext[PressurePlateDoorSystemInstalledKey] = true;
    }

    private static void EnsureLocalOrderSourceSystem(GameEngine engine, IModContext context)
    {
        if (engine.GlobalContext.ContainsKey(LocalOrderSourceSystemInstalledKey))
        {
            return;
        }

        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("CapabilityStandardCrowdPhysicsArenaMod requires OrderQueue.");
        engine.RegisterSystem(
            new CrowdPhysicsArenaLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, context),
            SystemGroup.InputCollection);
        engine.GlobalContext[LocalOrderSourceSystemInstalledKey] = true;
    }
}
