using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Entity attachment capability 端到端验收（headless 全引擎 + AttachmentCapabilityMod）：
    /// 多层炮塔坦克（底盘 Nav 写权独立移动、炮塔独立朝向、炮管孙层深度序）、
    /// 静态聚落（parent-moved 门）、GAS Attach/Detach 效果驱动骑乘与周界散布、写权授予/归还。
    /// 产出 MUD 风格战报 + trace + path 工件到 artifacts/acceptance/entity-attachment/。
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class EntityAttachmentCapabilityAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "attachment_capability";

        private static readonly string[] AcceptanceMods = { "LudotsCoreMod", "AttachmentCapabilityMod" };

        private readonly List<string> _battleLog = new();
        private readonly List<string> _trace = new();

        private void Log(string line)
        {
            _battleLog.Add(line);
            _trace.Add($"{line}");
            TestContext.Out.WriteLine(line);
        }

        [Test]
        public void AttachmentCapability_TankFollowsSettlementGateAndRiderLifecycle()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
                Path.Combine(repoRoot, "assets"));
            engine.Start();
            engine.LoadMap(MapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, "capability map must create a live session");
            // 不经 simulationLoop.Step()：那会切 TurnBased 冻结后续 engine.Tick 的模拟步，
            // sink 不再运行（headless 验收与 RTS 先例同走 realtime Tick）。
            for (int i = 0; i < 4; i++)
            {
                engine.Tick(DeltaTime);
            }

            var sink = engine.GetService(CoreServiceKeys.AttachmentPositionSync)
                ?? throw new InvalidOperationException("AttachmentPositionSync service missing.");
            Entity chassis = RequireEntityByName(engine.World, "Attachment.Tank.Chassis");
            Entity turret = RequireEntityByName(engine.World, "Attachment.Tank.Turret");
            Entity barrel = RequireEntityByName(engine.World, "Attachment.Tank.Barrel");
            Entity rider = RequireEntityByName(engine.World, "Attachment.Rider");
            Entity hall = RequireEntityByName(engine.World, "Attachment.Settlement.Hall");
            Entity annex = RequireEntityByName(engine.World, "Attachment.Settlement.Annex");
            Entity tower = RequireEntityByName(engine.World, "Attachment.Settlement.Tower");

            Log("== Entity Attachment capability 验收 ==");
            Log("[地图] attachment_capability 装载：坦克组合（底盘+炮塔+炮管）、骑乘单位、聚落（大厅+附楼+塔楼）就位");

            Log($"[预置组合] 炮塔挂接底盘 ChildOf={engine.World.Get<ChildOf>(turret).Parent == chassis}" +
                $", 炮管挂接炮塔 ChildOf={engine.World.Get<ChildOf>(barrel).Parent == turret}" +
                $", 附楼局部偏移=({engine.World.Get<AttachedLocalPose>(annex).OffsetCm.X.ToInt()},{engine.World.Get<AttachedLocalPose>(annex).OffsetCm.Y.ToInt()})cm");
            Assert.Multiple(() =>
            {
                Assert.That(engine.World.Get<ChildOf>(turret).Parent, Is.EqualTo(chassis));
                Assert.That(engine.World.Get<ChildOf>(barrel).Parent, Is.EqualTo(turret));
                Assert.That(engine.World.Get<ChildOf>(annex).Parent, Is.EqualTo(hall));
                Assert.That(engine.World.Get<ChildOf>(tower).Parent, Is.EqualTo(hall));
                Assert.That(engine.World.Get<WorldPositionCm>(annex).Value.X.ToFloat(), Is.EqualTo(5700f).Within(2f));
                Assert.That(engine.World.Get<WorldPositionCm>(annex).Value.Y.ToFloat(), Is.EqualTo(5000f).Within(2f));
                Assert.That(engine.World.Get<WorldPositionCm>(tower).Value.X.ToFloat(), Is.EqualTo(4650f).Within(2f));
                Assert.That(engine.World.Get<WorldPositionCm>(tower).Value.Y.ToFloat(), Is.EqualTo(5600f).Within(2f));
                Assert.That(engine.World.Get<PoseAuthority>(chassis).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                    "底盘持有 Nav 写权独立移动");
            });

            // ── 静态父样例：parent-moved 门 ──
            for (int i = 0; i < 3; i++)
            {
                engine.Tick(DeltaTime);
            }
            Log($"[静态父门] 全场 3 tick 无移动：跳过={sink.LastGateSkippedCount}（附楼+塔楼 + 未移动底盘上的炮塔）, 应用={sink.LastAppliedCount}");
            Assert.That(sink.LastGateSkippedCount, Is.EqualTo(3), "静态父位置依赖子树整树跳过（炮管朝向依赖不进门）");

            // ── 底盘移动（Nav 写权持有者的位姿写），炮塔跟随 + 独立瞄准 ──
            // 底盘朝向 +X 移动 2000cm；炮塔保持独立朝向（瞄准 -Y）。
            // 位姿写在 InputCollection 组（步内、PostMovement 之前），与真实 nav 写者同位次。
            var chassisPoseScript = new ScriptedChassisPoseSystem(engine.World, chassis);
            engine.RegisterSystem(chassisPoseScript, SystemGroup.InputCollection);
            engine.World.Get<FacingDirection>(turret).AngleRad = (float)(-Math.PI / 2);
            foreach (int stopCm in new[] { 500, 1000, 1500, 2000 })
            {
                chassisPoseScript.Enqueue(Fix64Vec2.FromInt(stopCm, 0), 0f);
                World world = engine.World;
                int target = stopCm;
                TickUntil(engine, () => world.Get<WorldPositionCm>(chassis).Value.X.ToInt() >= target, 10,
                    $"chassis must reach {stopCm}cm");
            }

            Fix64Vec2 chassisPose = engine.World.Get<WorldPositionCm>(chassis).Value;
            Fix64Vec2 turretPose = engine.World.Get<WorldPositionCm>(turret).Value;
            Fix64Vec2 barrelPose = engine.World.Get<WorldPositionCm>(barrel).Value;
            Log($"[多层跟随] 底盘 Nav 移动至 ({chassisPose.X.ToInt()},{chassisPose.Y.ToInt()})cm →" +
                $" 炮塔=({turretPose.X.ToInt()},{turretPose.Y.ToInt()})cm（零偏移锚定）," +
                $" 炮管=({barrelPose.X.ToInt()},{barrelPose.Y.ToInt()})cm（炮塔朝向 -Y 的 220cm 前伸）," +
                $" 深度={sink.LastMaxDepth}");
            Assert.Multiple(() =>
            {
                Assert.That(chassisPose.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
                Assert.That(turretPose.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
                Assert.That(turretPose.Y.ToFloat(), Is.EqualTo(0f).Within(2f));
                // 炮管随炮塔独立朝向（-Y）：局部 (220,0) 旋转 -90° → (0,-220)。
                Assert.That(barrelPose.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
                Assert.That(barrelPose.Y.ToFloat(), Is.EqualTo(-220f).Within(2f));
                Assert.That(engine.World.Get<FacingDirection>(turret).AngleRad, Is.EqualTo((float)(-Math.PI / 2)).Within(1e-4f),
                    "炮塔独立瞄准不被底盘/同步改写");
                Assert.That(sink.LastMaxDepth, Is.EqualTo(1), "炮管孙层与炮塔同一步内一致（深度序）");
            });

            // ── GAS Attach：骑乘单位上车，写权 Nav→Attached ──
            var effectQueue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue service missing.");
            Assert.That(engine.World.Get<PoseAuthority>(rider).Value, Is.EqualTo(PoseAuthorityKind.Nav));
            PublishEffect(effectQueue, "Effect.Attachment.AttachRider", chassis, rider);
            World simWorld = engine.World;
            TickUntil(engine, () => simWorld.Has<AttachedLocalPose>(rider) && simWorld.Get<PoseAuthority>(rider).Value == PoseAuthorityKind.Attached, 10,
                "rider must be attached with Attached authority after AttachOp");
            Fix64Vec2 riderAttached = engine.World.Get<WorldPositionCm>(rider).Value;
            Log($"[AttachOp] 骑乘单位上车：ChildOf=底盘, 局部偏移 (0,-140)cm → ({riderAttached.X.ToInt()},{riderAttached.Y.ToInt()})cm," +
                $" 写权={engine.World.Get<PoseAuthority>(rider).Value}");
            Assert.Multiple(() =>
            {
                Assert.That(engine.World.Get<ChildOf>(rider).Parent, Is.EqualTo(chassis));
                Assert.That(engine.World.Has<AttachedLocalPose>(rider));
                Assert.That(riderAttached.X.ToFloat(), Is.EqualTo(2000f).Within(2f));
                Assert.That(riderAttached.Y.ToFloat(), Is.EqualTo(-140f).Within(2f));
                Assert.That(engine.World.Get<PoseAuthority>(rider).Value, Is.EqualTo(PoseAuthorityKind.Attached),
                    "attach 授予 Attached 写权（边界结算后）");
            });

            // 底盘再前进，骑乘者随车。
            foreach (int stopCm in new[] { 3000, 3500 })
            {
                chassisPoseScript.Enqueue(Fix64Vec2.FromInt(stopCm, 0), 0f);
                int target = stopCm;
                TickUntil(engine, () => simWorld.Get<WorldPositionCm>(rider).Value.X.ToInt() >= target, 10,
                    $"rider must ride to {stopCm}cm");
            }
            Fix64Vec2 riderRiding = engine.World.Get<WorldPositionCm>(rider).Value;
            Log($"[随车] 底盘前进至 3500cm，骑乘者=({riderRiding.X.ToInt()},{riderRiding.Y.ToInt()})cm 保持相对偏移");
            Assert.Multiple(() =>
            {
                Assert.That(riderRiding.X.ToFloat(), Is.EqualTo(3500f).Within(2f));
                Assert.That(riderRiding.Y.ToFloat(), Is.EqualTo(-140f).Within(2f));
            });

            // ── GAS Detach：周界散布下车，写权归还 Nav ──
            PublishEffect(effectQueue, "Effect.Attachment.DetachRiderScatter", chassis, rider);
            TickUntil(engine, () => !simWorld.Has<ChildOf>(rider) && simWorld.Get<PoseAuthority>(rider).Value == PoseAuthorityKind.Nav, 10,
                "rider must be detached with Nav authority after DetachOp");
            Fix64Vec2 riderDetached = engine.World.Get<WorldPositionCm>(rider).Value;
            Log($"[DetachOp] 周界散布下车：落地=({riderDetached.X.ToInt()},{riderDetached.Y.ToInt()})cm" +
                $"（底盘 (3500,0) 半径 260cm 环槽 0），写权={engine.World.Get<PoseAuthority>(rider).Value}");
            Assert.Multiple(() =>
            {
                Assert.That(engine.World.Has<ChildOf>(rider), Is.False);
                Assert.That(engine.World.Has<AttachedLocalPose>(rider), Is.False);
                Assert.That(riderDetached.X.ToFloat(), Is.EqualTo(3760f).Within(2f));
                Assert.That(riderDetached.Y.ToFloat(), Is.EqualTo(0f).Within(2f));
                Assert.That(engine.World.Get<PoseAuthority>(rider).Value, Is.EqualTo(PoseAuthorityKind.Nav),
                    "detach 归还 Nav 写权（边界结算后）");
            });

            Log("== 验收通过：绑定与位置合同成立（可见性/指挥性等玩法语义不在本票范围）==");
            WriteArtifacts(repoRoot);
        }

        /// <summary>
        /// realtime Pacemaker 的固定步与平台帧不同频（50Hz vs 1/60），逐帧断言不确定；
        /// 按 RTS 验收先例用条件轮询驱动。
        /// </summary>
        private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                engine.Tick(DeltaTime);
            }

            Assert.That(condition(), Is.True, because);
        }

        /// <summary>
        /// 测试扮演底盘的 Nav 写者：在 InputCollection（PostMovement 之前）按脚本写 Current 位姿，
        /// 与真实 nav 写者同位次——SavePrevious 在步首已留旧值，parent-moved 门据此识别"父移动了"。
        /// </summary>
        private sealed class ScriptedChassisPoseSystem : Arch.System.BaseSystem<World, float>
        {
            private readonly Queue<(Fix64Vec2 Pose, float FacingRad)> _steps = new();
            private readonly Entity _chassis;

            public ScriptedChassisPoseSystem(World world, Entity chassis) : base(world)
            {
                _chassis = chassis;
            }

            public void Enqueue(Fix64Vec2 pose, float facingRad)
            {
                _steps.Enqueue((pose, facingRad));
            }

            public override void Update(in float dt)
            {
                if (_steps.Count == 0 || !World.IsAlive(_chassis))
                {
                    return;
                }

                (Fix64Vec2 pose, float facingRad) = _steps.Dequeue();
                World.Get<WorldPositionCm>(_chassis).Value = pose;
                if (World.Has<FacingDirection>(_chassis))
                {
                    World.Get<FacingDirection>(_chassis).AngleRad = facingRad;
                }
            }
        }

        private static void PublishEffect(EffectRequestQueue queue, string templateKey, Entity source, Entity target)
        {
            int templateId = EffectTemplateIdRegistry.GetId(templateKey);
            Assert.That(templateId, Is.GreaterThan(0), $"effect template '{templateKey}' must be registered");
            queue.Publish(new EffectRequest
            {
                RootId = 0,
                Source = source,
                Target = target,
                TargetContext = target,
                TemplateId = templateId,
            });
        }

        private static Entity RequireEntityByName(World world, string name)
        {
            Entity found = Entity.Null;
            world.Query(in new QueryDescription().WithAll<Name>(), (Entity entity, ref Name componentName) =>
            {
                if (found == Entity.Null && string.Equals(componentName.Value, name, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"entity '{name}' must exist");
            return found;
        }

        private void WriteArtifacts(string repoRoot)
        {
            string directory = Path.Combine(repoRoot, "artifacts", "acceptance", "entity-attachment");
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "battle-report.md"), _battleLog, Encoding.UTF8);
            File.WriteAllLines(Path.Combine(directory, "trace.jsonl"), _trace, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "path.mmd"), MermaidPath, Encoding.UTF8);
        }

        private const string MermaidPath = @"flowchart TD
    A[地图装载: 模板 children 预置组合] --> B{静态父?}
    B -- 是 --> C[parent-moved 门: 整树跳过]
    B -- 否 --> D[sink: 父∘局部 深度序派生]
    D --> E[底盘 Nav 写权独立移动]
    E --> F[炮塔零偏移锚定 + 独立朝向]
    F --> G[炮管随炮塔朝向前伸 孙层]
    G --> H[AttachOp: 骑乘上车 Nav→Attached]
    H --> I{事务失败?}
    I -- 是 --> J[回滚: 挂接/位姿/写权恢复]
    I -- 否 --> K[随车保持局部偏移]
    K --> L[DetachOp: 周界散布 Attached→Nav]
    L --> M[验收通过]
    J --> M";

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test directory.");
        }
    }
}
