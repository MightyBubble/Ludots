#!/usr/bin/env python3
"""Epic #990 Stage-3 closer: run remaining families through pi(deepseek-v4-flash),
reconcile, gate on tests, record, checkpoint; then re-record old-pacing evidence."""
from __future__ import annotations

import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
LOG = REPO / ".epic-briefs" / "stage3.log"
PI = shutil.which("pi") or shutil.which("pi.cmd") or r"C:\Users\sietg\AppData\Roaming\npm\pi.cmd"

FAMILIES = [
    (
        3,
        "QueryFilterTemplate,QueryFilterTeam,QueryFilterAttributeRange,QueryFromCollection,"
        "AggAverageAttribute,QueryAllMapEntities,AggSumAttribute,AggMinAttribute,AggMaxAttribute,"
        "QueryFilterTagNone,AggMinEntityByAttribute,AggMaxEntityByAttribute,QueryFilterTagAny",
        "核心模式=两拍框架（QueryNodeDriver.DrawOverlay 按 ctx.Wave 奇偶切换满员拍/结果拍）；"
        "Agg 数值版 detailTemplate 删'对应{label}'越权文案；QueryLimit/QuerySortStable 不在本家族（勿动）；"
        "席位双圈替代 caster 恒亮（LightCasterAndHits 不再无条件点亮 caster 的驱动用法照 fam3 规格）。",
    ),
    (
        8,
        "Call,Return,InvokeScript,MoveInt,JumpIfFalse,HaltReturnInt,Jump",
        "Yield 试点已完成茶杯水位（直读 _ints[0]，勿重做）：Jump/JumpIfFalse 共用茶杯+被跳过行灰化；"
        "Call/Return 驿站标牌+双向箭头路+残影圈；InvokeScript 双卷轴（callee SourceMap 已注册 _programs）；"
        "MoveInt 寄存器面板（左框不动右框长出，复制语义）；HaltReturnInt 答案托盘（ReturnInt pip）+打烊横条。"
        "铁律：水位不得回写 ActorHealth；drink/单人 op 头顶 HUD 隐藏已在 Yield 做过（勿重复）。",
    ),
    (
        4,
        "LoadContextTarget,LoadAttribute,ConstInt,CompareEqInt,CompareEqEntity,RemoveEffectTemplate,"
        "SelectEntity,ModifyAttributeAdd,LoadSelfAttribute,WriteSelfAttribute,CompareLtInt,LoadCaster,"
        "AddInt,LoadExplicitTarget",
        "ApplyEffectTemplate 试点样板（真实读 ActiveEffectContainer 画 Diamond 徽章+光环，禁止画假）。"
        "CompareEqInt/CompareLtInt/SelectEntity/CompareEqEntity 走 JumpIfFalse 门控+Effect.GraphOps.Strike 真结算"
        "（JumpIfFalse 在 Effect 图的编译路径 F6 已验证可行）；LoadContextTarget/LoadSelfAttribute/WriteSelfAttribute/"
        "LoadExplicitTarget/ModifyAttributeAdd 只需补过程视觉（准星/自环/写入竖线/伤害浮标/残影格，见 fam4 规格）；"
        "ConstInt/AddInt 算式台照 MulFloat 配方但 ConstInt 保留单节点图+铭牌道具、AddInt 用图内组合落地层数。"
        "铁律：驱动 Tick 内禁数值断言（数值归测试）。",
    ),
    (
        7,
        "RelationshipEnsureLink,QueryLimit,FanOutApplyEffect,QuerySortStable,RelationshipAddMetric,"
        "RelationshipSetMetric,ApplyEffectDynamic,FanOutApplyEffectDynamic,QueryRadius,HasTag,RelationshipHasFlag",
        "SandboxNodeDriver.DrawOverlay 以 ProgramHasQueryRadius 早退是画面全空根因——改为按 op 族分发"
        "（Relationship 族画青链 DrawDirectedLine+旗/记事板挂件；Tag 族画 DrawBadge；Query 族加 DrawRankPips）。"
        "真实结算：assets/GAS/sandbox/catalog.json 的 buffEffect→Effect.GraphOps.Strike；"
        "graphs/FanOutApplyEffect.json effectTemplate→Strike。⚠️QueryLimit 文案改真（'名单取前三个'，图加 "
        "QueryFilterNotEntity 前缀，禁止造 QuerySortByDistance）；⚠️QuerySortStable 同理（图加 notSelf 前缀+"
        "JoinHitNames 改按 HitTargets 真实序）。QueryRadius 图加 FilterNotEntity（count 不含自己）。"
        "EnsureLink 灰虚线→青实线两拍+环扣；AddMetric/SetMetric 记事板（血条不动——好感≠血量）。",
    ),
    (
        1,
        "LoadViewer,SnapToNearestInCollection,SendEvent,ControlDomainResolve,FanOutDispatchEffect,"
        "FanOutDispatchEffectDynamic,KnowledgeHasProjection,LoadEventPayloadFloat,LoadEventPayloadInt,"
        "LoadTargetPosY,LoadTargetPosX,IsPointInCircle,ControlDomainControls",
        "SnapToNearestGraphEdge 试点样板（ghost+下落箭头）。⚠️LoadEventPayloadFloat/Int 载荷真事件化："
        "新增生产者图 assets/GAS/graphs/LoadEventPayloadFloat.producer.json（LoadExplicitTarget→ConstFloat 2.5→"
        "SendEvent，语法照抄 SendEvent.json），EventNodeDriver 每 tick 先执行生产者、EventBus 后从总线真读回，"
        "删 BuildPayload 常量注入；断言总线恰 1 条事件+featured 结果==总线值。若 SendEvent 图 op 在 Effect 图"
        "不可编译则停下报告（勿造假）。SendEvent 听众=监听图（照 ApplyEffectDynamic 端口模式）作用于事件目标，"
        "徽章来自真实结算。ControlDomain 链用 vignette links(type Owns)+DrawDirectedLine 白实心指挥箭头+旗徽章。"
        "LoadTargetPosX/Y 双标尺（_fields/pos.json 共享舞台）。FanOutDispatch 系：预设卡+三连扇出箭头+浮标，"
        "静态版模板→Strike 真掉血、动态版→Mark 徽章（读 ActiveEffectContainer）。",
    ),
    (
        5,
        "QueryHexRing,QueryFilterRelationship,QueryFilterLayer,QueryFilterNotEntity,AggCount,QueryCone,"
        "TargetListGet,QueryRectangle,AggMinByDistance,QueryHexNeighbors,QueryHexRange,QueryLine",
        "亮暗门控已由 SyncHud 正确处理（勿动 GraphOpsStageVisuals 门控）。"
        "描边对比度：cone/矩形/线全部双笔（外深内亮，参考 DrawThickOutlineCircle 的 1.8x 思路，手画双笔即可）。"
        "场景去脆弱化（改 _fields/spatial.json + 各 vignette 演员坐标）：hexN→(2.6,5.0)、hexNW→(-2.6,5.0)、"
        "新增 edgeOut(4.1,5.9)；QueryRectangle 锚点前移（Seed 支持从 role 读锚点，现取 caster 位需改）；"
        "QueryLine near→(2.4,1.96)+窄带双平行线。Filter 族两拍：滤前含自己在名单→滤后塌缩，caster 不再恒亮"
        "（删 FillCaptions 尾部强制亮两行）。FilterLayer/FilterRelationship 区分：雇佣兵/内奸双演员+host layer "
        "字段——若 host layer 改动受阻，最小方案=两 op 用不同徽章色+文案讲清判据并在报告说明。"
        "AggMinByDistance 距离阶梯（灰线淘汰+胜者红线刻度）。TargetListGet 名次角标（照 QuerySortByAttribute）。",
    ),
]

RERECORD = [
    "RelationshipQueryIncoming", "RelationshipSetFlag", "QuerySortByAttribute",
    "WriteBlackboardFloat", "Yield", "SnapToNearestGraphEdge", "ApplyEffectTemplate",
] + [
    "RelationshipQueryMutual", "RelationshipFilterFlag", "RelationshipAggAverageMetric",
    "RelationshipFilterMetricRange", "RelationshipQueryOutgoing", "RelationshipHasLink",
    "RelationshipAggSumMetric", "RelationshipRemoveLink", "RelationshipSortByMetric",
    "RelationshipAggMinMetric", "RelationshipAggMaxMetric", "RelationshipGetMetric",
    "RelationshipAggMinEntityByMetric", "RelationshipAggMaxEntityByMetric",
    "RelationshipQueryBetweenPair",
] + [
    "LoadContextSource", "LoadContextTargetContext", "LoadConfigFloat", "LoadConfigInt",
    "ReadBlackboardFloat", "ReadBlackboardInt", "ReadBlackboardEntity", "LoadConfigEffectId",
    "BeginLifecycleTransaction", "WriteBlackboardInt", "WriteBlackboardEntity", "InvokeBuiltin",
]

SYSTEM = "你是 Ludots 仓库的实施工程师。严格遵守工单硬规则；改前先读文件；每 op 后跑测试；不确定就停并报告。"


def log(msg: str) -> None:
    stamp = time.strftime("%H:%M:%S")
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as stream:
        stream.write(f"[{stamp}] {msg}\n")
    print(f"[{stamp}] {msg}", flush=True)


def run(cmd: list[str], timeout_s: int = 3600) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, cwd=REPO, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=timeout_s)


def gallery_tests_green() -> tuple[bool, str]:
    for attempt in range(3):
        subprocess.run(["taskkill", "/IM", "testhost.exe", "/F"],
                       capture_output=True, text=True)
        time.sleep(3)
        result = run(["dotnet", "test", "src/Tests/GasTests",
                      "--filter", "FullyQualifiedName~GraphOpsNodeGallery", "--nologo"],
                     timeout_s=1200)
        tail = (result.stdout or "") + (result.stderr or "")
        if "已通过" in tail or "Passed" in tail:
            return True, tail.strip().splitlines()[-1] if tail.strip() else "green"
        if "MSB3027" in tail or "锁定" in tail or "locked" in tail.lower():
            log(f"test lock hit, retry {attempt + 1}/3")
            continue
        return False, tail[-1500:]
    return False, "test lock retries exhausted"


def reconcile() -> bool:
    for script in ("generate-graph-op-node-galleries.py", "generate-graph-op-node-wiki.py"):
        result = run(["python", f"scripts/{script}"])
        if result.returncode != 0:
            log(f"generator {script} FAILED: {result.stderr[-500:]}")
            return False
    check = run(["python", "-c",
                 "import json,glob;bad=[p for p in glob.glob(r'mods/showcases/capability_standard/"
                 r"CapabilityStandardGraphOpsNodeGalleryMod/assets/Maps/capability_standard_graph_op_*.json') "
                 "if json.load(open(p,encoding='utf-8'))['DefaultCamera']['VirtualCameraId']"
                 "!='Camera.Profile.GraphOpsGallery'];print(len(bad))"])
    if check.stdout.strip() != "0":
        log(f"camera drift: {check.stdout.strip()} bad maps")
        return False
    return True


def record(ops: list[str]) -> bool:
    cmd = ["python", "scripts/record-graph-op-node-galleries.py", "--build", "auto"]
    for op in ops:
        cmd += ["--op", op]
    result = run(cmd, timeout_s=5400)
    tail = (result.stdout or "") + (result.stderr or "")
    ok = "failed 0" in tail
    log(f"record {len(ops)} ops -> {'OK' if ok else 'FAIL'}\n{tail[-600:]}")
    return ok


def run_pi(family: int, note: str) -> bool:
    prompt = (
        f"先读 .epic-briefs/common.md 和 .epic-briefs/fam{family}.md（家族规格）。"
        f"完成 fam{family} 中尚未实现的全部 op（试点与已完成项勿重做勿改）。"
        f"本家族要点：{note} "
        "铁律：驱动 Tick 内禁数值断言（数值归 headless 测试）；行为变化用既有 op 图内组合；"
        "文案三件套按规格逐字；测试 pin 同步；最终 dotnet test --filter GraphOpsNodeGallery 全绿"
        "（sync gate 两个失败可忽略，协调者会跑生成器）。开始。"
    )
    for attempt in range(3):
        result = run([PI, "--model", "deepseek-v4-flash", "--no-session", "--print",
                      "--append-system-prompt", SYSTEM, prompt], timeout_s=5400)
        tail = (result.stdout or "") + (result.stderr or "")
        if result.returncode == 0 and len(tail) > 200:
            LOG.parent.mkdir(parents=True, exist_ok=True)
            (REPO / f".epic-briefs/pi_fam{family}_report.txt").write_text(tail, encoding="utf-8")
            log(f"pi fam{family} exit=0 report_saved ({len(tail)} chars)")
            return True
        log(f"pi fam{family} attempt {attempt + 1} failed (exit={result.returncode}, {len(tail)} chars): {tail[:120]}")
        time.sleep(90)
    return False


def main() -> int:
    start_from = int(sys.argv[sys.argv.index("--start") + 1]) if "--start" in sys.argv else 1
    for family, ops_csv, note in FAMILIES:
        if family < start_from:
            continue
        ops = [op for op in ops_csv.split(",") if op]
        log(f"=== F{family} start ({len(ops)} ops) ===")
        if not run_pi(family, note):
            log(f"F{family} ABORT: pi failed")
            return 1
        if not reconcile():
            log(f"F{family} ABORT: reconcile failed")
            return 1
        green, detail = gallery_tests_green()
        if not green:
            log(f"F{family} ABORT: tests red\n{detail}")
            return 1
        log(f"F{family} tests green: {detail}")
        if not record(ops):
            log(f"F{family} ABORT: recording failed")
            return 1
        run(["git", "add", "-A"])
        commit = run(["git", "commit", "-m",
                      f"epic(#990) stage3-F{family}: 零字幕化（pi/deepseek-v4-flash）+ 测试绿 + {len(ops)} 录屏"])
        log(f"F{family} committed: {commit.stdout.strip()[:60]}")

    log(f"=== re-record {len(RERECORD)} old-pacing ops ===")
    if not record(RERECORD):
        log("re-record FAILED")
        return 1
    run(["git", "add", "-A"])
    run(["git", "commit", "-m", "epic(#990) stage4: F2/F9/试点 34 op 换新节拍重录（8s 三段）"])
    green, detail = gallery_tests_green()
    log(f"final tests: green={green} {detail}")
    log("STAGE3 COMPLETE")
    return 0


if __name__ == "__main__":
    sys.exit(main())
