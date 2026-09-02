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
    /// M2.a:MapLoaded 时(raylib 宿主先 GameStart 后装载启动地图,故挂点在 MapLoaded 而非
    /// GameStart;内核与 SangoWebUiModEntry 收敛于同一 Scenario.Cur 判空门)把内核地图灌入
    /// 引擎 Field2D 层并落地城池标记 presenter;地图会话未启用 sango 层的宿主(非 sango
    /// 地图)整体跳过。
    /// </summary>
    public sealed class SangoSimModEntry : IMod
    {
        private static SaveParticipantRegistry? _registeredRegistry;

        public void OnLoad(IModContext context)
        {
            IVirtualFileSystem vfs = context.VFS;
            context.Log("[SangoSimMod] Loaded (M1.b: kernel boot on first manual turn, assets via VFS; M1.d: sango.sim save participant; M2.a: MapLoaded field/marker sync)");
            context.OnEvent(GameEvents.MapLoaded, OnMapLoaded(vfs));
            context.OnEvent(GameEvents.TurnAdvanced, OnTurnAdvanced(vfs));
        }

        public void OnUnload()
        {
        }

        private static System.Func<ScriptContext, Task> OnMapLoaded(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                SyncWorldToEngine(context);
                return Task.CompletedTask;
            };
        }

        // Field2D 灌层 + 城标记只在"sango 地图会话 + presenter 管线"宿主生效:地图未启用
        // sango.terrainType 层即视为非 sango 地图,整体跳过(不代建);启用即 fail-fast。
        private static void SyncWorldToEngine(ScriptContext context)
        {
            if (!context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                return;
            }

            Ludots.Core.Map.MapSession? session = engine.MapSessions?.FocusedSession;
            if (session?.Fields == null ||
                !session.Fields.TryGetByKey(SangoFieldLayers.TerrainTypeLayerKey, out _))
            {
                return;
            }

            SangoFieldLayers.Populate(session.Fields, Sango.Core.Scenario.Cur);

            var presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator);
            if (presenterRuntime == null || definitions == null || stableIds == null)
            {
                return;
            }

            int spawned = SangoCityMarkers.Spawn(
                engine.World, presenterRuntime, definitions, stableIds,
                SangoCityMarkers.BuildPlacements(Sango.Core.Scenario.Cur));
            Log.Info($"[SangoSimMod] M2.a: field layers populated; {spawned} city markers spawned");
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
