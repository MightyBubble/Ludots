using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using PresenterTreeOverrideShowcaseMod.Runtime;
using PresenterTreeOverrideShowcaseMod.Systems;

namespace PresenterTreeOverrideShowcaseMod
{
    /// <summary>
    /// 表现树覆盖演示：画面内容全部来自 assets 配置；本程序集只提供配置表达不了的三件事——
    /// 巡逻车按组件参数绕圈，乘客按地图时间表上下车，镜头按地图巡游表逐站停留。
    /// </summary>
    public sealed class PresenterTreeOverrideShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[PresenterTreeOverrideShowcaseMod] Loaded");
            PresenterTreeShowcaseComponentAuthoring.Register(context.ModId);

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    // capabilityId: presenter-tree-override.orbit-motion
                    engine.RegisterSystem(new PresenterTreeOrbitMotionSystem(engine.World), SystemGroup.InputCollection);
                    // capabilityId: presenter-tree-override.rider-shuttle
                    engine.RegisterSystem(new PresenterTreeRiderShuttleSystem(engine), SystemGroup.PostMovement);
                    // capabilityId: presenter-tree-override.camera-tour
                    engine.RegisterSystem(new PresenterTreeCameraTourSystem(engine), SystemGroup.PostMovement);
                }

                return Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
