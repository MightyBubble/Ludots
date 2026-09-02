using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Sango.Runtime;

namespace Sango
{
    /// <summary>
    /// SangoSimMod 入口(M1.b):把 Ludots 的 Manual 回合时钟事件转译为 sango 回合推进。
    /// assets/GAS/clock.json = Manual → GasClockSystem 每次 RequestStep(1) 触发一次
    /// TurnAdvanced(PLAN D5);首个事件到达时按默认种子惰性启动内核(真实运行时序里
    /// 时钟先于任何地图输入,无需独立 GameStart 挂点)。
    /// </summary>
    public sealed class SangoSimModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            IVirtualFileSystem vfs = context.VFS;
            context.Log("[SangoSimMod] Loaded (M1.b: kernel boot on first manual turn, assets via VFS)");
            context.OnEvent(GameEvents.TurnAdvanced, OnTurnAdvanced(vfs));
        }

        public void OnUnload()
        {
        }

        private static System.Func<ScriptContext, Task> OnTurnAdvanced(IVirtualFileSystem vfs)
        {
            return _ =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                SangoTurnDriver.AdvanceTurn();
                return Task.CompletedTask;
            };
        }
    }
}
