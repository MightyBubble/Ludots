using Ludots.Core.Modding;

namespace CityRallyShowcaseMod
{
    /// <summary>
    /// 纯资产 mod：系统与 GAS 管线全部复用 RtsDemoMod 的 setSpawnTarget 集结点链路，此处无需注册任何系统。
    /// </summary>
    public class CityRallyShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CityRallyShowcaseMod] Loaded — 城池集结点 showcase（复用 RtsDemoMod setSpawnTarget 管线）。");
        }

        public void OnUnload()
        {
        }
    }
}
