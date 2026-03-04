using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Triggers;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Scripting;
using MobaDemoMod.Presentation;

namespace MobaDemoMod.Triggers
{
    public sealed class InstallMobaDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "MobaDemoMod.Installed";
        public const string MobaConfigKey = "MobaDemoMod.Config";

        private readonly IModContext _ctx;

        public InstallMobaDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;

            // Load MobaConfig via VFS
            var mobaConfig = MobaConfig.Load(_ctx);
            engine.GlobalContext[MobaConfigKey] = mobaConfig;
            _ctx.Log("[MobaDemoMod] MobaConfig loaded from assets/Configs/moba_config.json");

            // GameConfig is required — it must be loaded before GameStart
            var config = (GameConfig)engine.GlobalContext[ContextKeys.GameConfig];
            _ = config.Constants.OrderTags["stop"];

            if (engine.GlobalContext.TryGetValue(ContextKeys.OrderQueue, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                _ctx.Log("[MobaDemoMod] OrderQueue ready, registering local order source.");
                engine.RegisterPresentationSystem(new MobaLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx));
            }
            engine.RegisterPresentationSystem(new MobaSkillDemoPresentationSystem(engine.World, engine.GlobalContext, _ctx));

            if (engine.GlobalContext.TryGetValue(ContextKeys.OrderTypeRegistry, out var registryObj) &&
                registryObj is OrderTypeRegistry orderTypeRegistry)
            {
                engine.RegisterSystem(new Ludots.Core.Gameplay.GAS.Systems.StopOrderSystem(engine.World, orderTypeRegistry), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new Ludots.Core.Gameplay.GAS.Systems.AbilityMoveWorldCmSystem(engine.World, engine.EventBus, mobaConfig.Movement.SpeedCmPerSec, mobaConfig.Movement.StopRadiusCm), SystemGroup.AbilityActivation);
            }

            // ── 选择系统回调（CoreInputMod 已注册系统，此处注入 MOBA 视觉反馈）──
            PresentationCommandBuffer? cmdBuffer = null;
            if (engine.GlobalContext.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer pcb)
                cmdBuffer = pcb;

            if (engine.GlobalContext.TryGetValue(InstallCoreInputOnGameStartTrigger.EntitySelectionCallbacksKey, out var selObj) &&
                selObj is List<System.Action<WorldCmInt2, Entity>> selectionCallbacks)
            {
                var capturedCmdBuffer = cmdBuffer;
                selectionCallbacks.Add((worldCm, entity) =>
                {
                    if (capturedCmdBuffer == null) return;
                    capturedCmdBuffer.TryAdd(new PresentationCommand
                    {
                        Kind = PresentationCommandKind.DestroyPerformerScope,
                        IdA = mobaConfig.Presentation.SelectionScopeId
                    });
                    if (engine.World.IsAlive(entity))
                    {
                        capturedCmdBuffer.TryAdd(new PresentationCommand
                        {
                            Kind = PresentationCommandKind.CreatePerformer,
                            IdA = mobaConfig.Presentation.SelectionIndicatorDefId,
                            IdB = mobaConfig.Presentation.SelectionScopeId,
                            Source = entity
                        });
                    }
                });
            }
            else
            {
                _ctx.Log("[MobaDemoMod] CoreInput selection callback bus missing; selection indicator callback not installed.");
            }

            // 单位主体渲染由 VisualModelPrimitiveEmitSystem 负责。
            // 这里仅注入选择与演示相关表现回调。

            var session = context.Get<Ludots.Core.Gameplay.GameSession>(ContextKeys.GameSession);
            if (session != null && session.Camera.Controller == null)
            {
                engine.GlobalContext[ContextKeys.CameraControllerRequest] = new CameraControllerRequest
                {
                    Id = CameraControllerIds.Orbit3C,
                    Config = new Orbit3CCameraConfig
                    {
                        EnablePan = false,
                        ZoomCmPerWheel = mobaConfig.Camera.ZoomCmPerWheel,
                        RotateDegPerSecond = mobaConfig.Camera.RotateDegPerSecond,
                        MinDistanceCm = mobaConfig.Camera.MinDistanceCm,
                        MaxDistanceCm = mobaConfig.Camera.MaxDistanceCm
                    }
                };
            }

            return Task.CompletedTask;
        }
    }
}
