using System;
using System.Threading.Tasks;
using CapabilityStandardCrowdPhysicsArenaMod.Runtime;
using CapabilityStandardCrowdPhysicsArenaMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.MassNavigation;
using Ludots.Core.Scripting;

namespace CapabilityStandardCrowdPhysicsArenaMod;

public sealed class CapabilityStandardCrowdPhysicsArenaModEntry : IMod
{
    private const string PressurePlateDoorSystemInstalledKey =
        "CapabilityStandardCrowdPhysicsArena.PressurePlateDoorSystemInstalled";

    /// <summary>Queryable plate/door state for tests and HUD (installed once per engine).</summary>
    public static readonly ServiceKey<CrowdPhysicsArenaPressurePlateDoorSystem> PressurePlateDoorSystemKey =
        new("CapabilityStandardCrowdPhysicsArena.PressurePlateDoorSystem");

    public void OnLoad(IModContext context)
    {
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

        // 竞技场 Q/E 技能通过按键施放（input mapping），技能栏 overlay 是纯显示且无点击交互，
        // 在竞技场里没有信息增益——显式关闭（CoreInputMod.SkillBarEnabled）。
        engine.GlobalContext["CoreInputMod.SkillBarEnabled"] = false;
        EnsureMassNavigationObserverDisclosure(engine);
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

    private static void EnsureMassNavigationObserverDisclosure(GameEngine engine)
    {
        MassNavigationPresentationAdapterInstaller.EnsureLocalObserverDisclosure(engine);
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
}
