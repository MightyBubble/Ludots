using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Sango.Runtime;

namespace Sango
{
    /// <summary>
    /// SangoSimMod 入口(M1.b):把 Ludots 的 Manual 回合时钟事件转译为 sango 回合推进。
    /// assets/GAS/clock.json = Manual → GasClockSystem 每次 RequestStep(1) 触发一次
    /// TurnAdvanced(PLAN D5);首个事件到达时按默认种子惰性启动内核(真实运行时序里
    /// 时钟先于任何地图输入,无需独立 GameStart 挂点)。
    /// M1.d:每次回合事件时向引擎存档注册表补注册 sango.sim 域(注册表由宿主按核心服务
    /// 就绪度建出,缺席即该宿主无持久化基建,不代建)。
    /// </summary>
    public sealed class SangoSimModEntry : IMod
    {
        private static SaveParticipantRegistry? _registeredRegistry;

        public void OnLoad(IModContext context)
        {
            IVirtualFileSystem vfs = context.VFS;
            context.Log("[SangoSimMod] Loaded (M1.b: kernel boot on first manual turn, assets via VFS; M1.d: sango.sim save participant)");
            context.OnEvent(GameEvents.TurnAdvanced, OnTurnAdvanced(vfs));
        }

        public void OnUnload()
        {
        }

        private static System.Func<ScriptContext, Task> OnTurnAdvanced(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                RegisterSaveParticipant(context, vfs);
                SangoTurnDriver.AdvanceTurn();
                return Task.CompletedTask;
            };
        }

        // 引擎注册表晚于 mod 装载建出(核心服务就绪时),且内核可能被 SangoWebUiMod 的
        // GameStart 启动抢先(本入口的惰性 Boot 分支不再进入),故挂在每回合事件上做一次
        // 幂等注册;同一注册表实例只注册一次(Register 对重复域显式抛错)。
        private static void RegisterSaveParticipant(ScriptContext context, IVirtualFileSystem vfs)
        {
            if (!context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                return;
            }

            SaveParticipantRegistry? registry = engine.GetService(CoreServiceKeys.SaveParticipants);
            if (registry == null || ReferenceEquals(registry, _registeredRegistry))
            {
                return;
            }

            registry.Register(new SangoSaveParticipant(vfs, "SangoContentMod"));
            _registeredRegistry = registry;
        }
    }
}
