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
        // 竞技场 Q/E 技能通过按键施放（input mapping），技能栏 overlay 是纯显示且无点击交互，
        // 在竞技场里没有信息增益——显式关闭（CoreInputMod.SkillBarEnabled）。
        engine.GlobalContext["CoreInputMod.SkillBarEnabled"] = false;
        EnsureObserverVisibilitySystem(engine);
        EnsureLocalOrderSourceSystem(engine, modContext);
        EnsurePressurePlateDoorSystem(engine);
        bool mapFocused = CapabilityStandardCrowdPhysicsArenaMapFocus.IsStartupMapFocused(engine);
        engine.SetService(CoreServiceKeys.PresentationAudienceRevealHidden, mapFocused);
        if (!mapFocused)
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimap)
        {
            // 竞技场是 96 单位的小范围场地（约 2400-7600cm），一屏即可看全，
            // minimap 没有信息增益（它是 10k 大世界 showcase 的标配）。
            minimap.Visible = false;
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




    private static void EnsurePressurePlateDoorSystem(GameEngine engine)
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
