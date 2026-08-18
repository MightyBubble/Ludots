using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CityRallyShowcaseMod.Systems;

namespace CityRallyShowcaseMod
{
    /// <summary>
    /// 复用 RtsDemoMod 的 setSpawnTarget 集结点链路；仅补 Knowledge 投影系统（引擎选择门控要求实体 LiveVisible 才可选中）。
    /// </summary>
    public class CityRallyShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                engine.RegisterSystem(new CityRallyKnowledgeProjectionSystem(engine), SystemGroup.InputCollection);
                return Task.CompletedTask;
            });

            context.Log("[CityRallyShowcaseMod] Loaded — 城池集结点 showcase（复用 RtsDemoMod setSpawnTarget 管线）。");
        }

        public void OnUnload()
        {
        }
    }
}
