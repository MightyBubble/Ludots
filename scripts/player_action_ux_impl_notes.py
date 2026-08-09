# -*- coding: utf-8 -*-
"""Ludots implementation notes for player-action UX catalog cases.

Checkpoint context (for later agents — do not invent newer status without re-audit):
  Branch tip at authorship: cursor/wasd-locomotion-ux-4211 @ 20986f5b8
  Base already on main from #743: player-action-ux catalog (Mermaid + storyboard).
  This module only annotates; it does not claim runtime features landed after that audit.

Speak plain Chinese for PM. Mark gaps as todos, never silent.
"""
from __future__ import annotations

from typing import Any

# category → (ludots one-liner, default todos)
CATEGORY_DEFAULTS: dict[str, tuple[str, list[str]]] = {
    "select": (
        "点选/框选：CommandSourceAcquisitionSystem 写入 EntityCollectionStore"
        "（collection.command.source / hover）；加选模式走 Replace/Additive/Toggle。",
        ["控制组存取仅 InteractionShowcase，未进 Core 正式 API"],
    ),
    "basic-order": (
        "右键/指令：CommandIntentArbiter + command_intent_profiles → OrderQueue"
        "（moveTo / attackTarget / stop）；群体走位走 MassNavigation 执行链。",
        ["默认 intent 配置偏 moveTo，复杂兵种语义需数据补齐"],
    ),
    "aim": (
        "瞄准：InputOrderMappingSystem + InteractionModeType（AimCast 等）；"
        "瞄准表现 AbilityAimPresentationRuntime；指针地面点 AuthoritativeGroundPointerHelper。",
        ["RFC-0065 欲退役专用 aim 事件，CastCommit 配置当前多为空 profiles"],
    ),
    "attack": (
        "普攻/射击：经 OrderQueue / AbilityExec；RTS 右键攻击走 attackTarget intent。",
        ["FPS 开镜换弹等偏展示/模组，无统一枪械 UX 主链"],
    ),
    "twin-stick": (
        "双摇杆：ControlScheme + 轴输入；键鼠等价靠 scheme 映射到同一移动/朝向语义。",
        ["磁吸辅助、翻滚中转向等属手感策略，需模组/配置声明，非全家桶默认开"],
    ),
    "instant-skill": (
        "无目标技：InputOrderMapping → castAbility Order；自身/脚下类目标在 mapping 与 ability 配置。",
        ["技能主链仍大量依赖旧 InteractionModeType，未完全切到 CastCommitProfile"],
    ),
    "unit-skill": (
        "点单位技：HoveredEntity / 点选目标 → castAbility；智能施法走 SmartCast 模式。",
        ["双目标连续点选要靠能力配置与多次 commit，缺统一 UX 向导"],
    ),
    "ground-skill": (
        "点地技：OrderTargetType.Position；小地图点地有 MinimapInputConsumer（偏展示）。",
        ["InputCastSpec（套索/多边形）RFC 有、代码未落地"],
    ),
    "direction-skill": (
        "方向/矢量：OrderTargetType.Direction / Vector + VectorAim 状态。",
        ["钩索等品类手感在模组，不在 Core 通用动词里写死"],
    ),
    "hold": (
        "按住：PressReleaseAimCast / 持续输入快照；引导/举盾靠 ability 通道与标签。",
        ["按住连发与通道打断的统一手感表仍分散在各 ability"],
    ),
    "combo": (
        "连段：能力图/效果阶段或输入缓冲（模组侧）；Core 提供 Order/GAS 组合点。",
        ["无统一「连段编辑器」产品链，多在具体 showcase/模组"],
    ),
    "defense": (
        "闪避/弹反：能力 + 时机窗（GAS Response / 模组）；非独立防御子系统。",
        ["完美闪避窗口若要用引擎级 Prompt，需接 GasInputResponse，产品层未铺全"],
    ),
    "environment": (
        "环互：可破坏/投掷等多走 ability 或世界交互模组，无统一「环境动词」Registry 产品层。",
        ["缺统一动态 context 交互键（与二十三类同源缺口）"],
    ),
    "army": (
        "部队/随从：指挥源 collection + Order；载具座位偏模组替换 ControlScheme/能力。",
        ["宝宝 AI 自动技见 Utility Autocast，与玩家开关不是一条链"],
    ),
    "cast-habit": (
        "同技能不同手感：ClientCastPreferenceStore（slot/form/template/global）；"
        "Alt 自施/双击/Shift 队列在 special_input 能力与 Order 队列。",
        ["Settings UI 未把偏好链完整接到玩家可点选项"],
    ),
    "multi-cast": (
        "群体施法：CastDispatchProfileRegistry（all / topN / cycle 等，showcase 有证）。",
        ["默认工程配置可能未挂齐，需模组显式声明 dispatch profile"],
    ),
    "context-order": (
        "选中×目标：CommandIntentProfile 谓词路由（点敌/点矿/点建筑不同 Order）。",
        ["RA2/SC2 级完整矩阵靠数据填满，不是代码写死兵种表"],
    ),
    "temp-kit": (
        "临时技：AbilityFormSet + FormRouting 可做形态切换；无统一「限时 overlay 技能栏」产品链。",
        ["TODO: 临时授予/收回的 UX 主链（变身整栏、饰品主动、偷技拷贝）"],
    ),
    "item": (
        "物品：InventoryRuntimeService / ItemAbilityGrant / 交换 showcase 有库存与装备；"
        "「对目标用物品」无独立 Order UX 主链。",
        ["TODO: item-use 交互（自用/对目标/对地）与快捷栏拖放正式化"],
    ),
    "mmo-social": (
        "MMO 社交：Core 无队伍/密聊/交易窗/对话树主链。",
        ["TODO: party / trade / dialogue 产品基建"],
    ),
    "mmo-world": (
        "MMO 世界：采集/商人/邮箱等无统一世界交互主链；坐骑/自动攻击可部分用 ability+scheme 拼。",
        ["TODO: 采集读条、商人、复活、任务追踪等世界循环"],
    ),
    "design-ui": (
        "界面手势：Native UI（Ludots.UI）+ WebUI PanelKit；拖放/确认多在 UI 层，不自动等于玩法 Order。",
        ["玩法向轮盘/信号需接到 CommandIntent 或 UI→Order 桥，尚未标准菜谱"],
    ),
    "dynamic-context": (
        "动态 context：GAS 有 PromptInput 响应；「同一键随身边物体改动词」产品层缺失。",
        ["TODO: 情境交互探测 + 优先级 + 同一键路由（处决/拾取/开门）"],
    ),
    "auto-cast": (
        "自动施法：AI Utility Autocast 契约与 showcase 有；玩家按技能开绿点的 UX 主链缺失。",
        ["TODO: 玩家 Autocast 开关、进距门闩、多技能优先级、手动抢占规则"],
    ),
    "locomotion": (
        "走路：AxisMoveOrderSystem（WASD/轴）+ 右键 moveTo/MassNav；ControlScheme 可热切换。",
        ["点地走自己与 WASD 冲突仲裁需在 scheme/意图层写清；冲刺耐力属能力/属性"],
    ),
    "touch-tablet": (
        "触控/卡牌：玩法输入几乎无独立多指 touch pipeline；仅有指针+拖框阈值。",
        ["TODO: 触控拖拽卡牌、部署落点、双指缩放/平移的 InputCast / pointer 扩展"],
    ),
    "menu-cmd": (
        "选单式指令：EntityCommandPanel / CommandDeck / AbilityAggregation 可做网格点选；"
        "非三国志式多层菜单状态机。",
        ["TODO: 分层选单（武将→指令→目标）的正式导航栈与取消链"],
    ),
    "blocked": (
        "放不了：GAS/Order 失败与表现事件有部分路径；包满/交易断/被控等未统一成 UX 反馈层。",
        ["TODO: 统一拒绝原因码 → 提示/音效/图标闪烁"],
    ),
    "netplay": (
        "联机：Ludots 当前是单机 ECS 框架，没有联机主链——房间、匹配、状态同步、"
        "回滚与重连都不存在，这一族整体是缺口，不要当成已有能力引用。",
        [
            "TODO: 联机会话（建房/加入/大厅准备/开局）产品基建",
            "TODO: 匹配队列与接受对局流程",
            "TODO: 断线重连与队友托管（含玩家可见的状态回显）",
            "TODO: 预测与回滚的玩家可感知表现（被拉回时怎么提示）",
            "TODO: 语音 / 表决 / 举报屏蔽等对局内社交治理",
        ],
    ),
    "couch-play": (
        "同屏多人：Ludots 有多设备输入的雏形（ControlScheme 可绑不同设备），"
        "但没有本地多玩家会话概念——分屏相机、按玩家分路的输入归属、"
        "同屏抢交互的仲裁都缺。",
        [
            "TODO: LocalPlayerSlot（手柄按键加入 / 掉出 / 重新接管）",
            "TODO: 分屏相机与每玩家视口",
            "TODO: 输入归属按玩家分路（谁的手柄控谁）",
            "TODO: 同屏抢同一个可交互物的仲裁与失败反馈",
        ],
    ),
}

# Optional per-case overrides (id → ludots / todos)
CASE_OVERRIDES: dict[str, dict[str, Any]] = {
    "select-control-group": {
        "ludots": "控制组：InteractionShowcaseRuntime 自建 collection key 演示，非 Core API。",
        "todos": ["提升为 Core：编队存取与跨会话策略"],
    },
    "order-smart-right": {
        "ludots": "智能右键总览：CommandIntentProfile 按目标类型路由；具体表在配置数据。",
    },
    "multi-cast-together": {
        "ludots": "齐放：CastDispatch 策略 all（能放的一起 commit）。",
    },
    "multi-cast-sequence": {
        "ludots": "顺序放：CastDispatch cycle / 队列化 commit（showcase 向）。",
    },
    "multi-cast-priority": {
        "ludots": "优先级：CastDispatch topN + FilterProfile 过滤不会的单位。",
    },
    "temp-kit-gow-transform": {
        "ludots": "变身整栏：可勉强用 AbilityFormSet 换表；无「计时整栏替换再收回」专用 UX。",
        "todos": ["TODO: TempAbilityKit（授予/计时/收回）运行时 + 命令卡绑定"],
    },
    "auto-cast-toggle-ability": {
        "ludots": "玩家绿点自动施法：缺失。现有是 AI Utility Autocast，不是玩家开关。",
        "todos": ["TODO: 每技能 Autocast 开关写入偏好并参与 CastCommit"],
    },
    "loco-wasd-camera": {
        "ludots": "WASD：AxisMoveOrderSystem + ControlScheme 轴绑定；镜头相对需相机/朝向约定。",
    },
    "dyn-ctx-same-key-swap": {
        "ludots": "同一键改动词：缺失产品层；GAS PromptInput 只能做响应式提示，不做探测优先级。",
        "todos": ["TODO: ProximityContextProbe + 交互动词优先级表"],
    },
    "touch-drag-deploy-card": {
        "ludots": "皇室战争式拖卡：缺失。无手牌区→战场落点的触控拖拽主链。",
        "todos": ["TODO: TouchDragCast（手指拖卡片→地面/车道部署）"],
    },
    "menu-rotk-layered": {
        "ludots": "三国志式分层选单：CommandDeck/面板可做一页技能格；无武将→指令→目标多层栈。",
        "todos": ["TODO: MenuCommandStack（推入/弹出/目标阶段）"],
    },
}


def enrich_case(c: dict[str, Any]) -> dict[str, Any]:
    cat = c.get("category", "")
    default_impl, default_todos = CATEGORY_DEFAULTS.get(
        cat,
        ("未归类：先对照 RFC-0065 与 Input/Order 管线再标。", ["TODO: 补实现标注"]),
    )
    ov = CASE_OVERRIDES.get(c["id"], {})
    c["ludots"] = ov.get("ludots", c.get("ludots") or default_impl)
    todos = ov.get("todos", c.get("todos") or default_todos)
    c["todos"] = list(todos)
    return c


def enrich_all(cases: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [enrich_case(c) for c in cases]
