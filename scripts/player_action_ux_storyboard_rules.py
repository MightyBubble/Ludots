# -*- coding: utf-8 -*-
"""分镜「文案承诺 vs 画面元件」对照规则（SSOT）。

图鉴的承诺是：每一拍的「画面输出」文案说了什么，右边分镜就要画出来。
这里把可机器判定的部分列成表，生成时逐拍核对，不符直接 fail。

规则只在「肯定语气」下生效：文案说「选框消失」「菜单关闭」时不要求画出来。
"""
from __future__ import annotations

import re

# 出现这些词说明该元素正在消失/被拒绝，不要求画出来
NEGATIONS = ("消失", "收起", "关闭", "取消", "灭", "不出", "没有", "移除", "解除", "退出", "结束")

# 平台 → 该平台不该出现的元件（画错设备等于骗玩家）
PLATFORM_FORBIDDEN_ELEMENTS = {
    "kbm": ("stickL", "stickR", "touchpt"),
    "gamepad": ("cursor", "touchpt", "wasd"),
    "touch": ("cursor", "stickL", "stickR", "wasd"),
}

# 只可能出现在键盘上的键帽文字。手柄 / 触控 case 画这些键就是让玩家去按不存在的键。
KEYBOARD_ONLY_KEYCAPS = frozenset({
    "W", "S", "D", "E", "Q", "R", "F", "G", "T", "V", "C", "Z", "X",
    "Shift", "Ctrl", "Alt", "Esc", "Tab", "空格", "左键", "右键", "中键", "滚轮",
})

# 平台 → 该平台文案里不该出现的设备词
PLATFORM_FORBIDDEN_WORDS = {
    # 键位缩写要独立成词才算手柄键，"RTS" 里的 RT 不是扳机
    "kbm": r"摇杆|扳机|手柄|十字键|(?<![A-Za-z])(?:LB|RB|LT|RT)(?![A-Za-z])",
    "gamepad": r"鼠标|右键|左键|键盘|Shift\+|Ctrl\+|Alt\+",
    "touch": r"鼠标|右键|左键|摇杆|扳机",
}

# 元件参数的合法取值。渲染器只认这些；写别的会静默退化成默认样子，玩家看到的就是错的画。
ELEMENT_ENUMS: dict[tuple[str, str], frozenset] = {
    ("unit", "team"): frozenset({"ally", "enemy", "neutral"}),
    ("unit", "layer"): frozenset({"ground", "air", None}),
    ("unit", "state"): frozenset({"normal", "downed", None}),
    ("building", "team"): frozenset({"ally", "enemy", "neutral", None}),
    ("cursor", "mode"): frozenset({"idle", "down", "drag", "up", "aim"}),
    ("ring", "kind"): frozenset({"select", "lock", "buff"}),
    ("arrow", "kind"): frozenset({"move", "attack"}),
    ("path", "kind"): frozenset({"lasso", "arc", "move"}),
    ("bar", "kind"): frozenset({"cast", "charge", "hp"}),
    ("key", "state"): frozenset({"idle", "active", "off"}),
    ("touchpt", "kind"): frozenset({"tap", "hold", "drag", "pinch"}),
    ("prop", "kind"): frozenset({"item", "ore", "herb", "chest", "door", "corpse", None}),
    ("vehicle", "kind"): frozenset({"car", "tank", "turret", "mount"}),
    ("hero", "state"): frozenset({"alive", "ghost", None}),
    ("npc", "role"): frozenset({"vendor", "quest", "healer", "auction", "trainer", None}),
    ("crosshair", "spread"): frozenset({"tight", "wide", None}),
    ("queue", "state"): frozenset({"waiting", "active", "done"}),
    ("camera", "mode"): frozenset({"lock", "free"}),
    ("splitscreen", "mode"): frozenset({"v", "h", "shared"}),
    ("netstat", "state"): frozenset({"ok", "lag", "lost"}),
    ("voice", "state"): frozenset({"off", "on", "talking"}),
}

# 一拍里最多出现一次的元件（多了就是画重了）
SINGLETON_ELEMENTS = ("hotbar", "badge", "stickL", "stickR", "bar")

# 必须有「看得见的主体」，只有文字标签不算画面
SUBJECT_ELEMENTS = (
    "unit", "hero", "card", "building", "crosshair", "menu", "stickL", "stickR",
    "key", "hotbar", "wasd", "wheel", "anchor", "touchpt", "prop",
    "vehicle", "corpse", "npc", "deny", "impact", "held", "queue", "camera",
    "playertag", "splitscreen", "padslot", "roster", "netstat", "voice", "vote",
)


def _has(cast: list, *types: str) -> bool:
    return any(e.get("t") in types for e in cast)


def _selected_unit(cast: list) -> bool:
    return any(e.get("t") == "unit" and e.get("sel") for e in cast)


def _ring(cast: list, *kinds: str) -> bool:
    return any(e.get("t") == "ring" and (not kinds or e.get("kind") in kinds) for e in cast)


def _cursor_mode(cast: list, *modes: str) -> bool:
    return any(e.get("t") == "cursor" and e.get("mode") in modes for e in cast)


def _team_unit(cast: list, team: str) -> bool:
    return any(e.get("t") in ("unit", "building") and e.get("team") == team for e in cast)


# (说明, 触发正则, 画面判定, 缺了要怎么补)
PROMISE_RULES: tuple[tuple[str, str, object, str], ...] = (
    (
        "说变专属准星/开镜，画面要有准星",
        r"专属准星|变准星|开镜|瞄准镜",
        lambda cast: _has(cast, "crosshair") or _cursor_mode(cast, "aim"),
        "补 crosshair(...) 或把 cursor 改成 mode='aim'",
    ),
    (
        "说点菜单项，画面要有菜单",
        r"菜单项",
        lambda cast: _has(cast, "menu"),
        "补 menu_box(..., active=被点中那项)",
    ),
    (
        "说弹出/展开菜单或选单，画面要有菜单或轮盘",
        r"弹出菜单|展开菜单|出现菜单|上下文菜单|次级菜单|分层选单",
        lambda cast: _has(cast, "menu", "wheel", "card"),
        "补 menu_box(...) 或 wheel(...) 轮盘",
    ),
    (
        "说出现选中圈/被选中，画面要有选中圈",
        r"选中圈|被选中|变成当前选中|设为选中",
        lambda cast: _ring(cast, "select", "lock") or _selected_unit(cast),
        "给 unit 加 sel=True 或补 ring(x, y, kind='select')",
    ),
    (
        "说读条/倒计时/蓄力，画面要有进度条",
        r"读条|进度条|倒计时|蓄力条|充能条|条走满",
        lambda cast: _has(cast, "bar"),
        "补 bar(x, y, ratio=..., kind='cast'/'charge')",
    ),
    (
        "说落点圈/范围预览，画面要有落点圈",
        r"落点圈|范围圈|落点预览|范围预览|作用范围",
        lambda cast: _has(cast, "circle", "cone"),
        "补 circle_ind(x, y, r, ok=True/False)",
    ),
    (
        "说扇形/锥形，画面要有扇形指示器",
        r"扇形|锥形|锥形区|矩形扫射",
        lambda cast: _has(cast, "cone"),
        "补 cone(x, y, angle, spread, length)",
    ),
    (
        "说推摇杆，画面要有摇杆图示",
        r"摇杆",
        lambda cast: _has(cast, "stickL", "stickR"),
        "补 stick('L'/'R', nx, ny)",
    ),
    (
        "说拖出选框，画面要有选框",
        r"选框|框住|矩形框",
        lambda cast: _has(cast, "box"),
        "补 box(x, y, w, h)",
    ),
    (
        "说技能栏/图标/冷却，画面要有技能栏",
        r"技能栏|技能图标|快捷栏|冷却|CD|绿点|亮起可再次",
        lambda cast: _has(cast, "hotbar"),
        "补 hotbar(active=..., cd=..., dot=..., deny=...)",
    ),
    (
        "说键位提示，画面要有键帽",
        r"按键提示|交互提示|提示键|提示「|键提示|键位提示",
        lambda cast: _has(cast, "key", "hotbar", "wasd"),
        "补 keyhint(x, y, label, state, hint)",
    ),
    (
        # 键位字母要独立成词，"按住 Shift" 里的 S 不是方向键
        "说 WASD 方向键，画面要有键组",
        r"WASD|按住?\s?[WASD](?![A-Za-z])",
        lambda cast: _has(cast, "wasd", "key"),
        "补 wasd(active=[...]) 方向键组",
    ),
    (
        "说轮盘，画面要有轮盘",
        r"轮盘",
        lambda cast: _has(cast, "wheel"),
        "补 wheel(x, y, labels, active)",
    ),
    (
        "说锚点，画面要有锚点",
        r"锚点",
        lambda cast: _has(cast, "anchor"),
        "补 anchor(x, y)",
    ),
    (
        "说敌人/敌方，画面要有敌方目标",
        r"敌人|敌方|敵",
        lambda cast: _team_unit(cast, "enemy"),
        "补 unit(x, y, team='enemy') 或 building(x, y, team='enemy')",
    ),
    (
        "说手牌/卡，画面要有卡牌",
        r"手牌|卡牌|一张卡|卡片",
        lambda cast: _has(cast, "card"),
        "补 card(x, y, label, cost)",
    ),
    (
        "说轨迹/套索，画面要有轨迹线",
        r"轨迹|套索",
        lambda cast: _has(cast, "path"),
        "补 path([[x, y], ...], kind='lasso')",
    ),
    (
        "说移瞄分离/边走边打，移动和攻击两个方向都要画出来",
        r"移瞄分离|边走边打|边退边打|走的方向.*打|往上走.*朝右",
        lambda cast: any(e.get("t") == "arrow" and e.get("kind") == "move" for e in cast)
        and any(e.get("t") == "arrow" and e.get("kind") == "attack" for e in cast),
        "补一根 arrow(kind='move') 表示走的方向，和 attack 那根并排",
    ),
    (
        "说分屏，画面要有分屏框",
        r"分屏|上下分|左右分|各占半屏",
        lambda cast: _has(cast, "splitscreen"),
        "补 splitscreen(mode='v'/'h'/'shared')",
    ),
    (
        "说第几号玩家，画面要标出是谁",
        r"P1|P2|P3|一号玩家|二号玩家|哪个是我",
        lambda cast: _has(cast, "playertag", "padslot", "splitscreen", "roster"),
        "补 playertag(x, y, 'P1'/'P2') 或 padslot/roster",
    ),
    (
        "说延迟/卡/掉线，画面要有网络状况",
        r"延迟|掉线|断线|网络中断|重连计时|重连态",
        lambda cast: _has(cast, "netstat"),
        "补 netstat(x, y, ping, state='ok'/'lag'/'lost')",
    ),
    (
        "说麦克风/语音，画面要有麦克风",
        r"麦克风|语音|按住说话|说话",
        lambda cast: _has(cast, "voice"),
        "补 voice(x, y, state='off'/'on'/'talking')",
    ),
    (
        "说表决/投票，画面要有票数进度",
        r"表决|投票|几票|同意",
        lambda cast: _has(cast, "vote"),
        "补 vote(x, y, yes, need)",
    ),
    (
        "说准备就绪/房间名单，画面要有名单",
        r"全员就绪|房间名单|全绿|点准备|已准备|未准备|还没准备",
        lambda cast: _has(cast, "roster"),
        "补 roster(x, y, rows)",
    ),
    (
        "说手柄加入/断开，画面要有手柄槽位",
        r"手柄加入|按键加入|插上手柄|拔了手柄|手柄断",
        lambda cast: _has(cast, "padslot"),
        "补 padslot([...]) 手柄槽位条",
    ),
    (
        "说相机/镜头本体在动，画面要有相机",
        r"相机",
        lambda cast: _has(cast, "camera"),
        "补 camera(x, y, angle, mode)；别拿 arrow(kind='move') 代替镜头，那是角色在走",
    ),
    (
        "说排队/依次出手，画面要有队列序号",
        r"排队|依次点|依次出手|一个接一个",
        lambda cast: _has(cast, "queue"),
        "补 queue_no(x, y, n, state) 头顶序号，别拿 keyhint 当序号",
    ),
    (
        "说被拒绝/禁止，画面要有禁止图标",
        r"拒绝|禁止图标|放不出|不可用|无效|挡下",
        lambda cast: _has(cast, "deny"),
        "补 deny(x, y, label='原因')",
    ),
    (
        "说商人/NPC/魂匠，画面要有 NPC 小人",
        r"商人|NPC|魂匠|拍卖师|任务给予者|飞行管理员",
        lambda cast: _has(cast, "npc"),
        "补 npc(x, y, role=...)",
    ),
    (
        "说触控手指动作，画面要有触点",
        r"手指|点触|轻点|双指|捏合|滑动",
        lambda cast: _has(cast, "touchpt"),
        "补 touch_point(x, y, kind=...) 触点元件",
    ),
)


def _negated(text: str, keyword_span: tuple[int, int]) -> bool:
    """关键词附近出现「消失/关闭」等词就不强制画出来。"""
    start = max(0, keyword_span[0] - 12)
    end = min(len(text), keyword_span[1] + 12)
    window = text[start:end]
    return any(n in window for n in NEGATIONS)


def check_beat(beat: dict) -> list[tuple[str, str]]:
    """返回 [(规则说明, 补法)]，空表示这一拍画面兑现了文案。"""
    cast = beat.get("cast") or []
    text = f"{beat.get('input') or ''} {beat.get('logic') or ''} {beat.get('screen') or ''}"
    out: list[tuple[str, str]] = []
    for why, pattern, ok, howto in PROMISE_RULES:
        hit = None
        for m in re.finditer(pattern, text):
            if not _negated(text, m.span()):
                hit = m
                break
        if hit is None:
            continue
        if not ok(cast):
            out.append((why, howto))
    return out


def check_structure(beat: dict) -> list[str]:
    """画面本身的合法性：主体存在、坐标在台上、元件不画重。"""
    cast = beat.get("cast") or []
    problems: list[str] = []
    if not any(e.get("t") in SUBJECT_ELEMENTS for e in cast):
        problems.append("这一拍没有看得见的主体（只有文字标签不算画面）")
    for e in cast:
        for key in ("x", "y", "x1", "y1", "x2", "y2"):
            if key in e and e[key] is not None and not 0 <= float(e[key]) <= 100:
                problems.append(f"{e['t']} 的 {key}={e[key]} 跑出画面（要在 0..100）")
    for t in SINGLETON_ELEMENTS:
        n = sum(1 for e in cast if e.get("t") == t)
        if n > 1:
            problems.append(f"{t} 画了 {n} 个（一拍最多一个）")
    for e in cast:
        for (et, key), allowed in ELEMENT_ENUMS.items():
            if e.get("t") == et and key in e and e[key] not in allowed:
                problems.append(
                    f"{et} 的 {key}={e[key]!r} 渲染器不认（只能是 "
                    f"{sorted(x for x in allowed if x is not None)}），会静默画错"
                )
    return problems


def check_platform(case: dict) -> list[str]:
    """平台标注 vs 画面元件 / 文案设备词是否自相矛盾。"""
    platform = case.get("platform")
    if platform not in PLATFORM_FORBIDDEN_ELEMENTS:
        return [f"platform={platform} 不在 gamepad/kbm/touch 之内"]
    problems: list[str] = []
    forbidden = PLATFORM_FORBIDDEN_ELEMENTS[platform]
    for i, b in enumerate(case.get("beats") or []):
        for e in b.get("cast") or []:
            if e.get("t") in forbidden:
                problems.append(f"T{i+1} 画了 {e['t']}，和 platform={platform} 矛盾")
            if (
                platform != "kbm"
                and e.get("t") == "key"
                and str(e.get("label")) in KEYBOARD_ONLY_KEYCAPS
            ):
                problems.append(
                    f"T{i+1} 画了键盘键帽「{e['label']}」，platform={platform} 上没有这个键"
                )
    if case.get("crossDevice"):
        # 这条 case 本身讲的就是「两种设备同时在场」，文案必须提到对方设备。
        # 只豁免文案检查，画面元件仍不许画错设备。
        return problems
    blob = " ".join(
        [case.get("title") or "", case.get("summary") or ""]
        + [f"{b.get('input')} {b.get('logic')} {b.get('screen')}" for b in case.get("beats") or []]
    )
    words = sorted(set(re.findall(PLATFORM_FORBIDDEN_WORDS[platform], blob)))
    if words:
        problems.append(f"文案出现别的平台设备词 {words}，和 platform={platform} 矛盾")
    return problems
