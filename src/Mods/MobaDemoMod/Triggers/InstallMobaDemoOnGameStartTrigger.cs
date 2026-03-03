using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Systems;
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

            // ── 选择系统 ──
            // PresentationCommandBuffer 用于实体锚点的 Performer 生命周期管理
            PresentationCommandBuffer cmdBuffer = null;
            if (engine.GlobalContext.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer pcb)
                cmdBuffer = pcb;

            var clickSelect = new EntityClickSelectSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            // 选中指示器：通过 Performer 直接 API 管理（定义 5002）
            var capturedCmdBuffer = cmdBuffer;
            clickSelect.OnEntitySelected = (worldCm, entity) =>
            {
                if (capturedCmdBuffer == null) return;
                // 销毁旧选中 scope
                capturedCmdBuffer.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.DestroyPerformerScope,
                    IdA = mobaConfig.Presentation.SelectionScopeId
                });
                // 若选中了有效实体，创建新指示器
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
            };
            engine.RegisterPresentationSystem(clickSelect);
            
            var gasSelection = new GasSelectionResponseSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            engine.RegisterPresentationSystem(gasSelection);

            // Core 通用输入响应系统
            engine.RegisterPresentationSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext));

            // 单位渲染由 performers.json 定义 5001（entity-scoped Marker3D）驱动
            // 团队颜色由 EntityColor 绑定解析

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
                        RotateDegPerSecond = mobaConfig.Camera.RotateDegPerSecond
                    }
                };
            }

            return Task.CompletedTask;
        }
    }
}
