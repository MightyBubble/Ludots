#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate player-action UX catalog data (SSOT → catalog-data.js).

Player/PM language only. Each beat is a closed loop:
  设备输入 → 逻辑处理 → 画面输出
Logic copy SSOT: player_action_ux_beat_logic.py (not a 'feel' swimlane).
Storyboard visuals are comic-panel descriptors for the HTML renderer.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from player_action_ux_action_index import (  # noqa: E402
    ACTION_GROUPS,
    PLATFORM_LABEL,
    PLATFORM_ORDER,
)
from player_action_ux_beat_logic import apply_beat_logic  # noqa: E402
from player_action_ux_impl_notes import enrich_all  # noqa: E402
from player_action_ux_storyboard_rules import (  # noqa: E402
    check_beat,
    check_platform,
    check_structure,
)

OUT = Path(__file__).resolve().parent.parent / "gitbook/reference/player-action-ux/catalog-data.js"
CHECKPOINT_MD = Path(__file__).resolve().parent.parent / "gitbook/reference/player-action-ux/CHECKPOINT.md"

# 分镜镜位（角标用人话，不给玩家看英文代号）
VIEW_LABELS = {
    "topdown": "俯视战场",
    "moba": "斜俯视",
    "tps": "越肩视角",
    "fps": "第一人称",
}

# 光标状态：渲染器逐个都画得出来，未列出的状态直接 fail
CURSOR_MODES = ("idle", "down", "drag", "up", "aim")

# 技能栏格子上印什么，取决于玩家手里是什么设备
HOTBAR_KEYCAPS = {
    "kbm": ("Q", "W", "E", "R", "T"),
    "gamepad": ("Y", "B", "X", "A", "LB"),
    "touch": ("①", "②", "③", "④", "⑤"),
}

# 光标是箭头时会压住脚下的东西，这些状态要让位；aim 是准星、drag 是抓着，压着才对
CURSOR_MODES_OFFSET = ("idle", "down", "up")
CURSOR_OCCLUDERS = ("unit", "hero", "building")

# 舞台像素尺寸必须和 catalog-app.js 的 SW / SH 一致
STAGE_W_PX = 284.0
STAGE_H_PX = 150.0
# 箭头图形从箭尖（0,0）往右下伸展出的包围盒
CURSOR_BODY_PX = (13.0, 20.0)


def _entity_radius_px(e: dict) -> float:
    t = e.get("t")
    if t == "unit":
        return 8.0 * (e.get("size") or 1)
    if t == "hero":
        return 9.0
    if t == "building":
        return 12.0
    raise SystemExit(f"未登记的遮挡体半径: {t}")


def _cursor_covers(cur: dict, ent: dict) -> bool:
    """箭头光标是否把这个实体压住（按箭头真实包围盒 + 实体半径算）。"""
    dx = (ent["x"] - cur["x"]) / 100.0 * STAGE_W_PX
    dy = (ent["y"] - cur["y"]) / 100.0 * STAGE_H_PX
    r = _entity_radius_px(ent)
    return -r <= dx <= CURSOR_BODY_PX[0] + r and -r <= dy <= CURSOR_BODY_PX[1] + r


def beat(input_text, screen, _author_note, view, cast, title=None):
    """Build one storyboard beat.

    Positional 3rd arg is a discarded authoring stub (old 'feel' slogans).
    Final ``logic`` text is applied from ``BEAT_LOGIC`` — required, no silent default.
    """
    return {
        "title": title,
        "input": input_text,
        "screen": screen,
        "view": view,
        "cast": cast,
    }


def case(cid, category, title, summary, beats, genres=None, ludots=None, todos=None,
         cross_device=False):
    """category = functional family (for impl_notes). Nav taxonomy is targets[] (game recreations)."""
    row = {
        "id": cid,
        "category": category,  # functional family, e.g. select / twin-stick
        "family": category,
        "title": title,
        "summary": summary,
        "genres": genres or [],
        "targets": [],  # filled by assign_game_targets()
        "beats": beats,
    }
    if ludots:
        row["ludots"] = ludots
    if todos:
        row["todos"] = list(todos)
    if cross_device:
        # 这条动作本身讲「两种设备同时在场」，文案必然提到对方设备
        row["crossDevice"] = True
    return row


# ---- visual helpers (normalized 0..100 stage coords) ----
def unit(x, y, sel=False, team="ally", face=0, size=1,
         role=None, layer=None, state=None, highlight=False):
    """场上单位。role=兵种缩写（工/坦/医），layer='air' 画成腾空带投影，
    state='downed' 画成倒地，highlight=白色虚线描边（被瞄上/被提示）。"""
    row = {"t": "unit", "x": x, "y": y, "sel": sel, "team": team, "face": face, "size": size}
    if role:
        row["role"] = role
    if layer:
        row["layer"] = layer
    if state:
        row["state"] = state
    if highlight:
        row["highlight"] = True
    return row


def cursor(x, y, mode="idle"):
    return {"t": "cursor", "x": x, "y": y, "mode": mode}


def box(x, y, w, h):
    return {"t": "box", "x": x, "y": y, "w": w, "h": h}


def stick(side, nx, ny):
    return {"t": "stickL" if side == "L" else "stickR", "nx": nx, "ny": ny}


def crosshair(x, y, locked=False, spread=None):
    """FPS 准星。spread='tight' 开镜收拢 / 'wide' 扫射扩散，不给就是常态。"""
    row = {"t": "crosshair", "x": x, "y": y, "locked": locked}
    if spread:
        row["spread"] = spread
    return row


def ring(x, y, r=8, kind="select"):
    return {"t": "ring", "x": x, "y": y, "r": r, "kind": kind}


def cone(x, y, angle=0, spread=40, length=28):
    return {"t": "cone", "x": x, "y": y, "angle": angle, "spread": spread, "length": length}


def arrow(x1, y1, x2, y2, kind="move"):
    return {"t": "arrow", "x1": x1, "y1": y1, "x2": x2, "y2": y2, "kind": kind}


def circle_ind(x, y, r=16, ok=True):
    return {"t": "circle", "x": x, "y": y, "r": r, "ok": ok}


def building(x, y, ghost=False, team=None):
    """team 标出阵营描边（敌方红 / 己方绿），不给就是中立灰。"""
    return {"t": "building", "x": x, "y": y, "ghost": ghost, "team": team}


def badge(text):
    return {"t": "badge", "text": text}


def path(points, kind="move"):
    return {"t": "path", "points": points, "kind": kind}


def hero(x, y, face=0, state=None, form=None):
    """玩家角色。state='ghost' 半透明灵魂；form='alt' 换了形态（轮廓变尖角紫色）。"""
    row = {"t": "hero", "x": x, "y": y, "face": face}
    if state:
        row["state"] = state
    if form:
        row["form"] = form
    return row


def marker(x, y, icon="skull", label="团队标记"):
    """挂在目标头上的团队标记（骷髅/月亮…）。别用键帽画标记，玩家会去按那个键。"""
    return {"t": "marker", "x": x, "y": y, "icon": icon, "label": label}


def vehicle(x, y, kind="car", occupied=False):
    """载具 / 炮位。kind=car 车 / tank 坦克 / turret 炮台 / mount 坐骑；occupied=有人坐进去了。"""
    return {"t": "vehicle", "x": x, "y": y, "kind": kind, "occupied": occupied}


def corpse(x, y):
    """尸体（跑尸、拾取尸体掉落）。"""
    return {"t": "corpse", "x": x, "y": y}


def held(label=None):
    """手持物槽，固定画在画面左上；不给 label 就是空手。
    「手里拿着什么决定了交互动词」这类动作必须每拍都画它，位置不能变。"""
    return {"t": "held", "label": label}


def impact(x, y, r=16, heavy=False):
    """命中 / 已经炸开：放射线 + 实心圈。和表示「这里可以放」的绿色落点圈区分开。"""
    return {"t": "impact", "x": x, "y": y, "r": r, "heavy": heavy}


def deny(x, y, label=None, r=11):
    """红圈斜杠禁止图标：这一下被系统挡住了，不能只靠文字说。"""
    return {"t": "deny", "x": x, "y": y, "label": label, "r": r}


def npc(x, y, role=None):
    """中立 NPC 小人。role=vendor 商 / quest 任务 / healer 魂匠 / auction 拍卖师 / trainer 训练师。"""
    return {"t": "npc", "x": x, "y": y, "role": role}


def fog(x=60, y=0, w=40, h=100):
    """战争迷雾遮罩，盖住看不见的那片区域。"""
    return {"t": "fog", "x": x, "y": y, "w": w, "h": h}


def card(x, y, label="卡", cost=None, dragging=False):
    return {"t": "card", "x": x, "y": y, "label": label, "cost": cost, "dragging": dragging}


def menu_box(x, y, lines, active=None):
    """active = 被点中的那一项（0 起），用于「点了哪一项」看得见。"""
    return {"t": "menu", "x": x, "y": y, "lines": list(lines), "active": active}


def playertag(x, y, label="P1", color="p1"):
    """玩家标识牌，挂在角色头上。同屏多人时玩家最先要认出「哪个是我」。"""
    return {"t": "playertag", "x": x, "y": y, "label": label, "color": color}


def splitscreen(mode="v"):
    """分屏框。mode=v 左右分 / h 上下分 / shared 共享一块屏。"""
    return {"t": "splitscreen", "mode": mode}


def padslot(states):
    """手柄槽位条。每格 joined 已加入 / waiting 等着按键加入 / lost 断开了。"""
    return {"t": "padslot", "states": list(states)}


def partyframe(x, y, rows, target=None):
    """队伍头像框：竖排头像 + 血条。rows = [{"name": ..., "hp": 0..1}]，
    target = 当前友好目标那一行（高亮）。这是界面上的队伍框，不是菜单。"""
    return {"t": "partyframe", "x": x, "y": y, "rows": list(rows), "target": target}


def roster(x, y, rows, title="房间"):
    """房间玩家名单。rows = [{"name": ..., "state": "ready|waiting|offline"}]。"""
    return {"t": "roster", "x": x, "y": y, "rows": list(rows), "title": title}


def netstat(x, y, ping=40, state="ok"):
    """网络状况：信号格 + 延迟数字。state=ok 顺 / lag 卡 / lost 断了。"""
    return {"t": "netstat", "x": x, "y": y, "ping": ping, "state": state}


def voice(x, y, state="off"):
    """麦克风。state=off 关 / on 开着 / talking 正在说话。"""
    return {"t": "voice", "x": x, "y": y, "state": state}


def vote(x, y, yes=0, need=5, label="表决"):
    """表决进度条，画出「还差几票」。"""
    return {"t": "vote", "x": x, "y": y, "yes": yes, "need": need, "label": label}


def camera(x, y, angle=0, mode="free"):
    """镜头本体（带视锥）。mode=lock 贴背跟随 / free 自由转。
    镜头是主角时必须画出来，不能拿「角色移动箭头」代替。"""
    return {"t": "camera", "x": x, "y": y, "angle": angle, "mode": mode}


def queue_no(x, y, n, state="waiting"):
    """单位头顶的排队序号。state=waiting 排队 / active 正在放 / done 放完了。
    别拿键帽当序号，玩家会以为要去按那个键。"""
    return {"t": "queue", "x": x, "y": y, "n": n, "state": state}


def hotbar(active=None, cd=None, extra=None, off=None, dot=None, deny=None, slots=4,
           page=None, defer=None):
    """Bottom-center skill row. active=pressed slot, cd=cooldown sweep,
    extra=temp-granted slot (green), off=removed/disabled slots, dot=autocast
    green dot, deny=rejected press (red flash)."""
    row = {"t": "hotbar", "slots": slots, "active": active, "cd": cd,
           "extra": extra, "off": list(off or []), "dot": dot, "deny": deny}
    if page:
        row["page"] = page
    if defer is not None:
        # 让路：这一颗本来要自动放，被手动抢占后延后，不是「不可用」
        row["defer"] = defer
    return row


def bar(x=50, y=30, ratio=0.6, kind="cast", label=None, broken=False):
    """Progress bar: kind=cast(blue)/charge(orange)/hp(green); broken=interrupted."""
    return {"t": "bar", "x": x, "y": y, "ratio": ratio, "kind": kind,
            "label": label, "broken": broken}


def touch_point(x, y, kind="tap", x2=None, y2=None):
    """手指触点。kind=tap 轻点 / hold 长按 / drag 拖动 / pinch 双指（要给 x2,y2）。"""
    return {"t": "touchpt", "x": x, "y": y, "kind": kind, "x2": x2, "y2": y2}


def wasd(active=None):
    """WASD 方向键组，画在画面左下角；active 列出按下的键，如 ["W"]。"""
    return {"t": "wasd", "active": list(active or [])}


def wheel(x, y, labels, active=None, r=30):
    """径向轮盘：按住弹出、指向一格松手选定。"""
    return {"t": "wheel", "x": x, "y": y, "labels": list(labels), "active": active, "r": r}


def anchor(x, y):
    """钩索/绳索可挂的锚点。"""
    return {"t": "anchor", "x": x, "y": y}


def prop(x, y, label=None, highlight=False, kind=None):
    """可交互物件（果子 / 箱子 / 矿脉 / 门 / 可搬物），带名字标签；highlight=已被点亮。"""
    row = {"t": "prop", "x": x, "y": y, "label": label, "highlight": highlight}
    if kind:
        row["kind"] = kind
    return row


def keyhint(x, y, label="F", state="idle", hint=None):
    """Key prompt cap. state=idle/active/off; hint=verb text above the key."""
    return {"t": "key", "x": x, "y": y, "label": label, "state": state, "hint": hint}


# Left-nav taxonomy = recreation targets (同一 case 可挂多个游戏，重复出现是预期)。
# Functional family 仍写在 case.category / case.family，供 impl_notes 与详情副标。
TARGET_GAMES = [
    ("sc2", "星际争霸2", "框选 · 指令队列 · 控制组 · 热键栏"),
    ("ra2", "红色警戒2", "生产建造 · 电力 · 右键语境指令"),
    ("war3", "魔兽争霸3", "英雄技 · 物品栏 · RTS 混战"),
    ("lol", "英雄联盟", "QWER · 技能瞄准 · 补刀走位"),
    ("wow", "魔兽世界", "技能栏 · 读条 · 任务与社交循环"),
    ("clash", "皇室战争", "拖卡部署 · 圣水 · 触控车道"),
    ("rotk", "三国志式选单", "武将 → 指令 → 目标分层菜单"),
    ("gow", "战神式动作", "近战连段 · 临时武器栏 · 闪避窗"),
    ("diablo", "暗黑式 ARPG", "点地走打 · 技能落点 · 刷宝"),
    ("twin", "双摇杆射击", "左走右瞄 · 弹幕清屏"),
    ("fps", "FPS / TPS", "准星 · 开镜 · 射击换弹"),
    ("zelda", "塞尔达 / 开放世界", "情境按键 · 攀爬采集互动"),
    ("netmatch", "联机对局", "建房匹配 · 准备开局 · 掉线重连"),
    ("couch", "同屏 / 分屏双人", "手柄加入 · 镜头拉扯 · 抢拾取"),
    ("shared", "跨品类通用", "拒绝反馈 · 设计手势 · 共通走位"),
]

# Back-compat alias: generator / checkpoint still say CATEGORIES
CATEGORIES = [(gid, title) for gid, title, _blurb in TARGET_GAMES]
FAMILY_TITLES = {
    "select": "谁听我的",
    "basic-order": "常规指令",
    "aim": "对准世界",
    "attack": "基本攻击与射击",
    "twin-stick": "双摇杆射击",
    "instant-skill": "不用瞄的技能",
    "unit-skill": "要选单位的技能",
    "ground-skill": "要点地面的技能",
    "direction-skill": "要选方向的技能",
    "hold": "按住不放",
    "combo": "一段接一段",
    "defense": "防 / 躲 / 反击窗",
    "environment": "和环境互动",
    "army": "部队 / 宝宝 / 载具",
    "cast-habit": "同技能不同手感",
    "multi-cast": "一群人放同一个技能",
    "context-order": "选中谁×点到谁",
    "temp-kit": "临时多出来的技能",
    "item": "物品：捡 / 用 / 装 / 拖",
    "mmo-social": "MMO：人、对话、队伍",
    "mmo-world": "MMO：采集、买卖、坐骑、复活",
    "design-ui": "设计向界面手势",
    "dynamic-context": "身边有什么，同一键变什么",
    "auto-cast": "自动施法",
    "locomotion": "走路：WASD / 摇杆 / 点地",
    "touch-tablet": "平板触控 / 卡牌拖放",
    "menu-cmd": "选单式指令",
    "blocked": "放不了时的反馈",
    "netplay": "联机：进一局并待在里面",
    "couch-play": "同屏多人：加入、分屏、抢东西",
}

# genre 标签 → 复刻目标（可一对多）
_GENRE_TO_TARGETS: dict[str, tuple[str, ...]] = {
    "SC2": ("sc2",),
    "SC2/War3": ("sc2", "war3"),
    "War3": ("war3",),
    "魔兽争霸3": ("war3",),
    "RTS英雄": ("war3", "sc2"),
    "RTS超武": ("sc2", "ra2"),
    "RA2": ("ra2",),
    "C&C": ("ra2",),
    "RTS": ("sc2", "ra2", "war3"),
    "RTS触控": ("sc2", "ra2", "clash"),
    "RTS面板": ("sc2", "ra2", "war3"),
    "LoL": ("lol",),
    "LoL/Dota": ("lol",),
    "MOBA": ("lol",),
    "MOBA补刀走位": ("lol",),
    "MOBA触控": ("lol", "clash"),
    "魔兽世界": ("wow",),
    "MMO": ("wow",),
    "MMO生活系": ("wow",),
    "皇室战争": ("clash",),
    "卡牌RTS": ("clash",),
    "卡牌": ("clash",),
    "COC式": ("clash",),
    "平板": ("clash",),
    "三国志": ("rotk",),
    "回合策略": ("rotk",),
    "战棋": ("rotk",),
    "策略": ("rotk", "ra2"),
    "4X": ("rotk",),
    "战神": ("gow",),
    "战神4": ("gow",),
    "魂like": ("gow",),
    "合作动作": ("gow",),
    "暗黑": ("diablo",),
    "ARPG": ("diablo", "gow"),
    "动作RPG": ("diablo", "gow"),
    "双摇杆射击": ("twin",),
    "双摇杆": ("twin",),
    "手柄": ("twin", "gow"),
    "FPS": ("fps",),
    "TPS": ("fps",),
    "逃离塔科夫": ("fps",),
    "塞尔达": ("zelda",),
    "开放世界": ("zelda",),
    "刺客信条": ("zelda",),
    "蝙蝠侠": ("zelda", "gow"),
    "蝙蝠侠/蜘蛛侠": ("zelda", "gow"),
    "蜘蛛侠": ("zelda",),
    "浸入式模拟": ("zelda",),
    "潜行游戏": ("zelda", "fps"),
    "AVG": ("rotk",),
    "经营": ("ra2", "rotk"),
    "载具": ("fps", "gow"),
    "设计选项": ("shared",),
    "全品类": ("shared",),
    "联机对局": ("netmatch",),
    "竞技匹配": ("netmatch", "lol"),
    "开黑组队": ("netmatch", "wow"),
    "同屏双人": ("couch",),
    "分屏": ("couch", "fps"),
    "派对游戏": ("couch",),
}

# 功能族默认挂到哪些复刻目标（genres 为空或偏抽象时兜底；可与 genres 叠加）
_FAMILY_TO_TARGETS: dict[str, tuple[str, ...]] = {
    "twin-stick": ("twin",),
    "touch-tablet": ("clash",),
    "menu-cmd": ("rotk",),
    "mmo-social": ("wow",),
    "mmo-world": ("wow",),
    "design-ui": ("shared",),
    "temp-kit": ("gow", "war3"),
    "dynamic-context": ("zelda", "wow"),
    "auto-cast": ("wow", "sc2", "war3"),
    "blocked": ("shared",),
    "netplay": ("netmatch",),
    "couch-play": ("couch",),
    "select": ("sc2", "ra2", "war3", "lol"),
    "basic-order": ("sc2", "ra2", "war3"),
    "context-order": ("sc2", "ra2", "war3"),
    "army": ("sc2", "ra2", "war3", "wow"),
    "multi-cast": ("sc2", "ra2", "war3"),
    "cast-habit": ("sc2", "lol", "wow"),
}


def assign_game_targets(cases: list) -> None:
    """Fill case['targets'] from genres + family + id hints. Duplicates across games are intentional."""
    valid = {gid for gid, _t, _b in TARGET_GAMES}
    for c in cases:
        hit: set[str] = set()
        for g in c.get("genres") or []:
            for t in _GENRE_TO_TARGETS.get(g, ()):
                hit.add(t)
        fam = c.get("family") or c.get("category") or ""
        if not hit:
            for t in _FAMILY_TO_TARGETS.get(fam, ()):
                hit.add(t)
        else:
            # 家族补充：触控/选单/双摇杆等强品类信号，即使已有 genres 也并上
            if fam in ("twin-stick", "touch-tablet", "menu-cmd", "design-ui", "mmo-social", "mmo-world"):
                for t in _FAMILY_TO_TARGETS.get(fam, ()):
                    hit.add(t)
        cid = c.get("id") or ""
        if cid.startswith("touch-") or "card" in cid or "elixir" in cid:
            hit.add("clash")
        if cid.startswith("menu-") or "rotk" in cid:
            hit.add("rotk")
        if "control-group" in cid or cid.startswith("select-box") or "hotkey" in cid:
            hit.update(("sc2", "war3"))
        if "wasd" in cid or cid.startswith("loco-wasd"):
            if not hit:
                hit.update(("diablo", "gow", "fps", "zelda"))
        if not hit:
            hit.add("shared")
        unknown = hit - valid
        if unknown:
            raise SystemExit(f"case {cid}: unknown targets {sorted(unknown)}")
        # stable order = TARGET_GAMES order
        order = {gid: i for i, (gid, _, _) in enumerate(TARGET_GAMES)}
        c["targets"] = sorted(hit, key=lambda t: order[t])
        c["familyTitle"] = FAMILY_TITLES.get(fam, fam)


def build_cases():
    c = []

    # ===== 一、谁听我的 =====
    c.append(case(
        "select-click", "select", "点一下选中一个单位",
        "鼠标点到某个单位，它成为当前指挥对象。",
        [
            beat("把指针移到单位上", "单位可被高亮/描边提示", "准备选中", "topdown",
                 [unit(40, 50), unit(62, 42), unit(55, 68), ring(62, 42, r=9, kind="select"),
                  cursor(62, 42), badge("悬停")], title="悬停"),
            beat("按下并松开左键", "该单位脚下出现选中圈，他人无圈", "它现在听你的", "topdown",
                 [unit(40, 50), unit(62, 42, sel=True), unit(55, 68), ring(62, 42), cursor(62, 42, "up"), badge("单击")], title="点选"),
        ], ["RTS", "MOBA", "SC2/War3"],
    ))
    c.append(case(
        "select-box", "select", "拖框选出一群",
        "按住拖出矩形，框内单位一起被选中。",
        [
            beat("在空地按下左键", "出现选框起点", "开始框选", "topdown",
                 [unit(35, 40), unit(48, 45), unit(58, 52), unit(70, 38),
                  box(28, 28, 12, 12), cursor(30, 30, "down"), badge("选框起点")], title="按下"),
            beat("拖动鼠标", "半透明选框扩大，框内单位闪一下", "还在框", "topdown",
                 [unit(35, 40, sel=True), unit(48, 45, sel=True), unit(58, 52, sel=True), unit(70, 38),
                  ring(35, 40), ring(48, 45), ring(58, 52), box(30, 30, 40, 30),
                  cursor(70, 60, "drag"), badge("拖动")], title="拖框"),
            beat("松开左键", "选框消失；框内单位全部带选中圈", "一队人听你的", "topdown",
                 [unit(35, 40, sel=True), unit(48, 45, sel=True), unit(58, 52, sel=True), unit(70, 38),
                  ring(35, 40), ring(48, 45), ring(58, 52), cursor(70, 60, "up"), badge("松开")], title="放开"),
        ], ["RTS", "C&C", "SC2/War3"],
    ))
    c.append(case(
        "select-lasso", "select", "套索圈出一群",
        "自由曲线围住单位再松开完成选择。",
        [
            beat("按下并沿路径拖动", "出现套索轨迹", "圈地中", "topdown",
                 [unit(40, 45), unit(55, 50), unit(50, 65), path([(35, 35), (70, 30), (75, 70), (30, 75), (35, 35)], "lasso"),
                  cursor(35, 35, "drag"), badge("套索")], title="画圈"),
            beat("松开", "圈内单位被选中", "圈住的都是你的", "topdown",
                 [unit(40, 45, sel=True), unit(55, 50, sel=True), unit(50, 65, sel=True),
                  ring(40, 45), ring(55, 50), ring(50, 65), cursor(35, 35, "up")], title="完成"),
        ], ["RTS"],
    ))
    c.append(case(
        "select-add-sub", "select", "加选 / 减选 / 反选",
        "按住修饰键再点或框，扩大或缩小当前选中。",
        [
            beat("已有选中，按住 Shift 再点另一个", "新单位加入选中，旧的仍在", "队伍变大", "topdown",
                 [unit(40, 50, sel=True), ring(40, 50), unit(65, 48, sel=True), ring(65, 48),
                  cursor(65, 48, "up"), badge("Shift+点")], title="加选"),
            beat("按住 Ctrl 再点已选中的单位", "该单位选中圈消失，其余仍在", "踢出一人", "topdown",
                 [unit(40, 50), unit(65, 48, sel=True), ring(65, 48), cursor(40, 50, "up"), badge("Ctrl+点")], title="减选"),
            beat("按住 Ctrl 再点：已选去掉、未选加入", "选中状态按点选翻转", "反着选", "topdown",
                 [unit(30, 50), unit(50, 52), unit(70, 48, sel=True), ring(70, 48),
                  cursor(50, 52, "up"), keyhint(42, 26, "Ctrl", "active", "刚点的那个被取消"),
                  badge("反选")], title="反选"),
        ], ["RTS", "SC2/War3"],
    ))
    c.append(case(
        "select-double-type", "select", "双击选同类型",
        "双击一个单位，同屏（或全局规则下）同类型一并选中。",
        [
            beat("双击士兵", "所有同造型士兵出现选中圈", "一键同型", "topdown",
                 [unit(30, 50, sel=True), unit(45, 55, sel=True), unit(60, 48, sel=True),
                  unit(70, 70, size=1.35, face=180),
                  ring(30, 50), ring(45, 55), ring(60, 48), cursor(45, 55, "up"), badge("双击·异类除外")], title="双击"),
        ], ["RTS", "SC2/War3"],
    ))
    c.append(case(
        "select-control-group", "select", "记编队 / 召编队",
        "Ctrl+数字记住当前选中；之后按数字召回。",
        [
            beat("选中一队后按 Ctrl+1", "界面出现编队 1 的肖像/计数", "记住了", "topdown",
                 [unit(40, 50, sel=True), unit(55, 52, sel=True), ring(40, 50), ring(55, 52),
                  keyhint(78, 28, "1", "active", "编队×2"), hotbar(active=0, slots=5),
                  badge("Ctrl+1")], title="记住"),
            beat("稍后按 1", "镜头可跳转；那队重新被选中", "召回编队", "topdown",
                 [unit(40, 50, sel=True), unit(55, 52, sel=True), ring(40, 50), ring(55, 52),
                  keyhint(78, 28, "1", "active", "召回"), badge("按 1")], title="召回"),
        ], ["SC2/War3", "RTS"],
    ))
    c.append(case(
        "select-avatar-only", "select", "永远只操控自己",
        "没有框选：角色就是你，无需选中步骤。",
        [
            beat("进入游戏", "第三人称/顶视角只有你可控", "你就是演员", "tps",
                 [hero(50, 60, face=-20), badge("化身")], title="化身"),
        ], ["ARPG", "FPS", "TPS", "MOBA"],
    ))
    c.append(case(
        "select-possess", "select", "切换操控对象",
        "附身、上车、切英雄、观战跟随时，操控跟到新主体。",
        [
            beat("靠近载具，按交互键", "载具高亮，出现上车提示", "能上车", "tps",
                 [hero(34, 62), vehicle(64, 52, "car"), ring(64, 52, r=20, kind="buff"),
                  keyhint(64, 30, "F", "active", "上车"), badge("可交互")], title="靠近"),
            beat("按下确认上车", "镜头切到载具视角/操控", "你变成车", "tps",
                 [vehicle(52, 55, "car", occupied=True), ring(52, 55, r=20, kind="select"),
                  keyhint(52, 30, "F", "active", "上车"), badge("切入")], title="切换"),
            beat("完成切换", "准星与 WASD 改成开车用", "载具手感", "tps",
                 [vehicle(44, 55, "car", occupied=True), crosshair(74, 38), wasd(["W"]),
                  badge("载具中")], title="操控中"),
        ], ["TPS", "RTS", "MOBA"],
    ))
    c.append(case(
        "select-clear", "select", "取消全部选中",
        "点空地或按专用键，选中圈全部消失。",
        [
            beat("已多选部队", "两单位带选中圈", "听令中", "topdown",
                 [unit(40, 50, sel=True), unit(55, 52, sel=True), ring(40, 50), ring(55, 52),
                  badge("已多选")], title="已选"),
            beat("点空地", "所有选中圈消失", "没人听令", "topdown",
                 [unit(40, 50), unit(55, 52), cursor(70, 70, "up"), badge("点空地")], title="清空"),
        ], ["RTS"],
    ))

    # ===== 二、常规指令 =====
    c.append(case(
        "order-move", "basic-order", "走到地上一点",
        "右键或移动键指定落点，单位出发。",
        [
            beat("对选中单位右键地面", "出现移动标记；单位朝标记走", "去那里", "topdown",
                 [unit(35, 55, sel=True, face=30), ring(35, 55), arrow(35, 55, 70, 40, "move"),
                  cursor(70, 40, "up"), badge("右键地面")], title="下令"),
        ], ["RTS", "ARPG", "MOBA"],
    ))
    c.append(case(
        "order-attack-move", "basic-order", "攻击移动",
        "沿路径前进，遇敌转入攻击。",
        [
            beat("按下攻击移动键再点地面", "路径带剑标，单位沿线前进", "边走边警惕", "topdown",
                 [unit(30, 60, sel=True, face=20), ring(30, 60), arrow(30, 60, 75, 35, "attack"),
                  cursor(75, 35, "up"), badge("A+点地")], title="下令"),
            beat("途中撞见敌人", "自动停下转火，打完继续沿线走", "见谁打谁", "topdown",
                 [unit(48, 48, sel=True, face=10), ring(48, 48), unit(60, 40, team="enemy"),
                  arrow(50, 47, 58, 42, "attack"), impact(60, 40, 12),
                  path([(48, 48), (75, 35)], "move"), badge("遇敌接战")], title="遇敌"),
        ], ["RTS", "SC2/War3", "MOBA"],
    ))
    c.append(case(
        "order-stop-hold", "basic-order", "停止 / 原地坚守",
        "立刻打断当前行动；或站桩反击不追击。",
        [
            beat("单位正沿路点移动", "地上有移动线，单位行进中", "在走", "topdown",
                 [unit(40, 50, sel=True, face=20), ring(40, 50), path([(40, 50), (70, 40)], "move"),
                  arrow(40, 50, 70, 40, "move"), badge("行进中")], title="行进"),
            beat("按停止", "单位停下，移动线消失", "站住", "topdown",
                 [unit(50, 50, sel=True), ring(50, 50), badge("Stop")], title="停止"),
            beat("按坚守", "单位站桩，有敌人靠近才打", "不追", "topdown",
                 [unit(50, 50, sel=True), ring(50, 50), circle_ind(50, 50, 26, True),
                  unit(78, 42, team="enemy"), badge("Hold")], title="坚守"),
        ], ["RTS"],
    ))
    c.append(case(
        "order-smart-right", "basic-order", "右键智能指令（总览）",
        "同一右键，点到不同东西结果不同。更细的「选中谁×点到谁」见第十七类（SC2/RA2 对照）。",
        [
            beat("右键敌人", "进入攻击该目标", "去干他", "topdown",
                 [unit(35, 55, sel=True, face=25), ring(35, 55), unit(70, 40, team="enemy"),
                  arrow(35, 55, 70, 40, "attack"), cursor(70, 40, "up"), badge("右键敌人")], title="打"),
            beat("右键矿点/资源", "工人去采集", "去挖", "topdown",
                 [unit(35, 55, sel=True, role="工"), ring(35, 55), prop(70, 45, "矿", kind="ore"),
                  arrow(35, 55, 70, 45, "move"), cursor(70, 45, "up"),
                  badge("右键资源")], title="采"),
        ], ["SC2/War3", "RTS"],
    ))
    c.append(case(
        "order-queue", "basic-order", "排队一串指令",
        "按住 Shift 连续下命令，做完一个接下一个。",
        [
            beat("Shift+右键点 A，再点 B", "地上出现 A→B 路点链", "排好队了", "topdown",
                 [unit(25, 60, sel=True), ring(25, 60), path([(25, 60), (50, 40), (75, 55)], "move"),
                  cursor(75, 55, "up"), badge("Shift 排队")], title="队列"),
            beat("到达 A 点", "不用再管，自动奔向 B；A 段路点消掉", "自己接着走", "topdown",
                 [unit(50, 40, sel=True, face=20), ring(50, 40), path([(50, 40), (75, 55)], "move"),
                  badge("自动接下一段")], title="自动接续"),
        ], ["SC2/War3", "RTS"],
    ))
    c.append(case(
        "order-patrol-rally", "basic-order", "巡逻与集结点",
        "两点间来回；或建筑造兵自动去集结点。",
        [
            beat("设巡逻两点", "单位在两点间往返", "来回晃", "topdown",
                 [unit(40, 50, sel=True, face=10), ring(40, 50), path([(30, 55), (70, 40), (30, 55)], "move"),
                  badge("巡逻")], title="巡逻"),
            beat("给兵营设集结点", "兵营外出现旗标", "新兵自动去那儿", "topdown",
                 [building(35, 50), arrow(35, 50, 70, 45, "move"), badge("集结点")], title="集结"),
        ], ["RTS", "C&C", "SC2/War3"],
    ))
    c.append(case(
        "order-load-unload", "basic-order", "装载 / 卸载",
        "选中单位进运输工具，再到别处倒出。",
        [
            beat("右键己方运输单位", "士兵走进载具，肖像进舱单", "上车", "topdown",
                 [unit(30, 58, sel=True), ring(30, 58), vehicle(64, 46, "car"),
                  arrow(34, 56, 58, 48, "move"), cursor(64, 46, "up"), badge("装载")], title="装"),
            beat("在别处下令卸载", "士兵在载具旁出现", "下车", "topdown",
                 [vehicle(38, 50, "car"), unit(58, 46), unit(62, 60), badge("卸载")], title="卸"),
        ], ["SC2/War3", "RTS"],
    ))

    # ===== 三、对准 =====
    c.append(case(
        "aim-look", "aim", "移动鼠标转视角",
        "鼠标往哪动，视野就往哪转，准星跟着落在世界某处。",
        [
            beat("移动鼠标", "画面旋转，准星指向新位置", "我在看那儿", "fps",
                 [crosshair(55, 45), cursor(30, 74, "drag"), badge("鼠标转视角")], title="转视角"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "aim-look-stick", "aim", "右摇杆转视角",
        "推右摇杆转视野，推得越满转得越快，准星跟着落在世界某处。",
        [
            beat("推右摇杆", "画面旋转，准星指向新位置", "我在看那儿", "fps",
                 [crosshair(55, 45), stick("R", 0.55, -0.25), badge("摇杆转视角")], title="转视角"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "aim-soft-lock", "aim", "软锁定附近敌人",
        "系统把攻击倾向吸向近处敌人，仍可挣脱。",
        [
            beat("靠近敌人进入锁定辅助", "目标描边；攻击朝向他", "咬住了", "tps",
                 [hero(40, 60, face=-31), unit(65, 45, team="enemy"), ring(65, 45, kind="lock"),
                  arrow(45, 56, 60, 48, "attack"), badge("软锁")], title="软锁"),
        ], ["动作RPG", "蝙蝠侠/蜘蛛侠"],
    ))
    c.append(case(
        "aim-hard-lock", "aim", "硬锁定切换目标",
        "锁定一人；按键切换上一个/下一个。",
        [
            beat("按下锁定", "镜头与准星钉死目标", "死死咬住", "tps",
                 [hero(40, 60), unit(70, 40, team="enemy"), crosshair(70, 40, locked=True), badge("硬锁")], title="锁定"),
            beat("按切换", "锁跳到下一个敌人", "换人咬", "tps",
                 [hero(40, 60), unit(70, 40, team="enemy"), unit(55, 35, team="enemy"),
                  crosshair(55, 35, locked=True), arrow(70, 40, 55, 35, "move"),
                  badge("切换")], title="切换"),
        ], ["动作RPG", "TPS"],
    ))
    c.append(case(
        "aim-skill-indicator", "aim", "进入技能瞄准（指示器）",
        "按技能后地上出现范围/方向预览，确认前可移动预览。",
        [
            beat("按下技能键", "进入瞄准；出现圈/扇形指示器", "先瞄再放", "moba",
                 [hero(40, 60), circle_ind(65, 40, 16, True), cone(40, 60, angle=-35, spread=46, length=32),
                  cursor(65, 40), hotbar(active=0), badge("技能瞄准")], title="出指示器"),
            beat("移动鼠标", "指示器跟随；非法区变红", "找落点", "moba",
                 [hero(40, 60), circle_ind(75, 55, 16, False), cone(40, 60, angle=10, spread=46, length=34),
                  cursor(75, 55), badge("调整")], title="调整"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "aim-cancel", "aim", "瞄准中取消",
        "右键或取消键退出瞄准，不放出技能。",
        [
            beat("按技能进入瞄准", "指示器亮起，跟着准星走", "正在瞄", "moba",
                 [hero(45, 55), circle_ind(62, 45, 14, True), cursor(62, 45, "aim"),
                  hotbar(active=0), badge("瞄准中")], title="瞄准中"),
            beat("按右键 / Esc 取消", "指示器消失，技能没消耗、也没进冷却", "当没按过", "moba",
                 [hero(45, 55), cursor(60, 50), hotbar(active=0),
                  menu_box(22, 28, ["技能仍可用", "无冷却"]), badge("已取消")], title="取消"),
        ], ["MOBA", "RTS超武"],
    ))

    # ===== 四、基本攻击 =====
    c.append(case(
        "atk-melee-tap", "attack", "近战点一下",
        "轻按攻击键挥砍一下。",
        [
            beat("点攻击键", "角色挥砍，命中有反馈", "砍一下", "tps",
                 [hero(45, 55, face=20), unit(65, 45, team="enemy"),
                  arrow(50, 52, 62, 47, "attack"), impact(65, 45, 13), badge("轻击")], title="挥砍"),
        ], ["动作RPG", "ARPG"],
    ))
    c.append(case(
        "atk-melee-hold-chain", "attack", "按住/连点打连段",
        "连续输入打出轻攻击链。",
        [
            beat("连点攻击", "招式一段接一段", "连起来了", "tps",
                 [hero(45, 55, face=15), unit(68, 48, team="enemy"),
                  arrow(48, 54, 56, 51, "attack"), arrow(50, 52, 62, 49, "attack"),
                  arrow(52, 51, 66, 48, "attack"), badge("连击×3")], title="连段"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "atk-gun-tap-spray", "attack", "枪械点射 / 按住扫射",
        "点一下一发；按住连续开火。",
        [
            beat("点射击键", "射出一发，准星微扬", "点射", "fps",
                 [crosshair(50, 48), arrow(50, 50, 62, 42, "attack"), badge("点射")], title="点射"),
            beat("按住射击键", "连续出弹，准星扩散", "压枪扫", "fps",
                 [crosshair(54, 44, spread="wide"),
                  arrow(50, 50, 72, 38, "attack"), arrow(50, 50, 78, 50, "attack"),
                  arrow(50, 50, 68, 58, "attack"), impact(74, 44, 13), badge("扫射")], title="扫射"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "atk-ads-reload-swap", "attack", "开镜 / 换弹 / 切枪",
        "右键开镜稳定准星；R 换弹读条；数字键或滚轮切枪换手感。",
        [
            beat("按开镜", "视野拉近，准星变精准", "瞄稳", "fps",
                 [box(32, 32, 36, 36), crosshair(50, 50, spread="tight"), badge("ADS开镜")], title="开镜"),
            beat("按换弹", "弹药数刷新，短暂不能射", "换弹中", "fps",
                 [crosshair(50, 50), bar(50, 68, 0.4, "cast", "换弹"),
                  badge("0/30 · 禁射")], title="换弹"),
            beat("按切枪", "武器模型与弹种切换", "换一把", "fps",
                 [crosshair(50, 50), hotbar(active=1), keyhint(78, 78, "2", "active", "切枪"),
                  badge("步枪→霰弹")], title="切枪"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "atk-grenade", "attack", "扔手雷到落点",
        "拿出手雷，看抛物线/落点圈，确认扔出。",
        [
            beat("按住手雷键瞄准", "地面落点圈 + 抛物预览", "看落点", "tps",
                 [hero(35, 60), circle_ind(70, 40, 12, True), path([(38, 55), (55, 30), (70, 40)], "arc"), badge("手雷")], title="预览"),
            beat("松开/再按确认", "手雷飞出，落点爆炸", "炸那儿", "tps",
                 [hero(35, 60), path([(38, 55), (55, 30), (70, 40)], "arc"),
                  impact(70, 40, 22, heavy=True), badge("投出")], title="投出"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "atk-right-click-enemy", "attack", "右键敌人下令攻击",
        "RTS：当前选中单位去打该敌人。",
        [
            beat("右键敌方单位", "选中单位冲过去打", "集火", "topdown",
                 [unit(30, 55, sel=True, face=30), unit(38, 60, sel=True, face=30), ring(30, 55), ring(38, 60),
                  unit(70, 40, team="enemy"), arrow(34, 56, 68, 42, "attack"), cursor(70, 40, "up"), badge("右键敌")], title="集火"),
        ], ["RTS", "MOBA"],
    ))

    # ===== 五、双摇杆 =====
    c.append(case(
        "twin-move-aim-split", "twin-stick", "左走右瞄（移瞄分离）",
        "左摇杆走路，右摇杆独自决定射击朝向，可边退边打。",
        [
            beat("左摇杆向前，右摇杆向右", "角色往上走，枪口朝右，子弹向右", "螃蟹步开火", "topdown",
                 [hero(45, 55, face=0), stick("L", 0, -0.8), stick("R", 0.9, 0),
                  arrow(45, 50, 45, 26, "move"), arrow(50, 55, 76, 55, "attack"),
                  unit(80, 52, team="enemy"), badge("走上·打右")], title="分离"),
        ], ["双摇杆射击"],
    ))
    c.append(case(
        "twin-fire-on-aim", "twin-stick", "右摇杆推出即开火",
        "右摇杆推过死区就开始射；回中停火或保持朝向。",
        [
            beat("右摇杆回中", "朝向保持，不开火", "待命", "topdown",
                 [hero(50, 55, face=45), stick("L", 0, 0), stick("R", 0, 0), badge("回中")], title="停火"),
            beat("右摇杆推出", "沿摇杆方向连续射击", "开火", "topdown",
                 [hero(50, 55, face=45), stick("L", -0.3, 0.2), stick("R", 0.7, -0.5),
                  arrow(55, 50, 78, 32, "attack"), badge("推射")], title="推射"),
        ], ["双摇杆射击"],
    ))
    c.append(case(
        "twin-auto-face", "twin-stick", "无瞄准时的朝向策略",
        "没推右摇杆时：跟移动方向 / 保持上次朝向 / 吸向最近敌人。",
        [
            beat("只推左摇杆（跟移动朝向）", "面朝行走方向", "看向去处", "topdown",
                 [hero(50, 55, face=-90), stick("L", 0, -1), stick("R", 0, 0), badge("面朝移动")], title="跟走"),
            beat("只推左摇杆（吸最近敌）", "边走边转向附近敌人", "自动咬敌", "topdown",
                 [hero(45, 55, face=-31), unit(70, 40, team="enemy"), stick("L", 0.5, -0.5), stick("R", 0, 0),
                  badge("自动朝敌")], title="吸敌"),
        ], ["双摇杆射击"],
    ))
    c.append(case(
        "twin-dodge-shoot", "twin-stick", "冲刺/翻滚中能否转向射击",
        "闪避期间右摇杆是否仍能改朝向与开火。",
        [
            beat("闪避中继续推右摇杆", "残影位移同时子弹改向", "闪着打", "topdown",
                 [hero(40, 55, face=60), path([(30, 55), (50, 50)], "move"), stick("R", 0.8, 0.2),
                  arrow(45, 52, 75, 48, "attack"), badge("闪射")], title="闪射"),
        ], ["双摇杆射击", "TPS"],
    ))
    c.append(case(
        "twin-stick-magnets", "twin-stick", "右摇杆磁吸辅助",
        "摇杆推向敌人方向有吸附；回中可清锁。",
        [
            beat("右摇杆推向敌人大致方向", "朝向粗对准，还差一点", "差不多了", "topdown",
                 [hero(40, 55, face=25), unit(70, 40, team="enemy"),
                  stick("R", 0.6, -0.4), badge("粗瞄")], title="粗瞄"),
            beat("进入磁吸角度", "准星/朝向被吸到敌人身上，出现锁定圈", "帮你咬准", "topdown",
                 [hero(40, 55, face=-27), unit(70, 40, team="enemy"), ring(70, 40, kind="lock"),
                  stick("R", 0.6, -0.4), badge("磁吸咬住")], title="吸附"),
            beat("摇杆回中", "锁定圈消失，回到自由朝向", "松口", "topdown",
                 [hero(40, 55, face=35), unit(70, 40, team="enemy"),
                  stick("R", 0, 0), badge("清锁")], title="清锁"),
        ], ["双摇杆射击"],
    ))
    c.append(case(
        "twin-kb-mouse-equiv", "twin-stick", "键鼠等价：WASD + 鼠标瞄",
        "同一套移瞄分离，只是输入皮肤不同。",
        [
            beat("WASD 走，鼠标定朝向", "顶视角角色移瞄分离，鼠标方向开火", "键鼠双摇杆", "topdown",
                 [hero(45, 55, face=-27), wasd(["W", "D"]), cursor(75, 40),
                  arrow(45, 50, 45, 26, "move"), arrow(50, 52, 72, 42, "attack"),
                  unit(78, 38, team="enemy"), badge("走上·打右上")], title="键鼠"),
        ], ["双摇杆射击", "ARPG"],
    ))

    # ===== 六、不用瞄的技能 =====
    c.append(case(
        "skill-self-buff", "instant-skill", "按键自身状态",
        "按下立刻给自己加状态/特效。",
        [
            beat("按技能键", "角色周围闪光/图标亮起", "给自己开了", "moba",
                 [hero(50, 55), ring(50, 55, r=14, kind="buff"), badge("自身技")], title="开"),
        ], ["MOBA", "ARPG", "RTS"],
    ))
    c.append(case(
        "skill-self-aoe", "instant-skill", "按键自己脚下炸一圈",
        "无瞄准，以自己为圆心立刻结算。",
        [
            beat("按技能键", "脚下出现爆炸圈，周围敌人受击", "震开", "topdown",
                 [hero(50, 55), impact(50, 55, 24, True), unit(65, 50, team="enemy"), badge("自身AOE")], title="炸圈"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "skill-blink-facing", "instant-skill", "按键朝面向闪现",
        "瞬移一小段，方向跟面向或移动键。",
        [
            beat("按闪现", "残影到新位置", "闪过去", "topdown",
                 [hero(40, 55, face=0), arrow(40, 55, 65, 55, "move"), hero(65, 55, face=0),
                  badge("闪现")], title="闪"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "skill-toggle-form", "instant-skill", "开关姿态 / 切形态",
        "再按关掉；或锤炮切换导致技能栏变化。",
        [
            beat("按切换键", "外形换了，技能栏整组换成新形态的招", "换一套招", "moba",
                 [hero(50, 55, face=20, form="alt"), hotbar(page="形态B"),
                  badge("切到形态B")], title="切换"),
            beat("再按一次", "外形变回来，技能栏也还原", "换回来", "moba",
                 [hero(50, 55, face=20), hotbar(active=0), badge("切回形态A")], title="切回"),
        ], ["MOBA", "RTS英雄"],
    ))

    # ===== 七、选单位技能 =====
    c.append(case(
        "skill-pick-enemy", "unit-skill", "技能后点敌人",
        "先按技能，再点合法敌方目标放出。",
        [
            beat("按技能", "鼠标变专属准星，场上敌人可点", "选目标", "moba",
                 [hero(35, 60), unit(68, 36, team="enemy"), unit(84, 60, team="enemy"),
                  cursor(54, 46, "aim"), hotbar(active=0), badge("选敌")], title="进入"),
            beat("点敌人", "被点的那个敌人套上锁定圈，技能飞向它", "点到了", "moba",
                 [hero(35, 60), unit(68, 36, team="enemy"), ring(68, 36, r=12, kind="lock"),
                  unit(84, 60, team="enemy"), arrow(40, 55, 64, 39, "attack"),
                  cursor(74, 44, "up"), hotbar(active=0), badge("确认")], title="点中"),
        ], ["MOBA", "RTS", "War3"],
    ))
    c.append(case(
        "skill-smart-cast-unit", "unit-skill", "智能施法打准星下单位",
        "按键当下自动对准星下单位施放，不进瞄准态。",
        [
            beat("鼠标已停在敌人上时按技能", "立刻出手，没有瞄准指示器这一步", "又快又险", "moba",
                 [hero(35, 60), unit(65, 42, team="enemy", highlight=True), cursor(65, 42),
                  arrow(40, 55, 62, 44, "attack"), hotbar(active=0), badge("智能施法")], title="瞬放"),
        ], ["MOBA"],
    ))
    c.append(case(
        "skill-ally-only", "unit-skill", "只能点特定对象",
        "治疗只能点友军；不合规目标禁止态。",
        [
            beat("技能瞄准移到敌人上", "准星禁止，点不出去", "对象不对", "moba",
                 [hero(35, 60), unit(65, 40, team="enemy"), circle_ind(65, 40, 12, False),
                  crosshair(65, 40, locked=True), hotbar(deny=0), badge("禁止")], title="非法"),
            beat("移到友军上再确认", "治疗特效打出", "对了", "moba",
                 [hero(35, 60), unit(65, 45, team="ally", sel=True), arrow(40, 55, 62, 46, "move"), badge("友军")], title="合法"),
        ], ["MOBA", "RTS"],
    ))
    c.append(case(
        "skill-two-targets", "unit-skill", "连续点两个目标",
        "一段技能要先点 A 再点 B。",
        [
            beat("第一次点单位 A", "A 被标记，提示再选 B", "还要一个", "moba",
                 [hero(30, 60), unit(50, 40, team="enemy"), ring(50, 40, kind="lock"), cursor(50, 40, "up"),
                  badge("目标1")], title="第一目标"),
            beat("再点单位 B", "技能连接 A 与 B 放出", "成了", "moba",
                 [hero(30, 60), unit(50, 40, team="enemy"), unit(75, 55, team="enemy"),
                  arrow(50, 40, 75, 55, "attack"), badge("目标2")], title="第二目标"),
        ], ["MOBA", "War3"],
    ))

    # ===== 八、点地技能 =====
    c.append(case(
        "skill-ground-click", "ground-skill", "技能后点地板",
        "落雷/圈地：点一下地面放出。",
        [
            beat("按下技能键", "进入瞄准，落点圈跟着光标", "先瞄", "moba",
                 [hero(30, 60), circle_ind(55, 45, 16, True), cursor(55, 45),
                  hotbar(active=0), badge("瞄准")], title="进瞄准"),
            beat("再点地面确认", "落点圈亮起并结算", "砸这儿", "moba",
                 [hero(30, 60), unit(65, 40, team="enemy"), circle_ind(65, 40, 18, True),
                  impact(65, 40, 14), cursor(65, 40, "up"), hotbar(cd=0),
                  badge("点地")], title="确认"),
        ], ["MOBA", "ARPG", "RTS"],
    ))
    c.append(case(
        "skill-ground-drag-release", "ground-skill", "按住拖指示器松手落下",
        "按住看预览，松手确认落点。",
        [
            beat("按住技能", "指示器跟随鼠标", "瞄着", "moba",
                 [hero(30, 60), circle_ind(60, 45, 16, True), cursor(60, 45, "drag"), badge("按住")], title="按住"),
            beat("松开", "在松手处落下", "放！", "moba",
                 [hero(30, 60), circle_ind(72, 38, 16, True), cursor(72, 38, "up"), badge("松开")], title="松开"),
        ], ["MOBA"],
    ))
    c.append(case(
        "skill-build-place", "ground-skill", "建造落格",
        "拖着建筑影在网格上找合法格，确认放下。",
        [
            beat("选建筑后移动影", "合法格绿色，非法红色", "找地儿", "topdown",
                 [building(48, 42, ghost=True), circle_ind(48, 42, 16, True),
                  building(72, 58, ghost=True), circle_ind(72, 58, 16, False),
                  cursor(48, 42), badge("建造预览")], title="预览"),
            beat("确认放置", "建筑开工/落地", "定了", "topdown",
                 [building(48, 42, ghost=False), badge("建造")], title="放下"),
        ], ["C&C", "RTS"],
    ))
    c.append(case(
        "skill-minimap-ground", "ground-skill", "小地图点落点",
        "大招/支援可在小地图点一下当世界落点。",
        [
            beat("技能瞄准时点小地图", "大地图对应位置落下技能", "远程点射地图", "moba",
                 [hero(25, 70), circle_ind(70, 30, 14, True),
                  box(74, 72, 22, 22), circle_ind(85, 83, 5, True), cursor(85, 83, "up"),
                  badge("小地图施法")], title="小地图"),
        ], ["MOBA", "RTS"],
    ))

    # ===== 九、方向技能 =====
    c.append(case(
        "skill-direction-shot", "direction-skill", "选方向射出",
        "转方向确认后放出直线/技能镜头。",
        [
            beat("进入方向瞄准", "箭头/条状指示器随鼠标转", "瞄方向", "moba",
                 [hero(40, 55), cone(40, 55, angle=-30, spread=18, length=35), cursor(70, 35), badge("方向")], title="瞄"),
            beat("确认", "沿该方向飞出", "射！", "moba",
                 [hero(40, 55), arrow(45, 52, 80, 30, "attack"), badge("放出")], title="放"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "skill-cone-sweep", "direction-skill", "扇形/矩形扫射预览",
        "预览随鼠标转，确认后扇形结算。",
        [
            beat("拖动改变扇形朝向", "地面扇形跟着转", "扫哪片", "topdown",
                 [hero(40, 55), cone(40, 55, angle=-28, spread=55, length=32), cursor(66, 30),
                  badge("扇形·跟着鼠标转")], title="预览"),
            beat("确认放出", "扇形内结算命中", "扫到了", "topdown",
                 [hero(40, 55), cone(40, 55, angle=20, spread=55, length=32), cursor(70, 44, "up"),
                  unit(62, 42, team="enemy"), unit(68, 55, team="enemy"),
                  impact(64, 48, 18), badge("结算")], title="确认"),
        ], ["MOBA", "ARPG", "TPS"],
    ))
    c.append(case(
        "skill-dash-dir", "direction-skill", "指定方向冲刺",
        "朝选定方向突进一段距离。",
        [
            beat("选定冲刺方向", "方向箭头/锥预览", "瞄方向", "topdown",
                 [hero(35, 55), cone(35, 55, angle=-25, spread=16, length=30),
                  cursor(70, 40), badge("方向")], title="瞄准"),
            beat("确认冲刺", "角色沿箭头冲出", "冲！", "topdown",
                 [hero(65, 42), arrow(35, 55, 70, 40, "move"), path([(35, 55), (70, 40)], "move"),
                  badge("冲刺")], title="冲出"),
        ], ["MOBA", "ARPG", "动作RPG"],
    ))
    c.append(case(
        "skill-grapple", "direction-skill", "钩索荡出去",
        "朝方向甩钩，钩到锚点才位移。",
        [
            beat("甩出钩索", "钩索沿弧线飞向可挂的锚点", "钩得上吗", "tps",
                 [hero(35, 65), path([(38, 60), (70, 35)], "arc"), anchor(72, 32), badge("钩索")], title="甩钩"),
            beat("钩中后荡移", "角色被拉向锚点", "荡过去", "tps",
                 [hero(62, 42), anchor(72, 32), path([(40, 62), (62, 44)], "arc"), badge("荡")], title="荡"),
        ], ["蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "skill-vector", "direction-skill", "拉一条矢量再放",
        "拖出起点到终点的矢量技能。",
        [
            beat("按下定起点", "起点钉在地上", "从这儿", "moba",
                 [hero(30, 60), circle_ind(40, 50, 8, True), cursor(40, 50, "down"),
                  badge("起点")], title="按下"),
            beat("拖到终点", "矢量箭头跟着拉长", "拉到那", "moba",
                 [hero(30, 60), arrow(40, 50, 70, 38, "move"), cursor(70, 38, "drag"),
                  badge("拖拽")], title="拖拽"),
            beat("松开确认", "沿矢量放出技能", "放！", "moba",
                 [hero(30, 60), arrow(40, 50, 75, 35, "attack"), cursor(75, 35, "up"),
                  badge("确认")], title="松开"),
        ], ["MOBA"],
    ))

    # ===== 十、按住 =====
    c.append(case(
        "hold-charge-shot", "hold", "蓄力松手开火",
        "按住蓄力，松手射出；蓄满可自动放。",
        [
            beat("按住", "蓄力条上涨，准星变化", "攒劲", "fps",
                 [crosshair(50, 50), badge("蓄力中")], title="蓄"),
            beat("松开", "射出强化弹", "放！", "fps",
                 [crosshair(50, 48), badge("松手")], title="放"),
        ], ["FPS", "TPS", "ARPG"],
    ))
    c.append(case(
        "hold-full-auto", "hold", "按住持续扫射 / 喷火",
        "按住期间持续结算。",
        [
            beat("按住射击", "连续弹道/火焰", "压着打", "tps",
                 [hero(40, 60, face=30), arrow(48, 55, 78, 40, "attack"), crosshair(80, 38),
                  keyhint(24, 82, "左键", "active", "按住不放"), badge("持续")], title="持续"),
        ], ["FPS", "双摇杆", "TPS"],
    ))
    c.append(case(
        "hold-block-channel", "hold", "举盾 / 站桩引导",
        "按住格挡；或读条引导（可慢走/不能动）。",
        [
            beat("按住格挡", "举盾姿态，减伤/弹反窗准备", "防住", "tps",
                 [hero(50, 55), box(56, 46, 10, 16), ring(50, 55, r=14, kind="buff"),
                  bar(50, 34, 0.65, "charge", "弹反窗"), badge("举盾")], title="盾"),
            beat("引导读条", "读条 UI，可规定能否移动", "读着", "moba",
                 [hero(50, 55), circle_ind(50, 55, 20, True), badge("引导")], title="引导"),
        ], ["动作RPG", "MOBA"],
    ))

    # ===== 十一、连招 =====
    c.append(case(
        "combo-light-chain", "combo", "轻攻击连段",
        "连点走出 1-2-3 段。",
        [
            beat("第一下", "第一段动画", "1", "tps",
                 [hero(45, 55, face=-25), unit(68, 45, team="enemy"),
                  cone(45, 55, angle=-25, spread=32, length=20), badge("一段")], title="1"),
            beat("衔接窗内再按", "第二段", "2", "tps",
                 [hero(48, 52, face=-20), unit(68, 45, team="enemy"),
                  arrow(52, 50, 64, 46, "attack"), cone(48, 52, angle=-15, spread=40, length=24),
                  badge("二段")], title="2"),
            beat("再按", "第三段收招", "3", "tps",
                 [hero(52, 50, face=-10), unit(68, 45, team="enemy"),
                  cone(52, 50, angle=-10, spread=55, length=30), path([(45, 55), (52, 50)], "move"),
                  badge("三段")], title="3"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "combo-heavy-branch", "combo", "轻重混按分支",
        "在链中插入重击走出另一条分支。",
        [
            beat("轻→重", "派生重击动画", "分支了", "tps",
                 [hero(48, 52, face=25), unit(70, 45, team="enemy"),
                  cone(48, 52, angle=-15, spread=55, length=30),
                  arrow(52, 50, 66, 46, "attack"), badge("轻→重")], title="分支"),
        ], ["动作RPG"],
    ))
    c.append(case(
        "combo-recast-stages", "combo", "两段/多段再按",
        "第一次放前半段，再按放后半段。",
        [
            beat("第一次按大招", "前半段演出/位移", "上半段", "moba",
                 [hero(40, 55, face=-20), path([(40, 55), (55, 45)], "move"),
                  arrow(40, 55, 55, 45, "move"), hotbar(active=3), badge("一段大招")], title="一段"),
            beat("提示窗内再按", "后半段爆发", "下半段", "moba",
                 [hero(55, 45), circle_ind(55, 45, 18, True), badge("二段")], title="二段"),
        ], ["MOBA"],
    ))
    c.append(case(
        "combo-dodge-attack", "combo", "闪避后接攻击",
        "闪避结束的专属窗内按攻击出派生。",
        [
            beat("按闪避", "角色翻滚位移，身后留残影", "先躲开", "tps",
                 [hero(38, 58, face=20), path([(52, 52), (38, 58)], "move"),
                  unit(66, 42, team="enemy"), badge("闪避中")], title="闪避"),
            beat("落地窗口内按攻击", "打出闪攻专属派生动画", "漂亮", "tps",
                 [hero(42, 55, face=30), cone(42, 55, angle=-15, spread=45, length=26),
                  unit(66, 42, team="enemy"), badge("闪攻派生")], title="闪攻"),
        ], ["动作RPG"],
    ))

    # ===== 十二、防御 =====
    c.append(case(
        "def-dodge", "defense", "翻滚 / 闪避",
        "按闪避键出无敌帧位移。",
        [
            beat("按闪避", "残影滚开", "躲过", "tps",
                 [hero(58, 48), unit(24, 62, team="enemy", face=-30),
                  arrow(28, 60, 42, 55, "attack"), path([(40, 55), (58, 48)], "move"),
                  ring(58, 48, r=12, kind="buff"), badge("闪避·无敌帧")], title="闪"),
        ], ["动作RPG", "TPS"],
    ))
    c.append(case(
        "def-perfect-dodge", "defense", "完美闪避",
        "在敌招判定前极短窗闪避，触发额外反馈。",
        [
            beat("红光提示时闪避", "慢镜，并亮起可反击的窗口", "完美！", "tps",
                 [hero(55, 48), path([(40, 55), (55, 48)], "move"),
                  unit(70, 45, team="enemy"), ring(70, 45, kind="finisher", r=11),
                  arrow(58, 50, 66, 46, "attack"), ring(55, 48, r=10, kind="buff"),
                  keyhint(70, 24, "F", "active", "反击"), badge("完美闪避")], title="完美"),
        ], ["蝙蝠侠", "动作RPG"],
    ))
    c.append(case(
        "def-parry-window", "defense", "弹反窗与反击",
        "敌招到来时按格挡；成功后可处刑/反击。",
        [
            beat("提示出现时按格挡", "弹反火花", "弹开！", "tps",
                 [hero(45, 55, face=15), unit(65, 48, team="enemy"),
                  impact(56, 51, 11), arrow(62, 49, 57, 51, "attack"),
                  keyhint(45, 34, "格挡", "active", "弹反成功"), badge("弹反")], title="弹反"),
            beat("出现处刑提示再按", "处刑演出", "收掉", "tps",
                 [hero(48, 52, face=10), unit(64, 48, team="enemy", highlight=True),
                  arrow(52, 51, 61, 49, "attack"), impact(64, 48, 15, heavy=True),
                  keyhint(64, 28, "F", "active", "处刑"), badge("处刑")], title="处刑"),
        ], ["蝙蝠侠", "动作RPG"],
    ))

    # ===== 十三、环境 =====
    c.append(case(
        "env-throw", "environment", "捡起东西扔掉",
        "交互拾取，再瞄准扔出。",
        [
            beat("对可抓物按交互", "举在手上", "抓到了", "tps",
                 [hero(40, 55), prop(48, 40, "箱子", kind="item", highlight=True),
                  keyhint(56, 30, "F", "active", "举起"), badge("举起")], title="抓"),
            beat("瞄准落点", "抛物预览与落点圈出现", "找砸点", "tps",
                 [hero(40, 55), prop(46, 44, "箱子", kind="item"),
                  path([(46, 44), (58, 32), (75, 42)], "arc"),
                  circle_ind(75, 42, 12, True), cursor(75, 42), badge("预览")], title="瞄准"),
            beat("松手扔出", "物体飞出砸中目标", "砸！", "tps",
                 [hero(40, 55), unit(78, 42, team="enemy"), path([(48, 50), (62, 34), (75, 42)], "arc"),
                  impact(76, 42, 15, heavy=True), arrow(60, 38, 74, 42, "attack"),
                  badge("命中")], title="扔出"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "env-wall-slam", "environment", "砸墙 / 推悬崖",
        "把敌人往环境特征上打。",
        [
            beat("朝墙方向攻击敌人", "敌人撞墙演出", "糊墙上", "tps",
                 [hero(36, 58), unit(60, 48, team="enemy"), prop(78, 46, "墙", kind="wall"),
                  arrow(40, 56, 55, 50, "attack"), path([(58, 50), (68, 48), (72, 47)], "move"),
                  impact(72, 47, 15, heavy=True), badge("砸墙")], title="砸墙"),
        ], ["蝙蝠侠", "动作RPG"],
    ))
    c.append(case(
        "env-destructible", "environment", "打可破坏物",
        "攻击木箱/墙开门路或掉资源。",
        [
            beat("攻击可破坏物", "物体碎裂", "砸开", "topdown",
                 [hero(40, 55), building(64, 50, ghost=True), arrow(45, 52, 58, 50, "attack"),
                  impact(64, 50, 17, heavy=True), badge("破坏")], title="破坏"),
        ], ["ARPG", "FPS", "动作RPG"],
    ))

    # ===== 十四、部队宝宝载具 =====
    c.append(case(
        "army-pet-attack", "army", "命令随从打当前目标",
        "给宝宝指定攻击对象。",
        [
            beat("选目标下令随从攻击", "随从冲向目标", "去咬", "topdown",
                 [hero(30, 60), unit(40, 55, sel=True), ring(40, 55), unit(70, 40, team="enemy"),
                  arrow(42, 53, 68, 42, "attack"), badge("随从攻击")], title="随从"),
        ], ["ARPG", "MOBA", "RTS"],
    ))
    c.append(case(
        "army-merge", "army", "两单位合体",
        "选中符合条件的单位下令合体。",
        [
            beat("对两名高阶单位下令合体", "两单位相向，准备融合", "合体！", "topdown",
                 [unit(40, 50, sel=True), unit(55, 50, sel=True), ring(40, 50), ring(55, 50),
                  arrow(42, 50, 52, 50, "move"), arrow(53, 50, 43, 50, "move"),
                  badge("下令合体")], title="下令"),
            beat("融合完成", "只剩体型更大的合体单位", "成了", "topdown",
                 [unit(48, 50, size=1.4, sel=True), ring(48, 50), badge("合体完成")], title="合体"),
        ], ["SC2"],
    ))
    c.append(case(
        "army-vehicle", "army", "上车变载具手感",
        "交互上车后，摇杆/射击变成载具武器。",
        [
            beat("走近载具按交互", "出现上车提示", "能上车", "tps",
                 [hero(32, 60), vehicle(58, 50, "tank"), keyhint(58, 30, "A键", "active", "上车"),
                  badge("靠近载具")], title="靠近"),
            beat("上车完成", "准星变车炮，移动手感变车辆，技能栏换载具武器", "开炮车", "tps",
                 [vehicle(46, 55, "tank", occupied=True), crosshair(74, 38),
                  stick("L", 0, -0.7), stick("R", 0.5, -0.2),
                  hotbar(extra=0), badge("载具操作")], title="载具"),
        ], ["TPS", "FPS"],
    ))

    # ===== 十五、手感变体 =====
    c.append(case(
        "habit-smart-vs-normal", "cast-habit", "智能施法 vs 先瞄后放",
        "同一技能：按下即打，或先出指示器再确认。",
        [
            beat("智能施法开启时按键", "立刻对向准星处出手", "快", "moba",
                 [hero(35, 60), unit(70, 40, team="enemy"), crosshair(70, 40),
                  arrow(40, 55, 68, 42, "attack"), hotbar(active=0), badge("智能")], title="智能"),
            beat("普通模式按键", "先出指示器等确认", "稳", "moba",
                 [hero(35, 60), circle_ind(60, 45, 14, True), cursor(60, 45),
                  hotbar(active=0), badge("普通")], title="普通"),
            beat("再点击确认出手", "指示器落地，技能放出", "确认打出", "moba",
                 [hero(35, 60), circle_ind(60, 45, 14, True), cursor(60, 45, "up"),
                  arrow(40, 55, 58, 46, "attack"), badge("确认出手")], title="确认"),
        ], ["MOBA"],
    ))
    c.append(case(
        "habit-alt-self", "cast-habit", "Alt 对自己放",
        "按住 Alt 再按技能，强制以自己为目标。",
        [
            beat("直接按技能", "进入选目标 / 打向准星方向", "要先选人", "moba",
                 [hero(50, 55), cursor(68, 42), circle_ind(68, 42, 12, True),
                  hotbar(active=0), badge("普通施法")], title="普通"),
            beat("按住 Alt 再按技能", "跳过选目标，直接打在自己身上", "自我施法", "moba",
                 [hero(50, 55), ring(50, 55, r=12, kind="buff"), hotbar(active=0),
                  badge("Alt自施")], title="自施"),
        ], ["MOBA"],
    ))
    c.append(case(
        "habit-shift-queue-cast", "cast-habit", "Shift 排队施法",
        "当前动作结束后再放这个技能。",
        [
            beat("正在走 A 段时 Shift+技能点落点", "路点后追加技能标记，当前动作不打断", "排后面", "moba",
                 [hero(30, 60), path([(30, 60), (50, 50)], "move"), circle_ind(70, 40, 12, True),
                  queue_no(50, 42, 1, "active"), queue_no(70, 26, 2),
                  keyhint(24, 80, "Shift", "active", "排到后面"),
                  badge("Shift排队")], title="排队"),
            beat("走完 A 段", "自动开始对排队落点施法", "到点自动放", "moba",
                 [hero(50, 50, face=20), circle_ind(70, 40, 12, True),
                  arrow(52, 49, 68, 42, "attack"),
                  queue_no(50, 38, 1, "done"), queue_no(70, 24, 2, "active"),
                  badge("自动施放")], title="自动放"),
        ], ["SC2", "MOBA"],
    ))
    c.append(case(
        "habit-double-tap", "cast-habit", "双击技能",
        "双击触发与单击不同的变体（如闪回自己方向）。",
        [
            beat("双击技能键", "走出双击变体", "双击版", "moba",
                 [hero(50, 55), path([(50, 55), (35, 45)], "move"), arrow(50, 55, 35, 45, "move"),
                  hotbar(active=0), keyhint(50, 30, "Q", "active", "双击"),
                  badge("双击")], title="双击"),
        ], ["MOBA"],
    ))

    # ===== 十六、一群人放同一个技能 =====
    c.append(case(
        "multi-cast-together", "multi-cast", "齐放：能放的人同时出手",
        "多选后点一次技能，所有满足条件的单位一起放。像 SC2 多选高圣堂武士对同一点砸心灵风暴；"
        "RA2 多选光棱坦克强制攻击同一落点齐射。",
        [
            beat("多选施法单位，点技能落点一次", "多个风暴/齐射同时砸在同一点", "一起轰", "topdown",
                 [unit(22, 62, sel=True), unit(32, 58, sel=True), unit(28, 70, sel=True),
                  ring(22, 62), ring(32, 58), ring(28, 70),
                  circle_ind(72, 38, 18, True), cursor(72, 38, "up"),
                  arrow(28, 58, 68, 40, "attack"), arrow(32, 56, 70, 42, "attack"),
                  badge("齐放·SC2风暴")], title="同点齐放"),
            beat("RA2：多选光棱，强制攻击地面一点", "数道光柱同时打向落点", "齐射", "topdown",
                 [unit(20, 55, sel=True, size=1.1), unit(32, 62, sel=True, size=1.1),
                  unit(26, 70, sel=True, size=1.1), circle_ind(75, 40, 10, True),
                  arrow(24, 56, 72, 42, "attack"), arrow(34, 60, 74, 40, "attack"),
                  badge("齐放·RA2光棱")], title="光棱齐射"),
        ], ["SC2", "RA2", "RTS"],
    ))
    c.append(case(
        "multi-cast-sequence", "multi-cast", "按顺序放：一个接一个",
        "多选后点技能，单位按队列依次出手，不叠在同一帧。"
        "像 SC2 多选幽灵对同一目标排队狙击/核武引导；RA2 多选工程师依次进占同一建筑。",
        [
            beat("多选后点技能目标一次", "第一个出手，其余排队等待图标", "等他放完", "topdown",
                 [unit(20, 55, sel=True), unit(32, 58, sel=True), unit(44, 62, sel=True),
                  ring(20, 55), unit(75, 40, team="enemy"),
                  arrow(22, 54, 70, 42, "attack"), badge("1号在放"),
                  queue_no(20, 44, 1, "active"), queue_no(32, 47, 2), queue_no(44, 51, 3)],
                 title="第一个出手"),
            beat("第一人完成", "第二人自动接着放同一技能/目标", "接下一个", "topdown",
                 [unit(20, 55, sel=True), unit(32, 58, sel=True), unit(44, 62, sel=True),
                  ring(32, 58), unit(75, 40, team="enemy"),
                  arrow(34, 56, 72, 42, "attack"), badge("2号接上"),
                  queue_no(20, 44, 1, "done"), queue_no(32, 47, 2, "active"),
                  queue_no(44, 51, 3)], title="顺序接力"),
            beat("RA2：多选工程师点敌建筑", "一人进占，其余在旁排队", "一个个进", "topdown",
                 [unit(30, 60, sel=True, role="工"), unit(38, 66, sel=True, role="工"),
                  unit(46, 58, sel=True, role="工"), building(72, 42, team="enemy"),
                  arrow(32, 58, 66, 44, "move"), badge("工程师排队"),
                  queue_no(30, 49, 1, "active"), queue_no(38, 55, 2), queue_no(46, 47, 3)],
                 title="占建筑排队"),
        ], ["SC2", "RA2", "RTS"],
    ))
    c.append(case(
        "multi-cast-priority", "multi-cast", "按优先级放：只让最合适的出手",
        "混编选中时按技能，系统只挑能放、且最合适的单位出手，其余不动。"
        "像 SC2 陆战队员+高圣堂混选按风暴，只有高圣堂放；RA2 混选坦克与防空单位点飞机，只有能打空的开火。",
        [
            beat("混选军队，按只有部分人会的技能", "不会的单位无反应；会的那几个出手", "对的人在放", "topdown",
                 [unit(22, 60, sel=True), unit(34, 55, sel=True, size=1.15), unit(46, 62, sel=True),
                  ring(34, 55), circle_ind(72, 38, 16, True), cursor(72, 38),
                  arrow(36, 54, 68, 40, "attack"), badge("仅施法者")], title="过滤不会的"),
            beat("多人都会时按优先级（能量高/离得近）", "只有优先级最高的一人或数人出手", "挑最好的", "topdown",
                 [unit(24, 58, sel=True), unit(36, 52, sel=True), unit(48, 60, sel=True),
                  ring(36, 52), unit(74, 40, team="enemy"),
                  arrow(38, 52, 70, 42, "attack"), badge("优先最近/满能量")], title="挑人出手"),
            beat("RA2：混选点空中目标", "防空单位开火，纯对地坦克不抬枪", "该打的打", "topdown",
                 [unit(25, 62, sel=True, role="坦"), unit(40, 55, sel=True, size=1.1, role="防空"),
                  unit(70, 34, team="enemy", face=180, layer="air"),
                  ring(40, 55), arrow(42, 52, 68, 34, "attack"), badge("仅防空")], title="防空优先"),
        ], ["SC2", "RA2", "RTS"],
    ))
    c.append(case(
        "multi-cast-split-targets", "multi-cast", "多人多目标：一人打一个",
        "多选施法单位后，依次点多个目标，每人认领一个。"
        "像 SC2 多选高圣堂依次反馈不同敌方；RA2 多选疯狂伊文对多建筑埋弹。",
        [
            beat("选多人，点技能后依次点目标 A/B/C", "每人头顶认领一条线指向各自目标", "分开点名", "topdown",
                 [unit(20, 60, sel=True), unit(32, 55, sel=True), unit(44, 62, sel=True),
                  unit(68, 30, team="enemy"), unit(78, 48, team="enemy"), unit(70, 68, team="enemy"),
                  arrow(22, 58, 66, 34, "attack"), arrow(34, 54, 76, 48, "attack"),
                  arrow(46, 60, 68, 66, "attack"),
                  queue_no(20, 49, 1, "active"), queue_no(32, 44, 2, "active"),
                  queue_no(44, 51, 3, "active"), badge("一人一目标")], title="拆目标"),
            beat("全部认领完毕", "各自同时出手，三个目标同拍挨打", "一波带走", "topdown",
                 [unit(20, 60, sel=True), unit(32, 55, sel=True), unit(44, 62, sel=True),
                  unit(68, 30, team="enemy"), impact(68, 30, 12),
                  unit(78, 48, team="enemy"), impact(78, 48, 12),
                  unit(70, 68, team="enemy"), impact(70, 68, 12),
                  arrow(22, 58, 66, 34, "attack"), arrow(34, 54, 76, 48, "attack"),
                  arrow(46, 60, 68, 66, "attack"), badge("齐发·三个同拍挨打")], title="齐发"),
        ], ["SC2", "RA2", "RTS"],
    ))

    # ===== 十七、选中谁 × 点到谁 =====
    c.append(case(
        "context-sc2-rightclick", "context-order", "SC2：同一右键，选中×目标不同结果不同",
        "右键没有固定含义——看你手里抓着谁、点到了什么。"
        "工人点矿去采、点气矿去采气、点地面去走；战斗单位点敌去打、点地面去走；治疗者点伤员去治。",
        [
            beat("选 SCV，右键矿物", "工人去采矿，不攻击", "去挖矿", "topdown",
                 [unit(30, 55, sel=True, role="工"), ring(30, 55), prop(70, 42, "矿", kind="ore"),
                  ring(70, 42, r=11, kind="buff"), arrow(32, 54, 66, 44, "move"),
                  cursor(70, 42, "up"), badge("工人×矿·采")], title="工人点矿"),
            beat("选陆战队员，右键同一矿物", "当普通地面走过去（或无效采集）", "不会去挖", "topdown",
                 [unit(30, 55, sel=True, face=20, role="兵"), ring(30, 55), prop(70, 42, "矿", kind="ore"),
                  deny(70, 62, "士兵不会采"), circle_ind(66, 48, 7, True),
                  arrow(32, 54, 64, 47, "move"), cursor(70, 42, "up"),
                  badge("士兵×矿·走")], title="士兵点矿"),
            beat("选战斗单位，右键敌人", "攻击该敌人", "去干他", "topdown",
                 [unit(30, 58, sel=True, face=25, role="兵"), ring(30, 58), unit(72, 40, team="enemy"),
                  arrow(34, 56, 68, 42, "attack"), cursor(72, 40, "up"), badge("兵×敌人")], title="兵点敌人"),
            beat("选医疗兵，右键受伤友军", "跑去治疗，不是攻击", "去救人", "topdown",
                 [unit(28, 58, sel=True, role="医"), ring(28, 58), unit(70, 45, team="ally"),
                  arrow(32, 56, 66, 46, "move"), ring(70, 45, r=10, kind="buff"),
                  cursor(70, 45, "up"), badge("医×伤员")], title="医兵救人"),
        ], ["SC2", "RTS"],
    ))
    c.append(case(
        "context-ra2-rightclick", "context-order", "RA2：兵种决定右键在对象上干什么",
        "同样右键点建筑/单位，工程师是占领或修理，间谍是潜入，普通坦克是攻击或移动，消融步兵是抹除。",
        [
            beat("选工程师，右键敌方建筑", "冲去占领，不是炮击", "去抢房子", "topdown",
                 [unit(28, 58, sel=True, role="工"), ring(28, 58), building(70, 42, team="enemy"),
                  arrow(32, 56, 66, 44, "move"), cursor(70, 42, "up"), badge("工兵×敌建筑")], title="占领"),
            beat("选工程师，右键己方损伤建筑", "过去修理", "去修", "topdown",
                 [unit(28, 58, sel=True, role="工"), ring(28, 58), building(70, 42, team="ally"),
                  arrow(32, 56, 66, 44, "move"), ring(70, 42, r=12, kind="buff"),
                  cursor(70, 42, "up"), badge("工兵×己建筑")], title="修理"),
            beat("选坦克，右键同一敌建筑", "开炮攻击建筑", "轰平它", "topdown",
                 [unit(28, 58, sel=True, size=1.15, role="坦"), ring(28, 58),
                  building(70, 42, team="enemy"), arrow(34, 56, 66, 44, "attack"),
                  cursor(70, 42, "up"), badge("坦克×敌建筑")], title="炮击"),
            beat("选消融步兵，右键敌单位", "擦除目标，不是普通射击", "抹掉", "topdown",
                 [unit(30, 58, sel=True, role="融"), ring(30, 58), unit(72, 42, team="enemy"),
                  impact(72, 42, 15, heavy=True), circle_ind(72, 42, 20, False),
                  path([(34, 56), (68, 44)], "arc"), badge("消融中")], title="消融"),
        ], ["RA2", "RTS"],
    ))
    c.append(case(
        "context-ground-vs-object", "context-order", "右键地面 vs 右键对象",
        "同一批选中：点空地通常是走/攻击移动；点对象才触发采集、攻击、占领、治疗、装载等“对物技能”。",
        [
            beat("选中部队，右键空地", "出现移动旗/路点，全员开拔", "去那儿", "topdown",
                 [unit(25, 55, sel=True), unit(35, 62, sel=True), ring(25, 55), ring(35, 62),
                  cursor(72, 40, "up"), arrow(30, 56, 70, 42, "move"), badge("右键地面")], title="点地面"),
            beat("同一选中，右键敌单位", "攻击线指向该对象", "锁定他打", "topdown",
                 [unit(25, 55, sel=True), unit(35, 62, sel=True), ring(25, 55), ring(35, 62),
                  unit(72, 40, team="enemy"), cursor(72, 40, "up"),
                  arrow(28, 54, 68, 42, "attack"), badge("右键对象")], title="点对象"),
            beat("选工人，右键空地", "出现移动旗，工人走开", "去那儿", "topdown",
                 [unit(30, 58, sel=True, role="工"), ring(30, 58), prop(68, 38, "矿", kind="ore"),
                  arrow(32, 56, 50, 70, "move"), circle_ind(50, 70, 8, True),
                  cursor(50, 70, "up"), badge("右键地面=走")], title="工人点地"),
            beat("选工人，右键矿", "下达采集，工人奔向矿点", "去挖", "topdown",
                 [unit(30, 58, sel=True, role="工"), ring(30, 58), prop(68, 38, "矿", kind="ore"),
                  arrow(34, 56, 64, 40, "move"), ring(68, 38, r=10, kind="buff"),
                  cursor(68, 38, "up"), badge("右键矿=采")], title="工人点矿"),
        ], ["SC2", "RA2", "RTS"],
    ))
    c.append(case(
        "context-mixed-selection", "context-order", "混选时右键：各干各的智能活",
        "一框里既有工人又有兵：右键矿→工人去采、兵走开或待机；右键敌人→兵去打、工人逃跑/不管。"
        "SC2 混编智能指令是典型；RA2 混选工程师与坦克点建筑时也应各走各的语义。",
        [
            beat("混选工人+士兵，右键矿物", "工人去采；士兵不采，通常走开或停", "人各有活", "topdown",
                 [unit(24, 58, sel=True, size=0.85), unit(36, 52, sel=True, face=-20, size=1.2),
                  ring(24, 58), ring(36, 52), prop(72, 40, "矿", kind="ore"),
                  arrow(26, 56, 68, 42, "move"), arrow(36, 52, 36, 38, "move"),
                  badge("工人采·士兵走开")], title="混选点矿"),
            beat("混选工人+士兵，右键敌人", "士兵进攻；工人不冲锋（或逃跑）", "兵打仗", "topdown",
                 [unit(24, 58, sel=True), unit(36, 52, sel=True, face=25), ring(24, 58), ring(36, 52),
                  unit(74, 40, team="enemy"), arrow(38, 52, 70, 42, "attack"),
                  badge("混选×敌人")], title="混选点敌"),
            beat("混选工程师+坦克，右键敌建筑（RA2）", "工程师去占；坦克去轰", "占的占打的打", "topdown",
                 [unit(24, 60, sel=True), unit(38, 54, sel=True, size=1.15), ring(24, 60), ring(38, 54),
                  building(72, 42), arrow(26, 58, 66, 44, "move"), arrow(40, 54, 68, 44, "attack"),
                  badge("混选×建筑")], title="占+轰"),
        ], ["SC2", "RA2", "RTS"],
    ))
    c.append(case(
        "context-stance-changes-verb", "context-order", "同一单位形态变了，右键含义也变",
        "还是那辆车，形态一变右键语义跟着变。SC2 攻城坦克：坦克形态右键敌=直射推进，攻城形态右键敌=原地炮击；"
        "变形后技能栏与默认右键都换一套。",
        [
            beat("坦克形态，右键敌人", "开过去边走边打", "追着打", "topdown",
                 [unit(30, 58, sel=True, size=1.2, role="车"), ring(30, 58), unit(72, 40, team="enemy"),
                  arrow(34, 56, 56, 48, "move"), arrow(56, 48, 68, 42, "attack"),
                  badge("坦克形态·边开边打")], title="车形态"),
            beat("切到攻城形态后，右键同一敌人", "就地架炮，不再追身", "站桩轰", "topdown",
                 [unit(30, 58, sel=True, size=1.3, role="炮"), ring(30, 58), unit(72, 40, team="enemy"),
                  arrow(36, 56, 68, 42, "attack"), circle_ind(30, 58, 26, True),
                  impact(72, 40, 14), deny(30, 78, "不再移动"),
                  badge("攻城形态·钉在原地")], title="炮形态"),
        ], ["SC2", "RTS"],
    ))

    # ===== 十八、临时多出来的技能 =====
    c.append(case(
        "temp-kit-gow-transform", "temp-kit", "变身：整栏技能临时换成另一套",
        "像战神4进入斯巴达之怒/瓦尔基里一类变身：平时那栏技能收起，换成变身专属攻击与技能；"
        "变身计时结束或主动解除后，旧技能栏回来，临时技全部撤走。",
        [
            beat("触发变身（集满槽/按变身键）", "角色换形；技能栏整页替换为变身技", "换了一套身子", "tps",
                 [hero(48, 55, face=20), ring(48, 55, r=16, kind="buff"),
                  bar(48, 30, 0.9, "charge", "变身剩余"), hotbar(page="变身套"),
                  badge("整栏换成变身技")], title="进入变身"),
            beat("变身期间按攻击/技能", "打出的是变身专属连段与技能，不是原武器", "这套只能现在用", "tps",
                 [hero(45, 55, face=30), cone(45, 55, angle=-20, spread=50, length=32),
                  unit(72, 40, team="enemy"), impact(72, 40, 15, heavy=True),
                  bar(45, 30, 0.5, "charge", "变身剩余"), hotbar(active=0, page="变身套"),
                  badge("放的是变身技")], title="用临时技"),
            beat("变身结束", "外形复原；变身那套收走，原技能回到原位", "变回来了", "tps",
                 [hero(48, 55, face=10), bar(48, 30, 0.0, "charge", "变身结束"),
                  hotbar(), badge("换回原栏")], title="解除"),
        ], ["战神4", "动作RPG"],
    ))
    c.append(case(
        "temp-kit-timed-overlay", "temp-kit", "限时叠加：原技能还在，多挂几颗临时键",
        "不变身换整栏，而是一段时间多出 1～N 个键（拾取武器、英雄时刻、召唤物附体）。"
        "倒计时结束临时键灰掉消失，原技能布局不动。",
        [
            beat("获得临时技能（拾取/触发）", "技能栏多出高亮新键，带倒计时", "多了几招", "moba",
                 [hero(40, 55), badge("+临时技")], title="授予"),
            beat("按临时键释放", "打出仅此期间可用的效果", "趁现在", "moba",
                 [hero(40, 55), circle_ind(70, 40, 14, True), cursor(70, 40), badge("临时技放出")], title="使用"),
            beat("倒计时走完", "临时键消失，栏位复原", "没了", "moba",
                 [hero(40, 55), hotbar(), bar(40, 33, 0, "cast", "倒计时"),
                  badge("临时技收回")], title="收回"),
        ], ["动作RPG", "MOBA", "ARPG"],
    ))
    c.append(case(
        "temp-kit-rts-morph-ability", "temp-kit", "RTS 限时形态：面板技能临时换掉",
        "单位进入临时形态时，命令卡换成形态技能；结束回到原卡。"
        "像 SC2 某些增益/形态窗，或英雄单位短暂开启的额外指令；不是永久升级。",
        [
            beat("单位进入限时形态", "选中他时命令卡换成形态技", "这会儿能按新键", "topdown",
                 [unit(45, 55, sel=True, size=1.25), ring(45, 55),
                  menu_box(60, 34, ["形态卡", "冲击波", "护盾", "冲锋"]),
                  hotbar(active=0), badge("形态命令卡")], title="形态开始"),
            beat("形态计时结束", "命令卡切回原技能，临时键不可用", "面板变回去", "topdown",
                 [unit(45, 55, sel=True), ring(45, 55),
                  menu_box(60, 34, ["原卡", "移动", "攻击", "技能"]),
                  hotbar(), deny(30, 34, "形态键已收回"), badge("原卡")], title="形态结束"),
        ], ["SC2", "RTS"],
    ))
    c.append(case(
        "temp-kit-item-trinket", "temp-kit", "饰品/道具按下才冒出来的主动技",
        "装备某件饰品或任务道具后，技能栏多一颗主动键；卸下或任务结束键消失。"
        "像魔兽饰品 on-use、大秘境钥匙类道具技能。",
        [
            beat("装备带主动的饰品", "技能栏多出饰品键，显示层数/CD", "多了一招装备技", "moba",
                 [hero(45, 55), hotbar(extra=3), badge("+饰品主动")], title="装上出现"),
            beat("按下饰品主动", "打出饰品效果，进入饰品 CD", "用装备技", "moba",
                 [hero(45, 55), ring(45, 55, r=14, kind="buff"), hotbar(cd=3),
                  badge("饰品放出")], title="使用"),
            beat("卸下饰品或任务收回", "该键从栏位消失", "招没了", "moba",
                 [hero(45, 55), hotbar(off=[3]), badge("键收回")], title="卸下收回"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "temp-kit-steal-copy", "temp-kit", "偷来 / 复制来的技能",
        "从敌人或环境得到对方技能的临时拷贝，用一次或限时后消失。"
        "像英雄联盟劫/塞拉斯偷大、部分动作游戏吸收招式。",
        [
            beat("成功偷取/复制", "栏位出现对方技能图标，标“临时”", "这招是借的", "moba",
                 [hero(40, 55), unit(70, 40, team="enemy"), hotbar(extra=3), badge("偷到技能")], title="获得拷贝"),
            beat("按该临时键", "打出被偷技能的效果", "用他的招", "moba",
                 [hero(40, 55), circle_ind(70, 40, 14, True), hotbar(extra=3, active=3), badge("释放拷贝")], title="释放"),
            beat("次数用尽或计时结束", "拷贝图标消失", "还回去了", "moba",
                 [hero(40, 55), hotbar(), badge("拷贝消失")], title="消失"),
        ], ["MOBA", "动作RPG"],
    ))
    c.append(case(
        "temp-kit-empower-next", "temp-kit", "强化下一击：先充能，再打出去",
        "按键进入“下一击强化”状态，普攻或下一次技能吃到加成后状态消耗。"
        "不是永久多一招，是窗口内改写下一次出手。",
        [
            beat("按下强化键", "武器/拳头发光，提示“下一击强化”", "蓄着劲", "tps",
                 [hero(45, 55), ring(45, 55, r=12, kind="buff"), badge("下一击")], title="充能"),
            beat("下一次攻击命中", "打出强化效果，发光消失", "这下够疼", "tps",
                 [hero(42, 55, face=20), unit(68, 42, team="enemy"),
                  arrow(48, 52, 64, 44, "attack"), impact(68, 42, 18, heavy=True),
                  badge("强化打出")], title="消耗"),
        ], ["动作RPG", "MOBA", "ARPG"],
    ))
    c.append(case(
        "temp-kit-vehicle-gunner", "temp-kit", "载具 / 炮台座位：整套操作临时替换",
        "上车或上炮位后，移动与射击键语义全换成载具武器；下车立刻恢复步行那套。"
        "和变身类似，但是“座位授予”而不是角色变身。",
        [
            beat("进入炮位/驾驶位", "准星变车炮；技能栏变载具武器", "换成开车手感", "tps",
                 [vehicle(42, 55, "turret", occupied=True), crosshair(76, 38),
                  stick("L", 0, -0.6), stick("R", 0.6, -0.2),
                  hotbar(extra=0, slots=3), badge("进入座位")], title="上车"),
            beat("下车 / 被炸下车", "操作与技能栏瞬间回到步行", "又变回人", "tps",
                 [hero(36, 58), vehicle(66, 50, "turret"), hotbar(slots=4),
                  badge("离开座位")], title="下车"),
        ], ["TPS", "FPS", "MMO"],
    ))
    c.append(case(
        "temp-kit-shrine-zone", "temp-kit", "神龛 / 区域buff：站进去才有的临时技",
        "踩进光环或交互神龛后短时获得技能或弹药；离开区域或超时收回。"
        "像命运2 公共事件球、部分 MMO 祭坛。",
        [
            beat("走进神龛范围或交互启动", "获得临时技能/弹药提示", "这片地给力", "tps",
                 [hero(45, 58), prop(45, 40, "神龛", kind="shrine", highlight=True),
                  ring(45, 58, r=26, kind="buff"), hotbar(extra=3),
                  badge("神龛授予")], title="获得"),
            beat("离开范围或超时", "临时技/弹药加成消失", "出圈就没", "tps",
                 [hero(78, 40), prop(45, 40, "神龛", kind="shrine"), ring(45, 58, r=26, kind="select"),
                  hotbar(), deny(78, 58, "出圈失效"), badge("离开收回")], title="收回"),
        ], ["MMO", "TPS", "ARPG"],
    ))
    c.append(case(
        "temp-kit-mount-combat", "temp-kit", "骑乘战斗：马上多出来的键",
        "上马后出现冲撞、加速、马上射击等骑乘技；下马这些键消失，陆地技回来。",
        [
            beat("上马成功", "技能栏出现骑乘技，移动变坐骑手感", "马上能按新键", "tps",
                 [hero(48, 55), hotbar(extra=3), badge("骑乘技")], title="上马"),
            beat("下马", "骑乘技消失，恢复陆地技能栏", "下地", "tps",
                 [hero(48, 55), hotbar(), badge("陆地技")], title="下马"),
        ], ["魔兽世界", "MMO", "动作RPG"],
    ))

    # ===== 十九、物品 =====
    c.append(case(
        "item-pickup-world", "item", "地上捡东西",
        "走近可拾取物，按交互键或右键拾取进包；包满要明确拒绝。",
        [
            beat("走近掉落物，出现拾取提示", "名称/品质飘在地上", "能捡", "tps",
                 [hero(40, 55), prop(62, 48, "绿装", kind="item", highlight=True),
                  circle_ind(62, 48, 14, True), menu_box(70, 58, ["精良 护腕"]),
                  keyhint(52, 34, "F", "active", "拾取"), badge("可拾取")], title="靠近"),
            beat("按交互键 / 右键拾取", "物品进背包，地上消失", "到手了", "tps",
                 [hero(45, 52), card(55, 34, "绿装", 0), menu_box(26, 68, ["背包 +1"]),
                  badge("已入包")], title="捡起"),
            beat("背包已满时再按拾取", "红字拒绝，掉落物仍留在地上", "拿不下", "tps",
                 [hero(40, 55), prop(62, 48, "绿装", kind="item"), circle_ind(62, 48, 14, False),
                  hotbar(deny=0), menu_box(26, 62, ["背包已满！"]), deny(62, 66, "包满"),
                  keyhint(52, 34, "F", "off", "拾取"), badge("包满拒绝")], title="包满"),
        ], ["MMO", "ARPG", "动作RPG"],
    ))
    c.append(case(
        "item-loot-window", "item", "开箱子 / 尸体掉落窗",
        "对箱子或尸体交互弹出战利品窗，逐格拾取或全部拾取；需掷骰时先看见分配。",
        [
            beat("对箱子/尸体按交互", "弹出掉落列表", "看看有啥", "moba",
                 [hero(32, 62), prop(78, 40, "宝箱", kind="chest", highlight=True),
                  keyhint(78, 22, "F", "active", "搜索"),
                  menu_box(48, 48, ["铁矿×3", "破剑", "布料"]), badge("掉落窗")], title="打开"),
            beat("点一项或点全部拾取", "进包；窗内该项消失", "拿走", "moba",
                 [hero(32, 62), prop(78, 40, "宝箱", kind="chest"),
                  menu_box(48, 48, ["铁矿×3", "布料"], active=1), card(24, 78, "破剑", 0),
                  badge("进包 -1")], title="拾取"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "item-need-greed", "item", "团队掷骰：需求 / 贪婪 / 放弃",
        "稀有掉落弹窗，限时选择需求、贪婪或放弃；结果出来再进某人的包。",
        [
            beat("稀有物品弹出掷骰窗", "倒计时与三个按钮", "快选", "moba",
                 [hero(40, 55), card(62, 48, "紫装", 0), menu_box(72, 60, ["需求", "贪婪", "放弃"]),
                  badge("掷骰窗")], title="弹窗"),
            beat("点需求或贪婪", "等待其他人；出结果后归属提示", "看谁赢", "moba",
                 [hero(40, 55), unit(65, 45, team="ally"), card(55, 40, "紫装", 0),
                  badge("归属结果")], title="结果"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "item-use-self", "item", "对自己用消耗品",
        "快捷栏点药水/食物：读条或瞬发，叠 CD，数量 -1。",
        [
            beat("按药水快捷键", "自己吃增益/回血，图标进入物品 CD", "补一口", "moba",
                 [hero(48, 55), ring(48, 55, r=12, kind="buff"), bar(48, 32, 0.7, "hp", "回血"),
                  hotbar(cd=2), badge("用药")], title="自用"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "item-use-on-target", "item", "对目标用物品",
        "先选中友方/敌方，再点物品；或点物品再点目标。"
        "像复活石点队友、减速陷阱点地面/敌人。",
        [
            beat("选中目标后按物品键", "物品效果打在目标上，消耗数量", "用在他身上", "moba",
                 [hero(35, 58), unit(65, 45, team="ally", sel=True), ring(65, 45, r=10, kind="buff"),
                  hotbar(active=1), badge("物品→目标")], title="对目标"),
            beat("先点物品再点目标", "进入“物品瞄准”，点合法目标才消耗", "指哪用哪", "moba",
                 [hero(35, 58), cursor(65, 45), unit(65, 45, team="ally"), badge("物品瞄准")], title="先物品后点"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "item-use-on-ground", "item", "对地面用物品",
        "炸弹、旗帜、营帐：点物品后点地板落下。和技能点地同一手感，消耗的是物品次数。",
        [
            beat("点快捷栏里的物品", "进入落点预览，光圈跟随鼠标", "选个位置", "topdown",
                 [hero(35, 60), circle_ind(70, 40, 14, True), cursor(70, 40, "aim"),
                  hotbar(active=1), badge("落点预览")], title="预览"),
            beat("点地面确认", "物品数量 -1，效果实体出现在落点", "丢那儿", "topdown",
                 [hero(35, 60), building(70, 40, ghost=False), hotbar(cd=1),
                  badge("已放置")], title="放置"),
        ], ["MMO", "MOBA", "ARPG"],
    ))
    c.append(case(
        "item-equip-swap", "item", "穿上 / 脱下 / 替换装备",
        "从背包双击或拖到装备槽；槽位已有装备则替换进包，属性立刻变。",
        [
            beat("双击背包里的武器", "进入装备槽；旧武器回包（若有）", "换好了", "moba",
                 [hero(28, 55), card(48, 40, "新剑", 0, True), card(70, 42, "旧斧"),
                  menu_box(48, 62, ["武器槽←剑", "旧斧→包"]), badge("装备")], title="穿上"),
            beat("从装备槽拖回背包或点卸下", "槽空了，属性去掉", "脱下", "moba",
                 [hero(28, 55), box(58, 38, 18, 18),
                  menu_box(48, 58, ["武器槽：（空）", "ATK -12"]), badge("卸下")], title="卸下"),
        ], ["MMO", "ARPG"],
    ))
    c.append(case(
        "item-hotbar-drag", "item", "拖到快捷栏",
        "从背包拖到快捷栏格子，之后可用数字键使用；拖走或替换会改键位映射。",
        [
            beat("从背包拖到快捷栏空位", "栏位出现物品图标与数量", "键位绑好了", "moba",
                 [card(35, 40, "药", None, True), cursor(42, 88, "drag"), hotbar(active=0, extra=0),
                  badge("拖到快捷栏")], title="拖上栏"),
            beat("按对应数字键", "等同于使用该物品", "键上就能用", "moba",
                 [hero(48, 55), hotbar(active=0), keyhint(28, 72, "1", "active", "用药"),
                  ring(48, 55, r=12, kind="buff"), badge("快捷使用")], title="快捷用"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "item-split-stack", "item", "拆堆 / 合堆",
        "按住修饰键拖动堆叠，输入数量拆成两堆；拖到同类上合并。",
        [
            beat("Shift+拖动堆叠，输入数量", "拆成两堆", "分开装", "moba",
                 [card(40, 50, "×20", 0), card(62, 50, "×5", 0, True), cursor(62, 50, "drag"),
                  badge("拆堆")], title="拆"),
            beat("拖到同类物品上", "数量合并（不超过上限）", "摞一起", "moba",
                 [card(50, 50, "×25", 0), cursor(50, 50, "up"), badge("合堆")], title="合"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "item-compare-tooltip", "item", "悬停对比：身上 vs 包里",
        "鼠标悬停背包装备时，并排显示当前已穿装备的数值差。",
        [
            beat("鼠标悬停背包中的装备", "双提示框：新装备 + 已穿对照", "看看谁更好", "moba",
                 [card(40, 58, "剑", 0, True), cursor(40, 58),
                  menu_box(18, 28, ["包里 剑 +12", "暴击 +5"]),
                  menu_box(58, 28, ["已穿 剑 +8", "暴击 +2"]),
                  badge("对比提示")], title="对比"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "item-destroy-sell", "item", "摧毁 / 卖店",
        "拖到摧毁或卖店确认；贵重物品要二次确认，防止手滑。",
        [
            beat("拖到出售/摧毁区", "弹出标价或警告确认框", "再想想", "moba",
                 [card(40, 50, "破装", 0, True), cursor(65, 55, "drag"),
                  menu_box(62, 48, ["出售 12G", "摧毁", "取消"]), badge("出售确认")], title="确认框"),
            beat("点确认", "物品离包，金币入账；贵重物品要再输名字/二次确认", "卖掉了", "moba",
                 [menu_box(40, 45, ["+12G", "背包 -1"]), badge("已卖出")], title="成交"),
        ], ["MMO", "ARPG"],
    ))
    c.append(case(
        "item-charges-durability", "item", "次数道具与耐久",
        "魔杖/爆炸物显示剩余次数；装备掉耐久到 0 失效或强制修理提示。",
        [
            beat("使用带次数的物品", "次数 -1，到 0 物品消失或变灰", "还能用几次", "moba",
                 [hero(48, 55), card(62, 48, "魔杖×2", 0), hotbar(dot=0),
                  menu_box(28, 68, ["次数 3→2"]), badge("次数-1")], title="扣次数"),
            beat("装备耐久耗尽", "属性失效或红字提示去修", "该修了", "moba",
                 [hero(48, 55), card(62, 48, "破甲", 0),
                  menu_box(28, 62, ["耐久 0", "属性失效", "去修理"]), badge("耐久耗尽")], title="耐久"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))

    # ===== 二十、MMO 社交 =====
    c.append(case(
        "mmo-tab-target", "mmo-social", "按 Tab 在敌人之间轮换目标",
        "键盘按一下 Tab 就把当前目标换到前方下一个敌人，不用把鼠标移过去。"
        "前方没有可选敌人时要明确没反应，而不是悄悄选到背后或很远的东西。",
        [
            beat("按一下 Tab", "最近那个敌人成为当前目标，套上锁定圈", "先咬住近的", "tps",
                 [hero(30, 62), unit(55, 42, team="enemy"), ring(55, 42, kind="lock"),
                  unit(76, 50, team="enemy"), keyhint(24, 82, "Tab", "active", "换目标"),
                  badge("目标=近的那个")], title="选中最近的"),
            beat("再按一下 Tab", "目标换到下一个敌人，上一个的锁定圈撤掉", "换下一个", "tps",
                 [hero(30, 62), unit(55, 42, team="enemy"), unit(76, 50, team="enemy"),
                  ring(76, 50, kind="lock"), keyhint(24, 82, "Tab", "active", "换目标"),
                  queue_no(55, 30, 1, "done"), queue_no(76, 36, 2, "active"),
                  badge("轮到第二个")], title="换到下一个"),
            beat("前方没有可选敌人时按 Tab", "明确没反应，不乱选背后或超远的目标", "按不动", "tps",
                 [hero(30, 62), unit(92, 20, team="enemy"), circle_ind(30, 62, 26, False),
                  keyhint(24, 82, "Tab", "off"), deny(58, 50, "前方无可选目标"),
                  badge("Tab 无效")], title="没得选"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-party-frame-target", "mmo-social", "点界面上的队友头像选中他",
        "把鼠标移到队伍框里某个队友的头像上点一下，他就成了我的当前目标，"
        "接着放治疗就落在他身上。这是用界面选人，和在世界里点角色是两条路。",
        [
            beat("队伍框里有人掉血", "那一行的血条变短变色，提醒我该看他", "谁在掉血", "moba",
                 [hero(46, 58), unit(66, 48, team="ally"),
                  partyframe(6, 24, [{"name": "我", "hp": 1.0},
                                     {"name": "友A", "hp": 0.28},
                                     {"name": "友B", "hp": 0.9}]),
                  badge("友A 残血")], title="看见掉血"),
            beat("点他的头像", "该行高亮，他成为我的当前友好目标", "选他", "moba",
                 [hero(46, 58), unit(66, 48, team="ally"), ring(66, 48, kind="lock"),
                  partyframe(6, 24, [{"name": "我", "hp": 1.0},
                                     {"name": "友A", "hp": 0.28},
                                     {"name": "友B", "hp": 0.9}], target=1),
                  cursor(16, 44, "up"), badge("友好目标=友A")], title="点头像"),
            beat("按治疗键", "治疗落在被选中的队友身上，不用把鼠标移到他角色上", "奶上去", "moba",
                 [hero(46, 58), unit(66, 48, team="ally", sel=True), ring(66, 48, kind="buff"),
                  partyframe(6, 24, [{"name": "我", "hp": 1.0},
                                     {"name": "友A", "hp": 0.72},
                                     {"name": "友B", "hp": 0.9}], target=1),
                  arrow(50, 56, 62, 50, "move"), hotbar(active=0, cd=0),
                  badge("治疗到位")], title="对他放治疗"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-interact-npc", "mmo-social", "对 NPC 按交互：任务 / 商店 / 对话",
        "面向可交互 NPC 按键，打开任务、商店或对话——同一键，NPC 类型决定面板。",
        [
            beat("靠近 NPC，出现交互提示", "名称与类型（任务感叹号等）", "能说话", "tps",
                 [hero(38, 58), npc(64, 46, "quest"), keyhint(64, 26, "F", "active", "交谈"),
                  badge("可交互")], title="靠近"),
            beat("按交互键", "打开任务/对话/商店面板", "开始谈", "tps",
                 [hero(30, 62), npc(64, 46, "quest"),
                  menu_box(44, 30, ["接受任务", "商店", "再见"]),
                  keyhint(64, 26, "F", "active", "交谈"), badge("对话面板")], title="交互"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-dialogue-choice", "mmo-social", "对话选项与跳过",
        "对话里点选项推进分支；可跳过旁白，但关键选择不能被静默跳过。",
        [
            beat("对话播放中点跳过", "旁白快进到下一句或选项", "说快点", "moba",
                 [unit(60, 45, team="neutral"), menu_box(30, 65, ["……旁白……", "[跳过]"]),
                  badge("跳过旁白")], title="跳过"),
            beat("点一个对话选项", "分支推进，面板换下一页", "选这条", "moba",
                 [unit(60, 45, team="neutral"), cursor(40, 70),
                  menu_box(28, 60, ["接受任务", "再看看", "再见"]), badge("选项")], title="选项"),
        ], ["MMO", "动作RPG", "AVG"],
    ))
    c.append(case(
        "mmo-party-invite", "mmo-social", "组队邀请 / 接受 / 离队",
        "右键玩家或用命令邀请；对方弹窗接受；队长有额外权限提示。",
        [
            beat("对玩家发组队邀请", "对方屏幕弹出邀请", "邀了", "moba",
                 [hero(35, 55), unit(65, 45, team="ally"),
                  menu_box(58, 36, ["组队邀请", "接受", "拒绝"]), badge("邀请")], title="邀请"),
            beat("点接受", "进队，队伍框多出他那一行", "组上了", "moba",
                 [hero(40, 58), unit(66, 48, team="ally"),
                  partyframe(6, 26, [{"name": "我", "hp": 1.0},
                                     {"name": "阿强", "hp": 0.95}]),
                  badge("队伍 2/5")], title="接受"),
            beat("点离队或被移出", "队伍框只剩我一行", "散了", "moba",
                 [hero(48, 58), partyframe(6, 26, [{"name": "我", "hp": 1.0}]),
                  cursor(28, 44, "up"), badge("队伍 1/5")], title="离队"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-follow-assist", "mmo-social", "跟随 / 协助",
        "跟随队友自动走；协助把你的目标设成队友的目标，方便治疗或集火。",
        [
            beat("对队友选跟随", "自动朝队友移动", "跟着走", "tps",
                 [hero(35, 60), unit(60, 45, team="ally"), arrow(38, 58, 56, 48, "move"),
                  badge("跟随")], title="跟随"),
            beat("按协助", "当前目标变成队友的目标", "打他打的", "tps",
                 [hero(35, 60), unit(55, 40, team="ally"), unit(75, 42, team="enemy"),
                  ring(75, 42, kind="lock"), badge("协助")], title="协助"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-trade", "mmo-social", "交易窗：两边放东西再确认",
        "对玩家交易，双方拖入物品与钱，双方点确认才成交；一人取消全黄。",
        [
            beat("对玩家发起交易", "双方弹出交易窗", "来交易", "moba",
                 [hero(35, 55), unit(65, 45, team="ally"),
                  menu_box(16, 38, ["你的报价", "(空)", "金币 0"]),
                  menu_box(58, 38, ["对方报价", "(空)", "金币 0"]),
                  badge("交易窗")], title="打开"),
            beat("双方拖入物品", "窗内出现报价物，待确认", "先放齐", "moba",
                 [hero(30, 55), unit(70, 45, team="ally"),
                  menu_box(16, 38, ["你的报价", "矿石", "金币 20"]),
                  menu_box(58, 38, ["对方报价", "币袋", "金币 0"]),
                  card(42, 62, "矿", 0), card(58, 62, "币", 0), badge("已报价")], title="报价"),
            beat("双方点确认", "物品交换完成：矿石到了对方手上，币袋到了我手上", "成交", "moba",
                 [hero(24, 60), unit(76, 42, team="ally"),
                  card(30, 74, "币", 0), card(72, 66, "矿", 0),
                  arrow(44, 62, 68, 66, "move"), arrow(62, 56, 36, 70, "move"),
                  menu_box(40, 20, ["交易完成"]), badge("成交·各自到手")], title="成交"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-whisper-mark", "mmo-social", "密聊 / 标记目标给队友",
        "对玩家密语；在目标上打标记（骷髅/月亮），团队可见以便集火。",
        [
            beat("对玩家发密语", "聊天频道切到密语", "私聊", "moba",
                 [hero(35, 55), unit(65, 45, team="ally"), menu_box(30, 70, ["密语: 你好"]), badge("密语")], title="密聊"),
            beat("给当前目标打团队标记", "头顶出现标记图标，队友看见", "集火这个", "tps",
                 [unit(60, 45, team="enemy"), marker(60, 26, "skull", "集火"),
                  unit(30, 58, team="ally"), marker(30, 40, "moon", "队友看到"),
                  badge("团队标记")], title="标记"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-emote-sit", "mmo-social", "表情 / 坐下 / 检查",
        "表情轮盘或命令让角色做动作；坐下回蓝；检查看对方装备。",
        [
            beat("选表情或输入命令", "角色播放表情动画", "比个心", "tps",
                 [hero(48, 55), menu_box(66, 36, ["挥手", "跳舞", "比心"]),
                  badge("表情轮盘")], title="表情"),
            beat("坐下", "进入坐姿，开始回蓝", "坐着回", "tps",
                 [hero(48, 55), bar(48, 36, 0.45, "cast", "回蓝"), badge("坐下回蓝")], title="坐下"),
            beat("检查其他玩家", "打开其角色/装备预览", "看看装", "moba",
                 [unit(60, 45, team="ally"),
                  menu_box(22, 32, ["头", "胸甲", "武器 +12", "饰品"]),
                  card(78, 40, "剑"), card(78, 58, "甲"), badge("装备预览")], title="检查"),
        ], ["魔兽世界", "MMO"],
    ))

    # ===== 二十一、MMO 世界 =====
    c.append(case(
        "mmo-gather-channel", "mmo-world", "采集：对着矿/草读条",
        "对节点按交互，进入读条；被打或移动打断；成功进包。",
        [
            beat("对矿脉按交互", "开始读条，角色做采集动作", "挖着", "tps",
                 [hero(40, 55), prop(65, 45, "矿脉", kind="ore", highlight=True),
                  bar(40, 33, 0.45, "cast", "采集中"), badge("采集读条")], title="开始"),
            beat("读条完成", "节点枯竭或刷新，物品进包", "挖到了", "tps",
                 [hero(40, 55), prop(65, 45, "已采空", kind="ore"), card(26, 70, "矿", 0),
                  bar(40, 33, 1.0, "cast", "采集完成"), badge("采集成功")], title="完成"),
            beat("读条中被攻击或移动", "读条取消，节点仍在", "打断了", "tps",
                 [hero(40, 55), prop(65, 45, "矿脉", kind="ore"),
                  bar(40, 33, 0.45, "cast", "被打断", broken=True),
                  badge("采集打断")], title="打断"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-vendor", "mmo-world", "商人：买 / 卖 / 回购",
        "打开商人，右键买，拖背包物品卖；误卖可用回购页拿回。",
        [
            beat("与商人交互", "商店列表打开", "逛店", "moba",
                 [hero(28, 62), npc(70, 40, "vendor"),
                  menu_box(44, 30, ["药水 5G", "面包 1G", "箭袋 3G"]), badge("商店")], title="开店"),
            beat("右键购买或拖出售", "金币与物品变化，卖出立刻到账", "买到/卖掉", "moba",
                 [hero(24, 64), npc(70, 40, "vendor"), card(46, 62, "货", 0, True),
                  cursor(46, 62, "drag"), menu_box(44, 30, ["卖出 +6G", "金币 132G"]),
                  badge("买卖")], title="买卖"),
            beat("打开回购", "刚卖掉的东西可买回", "我手滑了", "moba",
                 [hero(24, 64), npc(70, 40, "vendor"),
                  menu_box(44, 30, ["回购页", "破剑 6G"]), badge("回购")], title="回购"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-mount-taxi", "mmo-world", "坐骑 / 飞行点",
        "召唤坐骑改变移动；飞行点选目的地后进入航线，途中可跳下（若规则允许）。",
        [
            beat("按召唤坐骑", "上马，移动变快，陆地战技能受限或切换", "骑上", "tps",
                 [hero(46, 46), vehicle(46, 56, "mount", occupied=True),
                  arrow(52, 52, 78, 38, "move"), hotbar(off=[1, 2, 3]), badge("坐骑")], title="上坐骑"),
            beat("在飞行管理员选目的地", "进入飞行路线镜头", "飞过去", "tps",
                 [hero(28, 62), npc(66, 42, "trainer"),
                  menu_box(44, 28, ["暴风城", "铁炉堡", "取消"]), badge("飞行点")], title="飞行点"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-release-resurrect", "mmo-world", "释放灵魂 / 复活",
        "死后选释放灵魂跑尸；或点魂匠/队友复活接受复活。",
        [
            beat("死亡后点释放灵魂", "变成灵魂形态，尸体留在原地", "去找尸体", "tps",
                 [hero(28, 62, state="ghost"), corpse(72, 40), npc(50, 30, "healer"),
                  arrow(33, 60, 66, 43, "move"), badge("灵魂形态")], title="释放"),
            beat("跑回尸体或接受复活", "回到尸体处活过来，灵魂态结束", "活了", "tps",
                 [hero(68, 44), ring(68, 44, r=12, kind="buff"), badge("复活")], title="复活"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-auto-attack", "mmo-world", "自动攻击开关",
        "有目标时开启自动挥砍/射击；再按或失去目标停止。和 MOBA 右键追击不同，是开关态。",
        [
            beat("有目标时按下自动攻击", "角色自动对目标普攻", "自己打着", "tps",
                 [hero(40, 55, face=20), unit(68, 42, team="enemy"), ring(68, 42, kind="lock"),
                  arrow(44, 54, 64, 44, "attack"), impact(68, 42, 13),
                  badge("自动攻击开")], title="开启"),
            beat("再按关闭或目标死亡", "停手", "停", "tps",
                 [hero(40, 55), badge("自动攻击关")], title="关闭"),
        ], ["魔兽世界", "MMO"],
    ))
    c.append(case(
        "mmo-interrupt-cast", "mmo-world", "打断对方读条 / 自己取消读条",
        "敌方读条时按打断技能；自己读条时可走或按取消打断自己。",
        [
            beat("敌方读条时按打断", "对方读条失败，常带锁定类惩罚", "掐掉", "tps",
                 [hero(35, 58, face=20), unit(68, 42, team="enemy"),
                  bar(68, 28, 0.55, "cast", "读条", broken=True),
                  arrow(40, 56, 64, 44, "attack"), badge("打断")], title="打断别人"),
            beat("自己读条中按取消或移动（若规则允许）", "自己读条中断，技能进 CD 或退回", "不放了", "tps",
                 [hero(48, 55), badge("取消读条")], title="取消自己"),
        ], ["魔兽世界", "MMO", "MOBA"],
    ))
    c.append(case(
        "mmo-quest-tracker", "mmo-world", "任务追踪：点目标去地图",
        "点任务追踪条目，地图/箭头标出目标；超远时给装等提示。",
        [
            beat("点击任务追踪里的目标", "地图标记或地面箭头更新", "知道去哪", "moba",
                 [hero(34, 62), menu_box(14, 18, ["主线：清理狼群", "支线：送信"], active=0),
                  cursor(22, 38, "up"), arrow(38, 60, 68, 44, "move"),
                  circle_ind(72, 40, 10, True), badge("追踪→地图")], title="点追踪"),
            beat("走进目标区域", "追踪条目打钩或换下一步指引", "到了", "moba",
                 [hero(68, 42), circle_ind(70, 40, 12, True),
                  menu_box(28, 65, ["任务：√ 已到达"]), badge("阶段完成")], title="到达"),
        ], ["魔兽世界", "MMO", "ARPG"],
    ))
    c.append(case(
        "mmo-mailbox-ah", "mmo-world", "邮箱 / 拍卖行投递",
        "邮箱取附件；拍卖行上架要填价与时限，成功后物品离包。",
        [
            beat("打开邮箱点附件", "物品进包，邮件更新", "取件", "moba",
                 [hero(40, 55), prop(66, 44, "邮箱", kind="chest"), card(54, 50, "附件", 0),
                  menu_box(24, 66, ["收件箱", "取附件"]), badge("邮箱")], title="取邮"),
            beat("拍卖行填价上架", "物品进入拍卖，包里消失", "挂上了", "moba",
                 [hero(30, 60), npc(72, 40, "auction"), menu_box(48, 30, ["一口价 12g", "已上架", "包里已无"]),
                  card(66, 78, "货", 0), badge("上架")], title="拍卖"),
        ], ["魔兽世界", "MMO"],
    ))

    # ===== 二十二、设计向界面手势 =====
    c.append(case(
        "ui-radial-wheel", "design-ui", "按住出轮盘，指向松手选定",
        "按住键弹出径向轮盘（表情、武器、标记、消耗品），指向一格松手确认；松开太早或回中取消。",
        [
            beat("按住轮盘键", "轮盘在角色旁展开", "选项铺开", "tps",
                 [hero(24, 60), wheel(58, 48, ["表情", "武器", "药", "标记"]),
                  stick("R", 0, 0), badge("轮盘展开")], title="按住"),
            beat("指向某一格松手", "执行该格动作，轮盘收起", "就是这个", "tps",
                 [hero(24, 60), wheel(58, 48, ["表情", "武器", "药", "标记"], active=1),
                  stick("R", 0.8, 0.1), badge("选定")], title="松手选定"),
            beat("指回中心松手", "取消，无动作", "算了", "tps",
                 [hero(24, 60), wheel(58, 48, ["表情", "武器", "药", "标记"]),
                  stick("R", 0, 0), badge("回中取消")], title="取消"),
        ], ["动作RPG", "TPS", "MMO"],
    ))
    c.append(case(
        "ui-hold-context-menu", "design-ui", "长按出上下文菜单",
        "对单位/物品长按或右键，弹出与对象类型相关的菜单（交易、检查、跟随、使用）。",
        [
            beat("对对象右键/长按", "弹出上下文菜单", "还能干这些", "moba",
                 [unit(55, 45, team="ally"), cursor(55, 45),
                  menu_box(62, 40, ["交易", "检查", "跟随"]), badge("上下文菜单")], title="打开菜单"),
            beat("点菜单项", "点中的那项亮一下，随即收起菜单并执行", "选这项", "moba",
                 [unit(55, 45, team="ally"), arrow(58, 45, 88, 45, "move"),
                  menu_box(62, 40, ["交易", "检查", "跟随"], active=2),
                  cursor(66, 72, "up"), badge("执行·跟随")], title="选中"),
        ], ["MMO", "RTS", "ARPG"],
    ))
    c.append(case(
        "ui-drag-drop-slot", "design-ui", "槽位拖放（技能/物品/天赋）",
        "从库拖到槽；非法槽要红叉拒绝；替换要看得见谁被挤走。",
        [
            beat("拖到合法槽松手", "槽位更新图标", "放进去了", "moba",
                 [menu_box(14, 36, ["技能库", "火球", "闪现"]),
                  card(52, 48, "火球", 0, True), cursor(62, 70, "drag"),
                  hotbar(active=2), circle_ind(62, 78, 10, True),
                  badge("合法放下")], title="合法"),
            beat("拖到非法槽", "红叉，松手弹回原处", "不能放这", "moba",
                 [menu_box(14, 36, ["技能库", "火球", "闪现"]),
                  card(52, 48, "火球", 0, True), cursor(62, 50, "drag"),
                  hotbar(deny=1), circle_ind(62, 70, 12, False), deny(62, 50, "不能放这"),
                  path([(62, 50), (28, 48)], "move"), badge("非法拒绝")], title="非法"),
        ], ["MMO", "MOBA", "ARPG"],
    ))
    c.append(case(
        "ui-confirm-destructive", "design-ui", "危险操作二次确认",
        "分解粉装、删角色、放弃任务等：先警告，确认后才执行；取消原样返回。",
        [
            beat("点危险操作", "弹出确认框，写清后果", "真的吗", "moba",
                 [hero(40, 55), menu_box(55, 45, ["删除角色？", "不可恢复"]), badge("确认框")], title="警告"),
            beat("点确认", "执行；点取消则什么都不改", "定了/算了", "moba",
                 [hero(40, 55), menu_box(55, 45, ["确认", "取消"]), cursor(62, 58), badge("确认或取消")], title="抉择"),
        ], ["MMO", "全品类"],
    ))
    c.append(case(
        "ui-ping-comm-wheel", "design-ui", "信号 / 沟通轮：点地告知队友",
        "按信号键点地板或用轮盘选“来这里/小心/进攻”，地图与世界出现短时标记。",
        [
            beat("按住信号键", "沟通轮展开", "要喊人", "moba",
                 [hero(26, 62), wheel(52, 45, ["来这里", "小心", "进攻", "撤退"]),
                  badge("打开轮盘")], title="开轮盘"),
            beat("指向一项或点地确认", "世界与小地图出现标记", "喊了一嗓子", "moba",
                 [hero(26, 62), wheel(52, 45, ["来这里", "小心", "进攻", "撤退"], active=0),
                  circle_ind(80, 70, 10, True), box(82, 12, 16, 16), circle_ind(90, 20, 3, True),
                  badge("信号落下")], title="确认"),
        ], ["MOBA", "MMO", "TPS"],
    ))
    c.append(case(
        "ui-camera-modes", "design-ui", "镜头：锁定跟随 / 自由转 / 拉远",
        "切换镜头模式改变操作手感：锁定贴背、自由转观察、滚轮拉距离；模式切换要可见。",
        [
            beat("切换到自由镜头并拖动", "相机离角色旋转，角色不立刻转身", "四处看", "tps",
                 [hero(38, 62, face=-90), path([(58, 34), (74, 55), (58, 76)], "arc"),
                  camera(74, 55, angle=180, mode="free"), cursor(70, 70, "drag"),
                  badge("自由镜头·角色不转身")], title="自由"),
            beat("切回锁定跟随", "相机回贴角色朝向", "跟身", "tps",
                 [hero(50, 48, face=-90), camera(50, 76, angle=-90, mode="lock"),
                  badge("锁定跟随·贴回背后")], title="锁定"),
        ], ["MMO", "动作RPG", "TPS"],
    ))
    c.append(case(
        "ui-map-waypoint", "design-ui", "大地图钉点 / 导航",
        "打开地图点一下设导航点，世界出现路径或箭头；再点可改或清除。",
        [
            beat("打开地图点目标位置", "导航点与路线出现", "往那走", "moba",
                 [box(18, 18, 64, 52), hero(30, 60), circle_ind(65, 35, 8, True),
                  ring(65, 35, r=6), path([(30, 60), (45, 50), (65, 35)], "move"),
                  arrow(50, 48, 62, 38, "move"), cursor(65, 35, "up"),
                  badge("地图钉点")], title="钉点"),
            beat("清除导航", "箭头/路线消失", "取消导航", "moba",
                 [hero(40, 55), badge("清除钉点")], title="清除"),
        ], ["MMO", "ARPG", "开放世界"],
    ))
    c.append(case(
        "ui-hold-vs-toggle", "design-ui", "按住 vs 切换：同一功能两种手感",
        "冲刺、开镜、蹲下可设为按住有效或按一下切换；设置改变手感，UI 要显示当前模式。",
        [
            beat("按住模式：按住才冲刺", "松手立刻停", "按多久走多久", "tps",
                 [hero(40, 55), wasd(["W"]), keyhint(72, 40, "Shift", "active", "按住才跑"),
                  badge("按住")], title="按住"),
            beat("切换模式：按一下开始，再按停止", "状态图标保持", "开关态", "tps",
                 [hero(40, 55), wasd(["W"]), hotbar(dot=0),
                  keyhint(72, 40, "冲", "active", "冲刺ON"), badge("切换")], title="切换"),
        ], ["MMO", "FPS", "TPS", "设计选项"],
    ))
    c.append(case(
        "ui-tutorial-prompt", "design-ui", "教学逼迫输入：提示键才继续",
        "教程高亮某个键或对象，玩家按对了才放行；按错给清晰反馈，不静默吞。",
        [
            beat("屏幕提示“按 F 交互”", "高亮 F，其他键这一步不放行", "只好按它", "tps",
                 [hero(40, 58), prop(64, 48, "门", kind="door", highlight=True),
                  keyhint(64, 28, "F", "active", "交互"), badge("按 F")], title="逼迫提示"),
            beat("按了别的键", "明确告诉你不是这个键，不静默吞掉", "按错了", "tps",
                 [hero(40, 58), prop(64, 48, "门", kind="door", highlight=True),
                  keyhint(64, 28, "F", "active", "交互"), keyhint(30, 28, "E", "off"),
                  deny(30, 46, "不是这个键"), badge("按错反馈")], title="按错"),
            beat("按对键", "提示关闭，教程前进一步", "过了", "tps",
                 [hero(52, 52), prop(64, 48, "门", kind="door"), ring(64, 48, kind="buff"),
                  badge("完成步骤")], title="按对"),
        ], ["全品类", "设计选项"],
    ))
    c.append(case(
        "ui-build-tech-click", "design-ui", "建造 / 科技树点击",
        "在建造菜单点单位或科技：扣资源排队；点已在造的可取消（常需确认）。",
        [
            beat("打开建造/科技面板点一项", "资源扣除，队列出现肖像", "开造", "topdown",
                 [building(28, 50), menu_box(48, 28, ["步兵 50矿", "坦克 150"]),
                  card(72, 48, "步兵", 50), bar(72, 72, 0.35, "cast", "矿物-50"),
                  cursor(58, 36, "up"), badge("入队")], title="点造"),
            beat("取消队列中的项", "资源按规则退回，队列缩短", "不造了", "topdown",
                 [building(40, 50), menu_box(58, 45, ["步兵 x2", "取消一项"]), cursor(70, 60),
                  badge("取消建造")], title="取消"),
        ], ["RTS", "4X", "MMO生活系"],
    ))
    c.append(case(
        "ui-inventory-tetris", "design-ui", "背包格斗：异形体积摆放",
        "物品占多格，要旋转并找到空位才能放进；放不下明确提示，不自动“随便塞”。",
        [
            beat("拖 L 形物品进包", "当前姿态放不下，非法格标红", "塞不进", "moba",
                 [card(45, 40, "L形", 0, True), cursor(45, 40, "drag"),
                  circle_ind(62, 52, 12, False), deny(62, 52, "姿态放不下"),
                  badge("放不下")], title="非法"),
            beat("按 R 旋转再放", "旋转后合法格高亮，松手放入", "转一下就行", "moba",
                 [card(60, 50, "L形", 0, True), cursor(60, 50, "drag"),
                  circle_ind(60, 52, 12, True), keyhint(40, 30, "R", "active", "旋转"),
                  badge("旋转放入")], title="旋转放入"),
        ], ["逃离塔科夫", "ARPG", "设计选项"],
    ))
    c.append(case(
        "ui-spectate-replay", "design-ui", "观战 / 回放：切视角",
        "观战点队友切换相机；回放可暂停、打点、看别人视角。只读，不改对局。",
        [
            beat("观战中点队友头像或数字键", "相机切到该玩家视角", "换人看", "tps",
                 [hero(70, 46), unit(30, 60, team="ally"), camera(70, 74, angle=-90, mode="lock"),
                  partyframe(4, 20, [{"name": "1P", "hp": 0.8},
                                     {"name": "2P", "hp": 0.55},
                                     {"name": "3P", "hp": 1.0}], target=1),
                  cursor(14, 40, "up"), badge("观战切换·跟 2P")], title="切视角"),
            beat("回放中拖时间轴 / 暂停打点", "画面跳到该时刻，可加书签", "倒回去看", "tps",
                 [hero(48, 55), bar(50, 78, 0.4, "cast", "时间轴"), badge("回放打点")], title="回放"),
        ], ["MOBA", "FPS", "MMO"],
    ))

    # ===== 二十三、动态 context 解锁 =====
    c.append(case(
        "dyn-ctx-same-key-swap", "dynamic-context", "同一交互键：旁边是敌人就处决，是果子就拾取吃掉",
        "不换键，只换提示与结果。靠近可处决敌人时，交互提示变成“处决”；"
        "靠近果子/草药时变成“拾取/食用”。松手或按键执行当前高亮那一项。",
        [
            beat("走进可处决敌人背后/虚弱圈", "键位提示从无→「处决」，敌人亮起可终结圈", "能补刀了", "tps",
                 [hero(40, 55, face=20), unit(62, 45, team="enemy"), ring(62, 45, kind="finisher", r=11),
                  badge("提示:处决")], title="解锁处决"),
            beat("按同一交互键", "播放处决，敌人倒下", "处决！", "tps",
                 [hero(50, 50, face=20), unit(60, 45, team="enemy"), arrow(52, 50, 58, 46, "attack"),
                  badge("处决")], title="执行处决"),
            beat("离开处决圈，走到果子旁", "同一键提示改成「拾取/食用」", "现在能摘", "tps",
                 [hero(40, 55), prop(65, 48, "果子", highlight=True),
                  keyhint(65, 32, "F", "active", "拾取/食用"), badge("提示:拾取")], title="换成拾取"),
            beat("再按同一键", "果子进包或当场吃掉回血", "吃了/捡了", "tps",
                 [hero(52, 50), prop(65, 48, "果子"), ring(52, 50, r=10, kind="buff"),
                  keyhint(65, 32, "F", "active", "拾取/食用"), badge("拾取消耗")], title="执行拾取"),
        ], ["动作RPG", "刺客信条", "塞尔达", "MMO"],
    ))
    c.append(case(
        "dyn-ctx-priority-overlap", "dynamic-context", "身边同时好几个可交互：谁抢提示",
        "敌人、尸体、门、果子叠在一起时，屏幕只亮一个主提示。"
        "要有明确优先级（例如处决 > 救人 > 开门 > 拾取），或滚轮/键切换次选。",
        [
            beat("同时进入多个交互圈", "主提示显示最高优先级那项，旁边可有次选", "先干最要紧的", "tps",
                 [hero(38, 58), unit(56, 40, team="enemy"), ring(56, 40, kind="finisher", r=11),
                  prop(74, 52, "门", kind="door"), unit(52, 72, team="ally", state="downed"),
                  prop(30, 42, "果子", kind="item"),
                  keyhint(56, 22, "F", "active", "处决"), badge("优先:处决")], title="多目标重叠"),
            beat("拨切换次选 / 看向另一目标", "主提示换成门/拾取/救人", "换一个", "tps",
                 [hero(38, 58), unit(56, 40, team="enemy"), prop(74, 52, "门", kind="door", highlight=True),
                  unit(52, 72, team="ally", state="downed"), prop(30, 42, "果子", kind="item"),
                  keyhint(74, 26, "F", "active", "开门"), badge("次选:开门")], title="切换次选"),
        ], ["动作RPG", "MMO", "设计选项"],
    ))
    c.append(case(
        "dyn-ctx-angle-stealth", "dynamic-context", "角度 / 潜行才解锁的交互",
        "正面对着敌人只有攻击；绕到背后或潜行条满了，才冒出暗杀/背刺提示。"
        "离开角度提示立刻收回——这是动态解锁，不是常驻技能。",
        [
            beat("正面靠近敌人", "无处决提示，仍是普通攻击", "还不能暗杀掉", "tps",
                 [hero(40, 55, face=20), unit(65, 45, team="enemy", face=200),
                  keyhint(52, 34, "F", "off", "暗杀未解锁"), deny(65, 24, "正面不可暗杀"),
                  badge("无暗杀")], title="正面"),
            beat("绕到背后或进入潜行有效区", "出现「暗杀/背刺」提示", "机会来了", "tps",
                 [hero(70, 48, face=200), unit(58, 45, team="enemy", face=20, highlight=True),
                  ring(58, 45, kind="finisher", r=12), keyhint(58, 26, "F", "active", "暗杀/背刺"),
                  badge("暗杀解锁")], title="背后解锁"),
            beat("离开背后角度", "暗杀提示消失", "窗口没了", "tps",
                 [hero(40, 60, face=10), unit(65, 45, team="enemy"), badge("提示收回")], title="收回"),
        ], ["刺客信条", "动作RPG", "潜行游戏"],
    ))
    c.append(case(
        "dyn-ctx-state-threshold", "dynamic-context", "状态到线才解锁：残血处决 / 破防惩罚",
        "敌人血量或破防条到阈值，交互或专属键才亮；打满之前同一位置只是普通攻击。"
        "像战神/鬼泣处决窗、魂系背刺窗、破刃一闪。",
        [
            beat("敌人满血时靠近", "无处决，只能普通攻击", "还早", "tps",
                 [hero(40, 55), unit(65, 45, team="enemy"), bar(65, 30, 1.0, "hp", "满血"),
                  keyhint(52, 36, "F", "off", "处决未解锁"), badge("无处决窗")], title="未达标"),
            beat("敌人残血/破防", "处决键或提示亮起", "可以收了", "tps",
                 [hero(40, 55), unit(65, 45, team="enemy"), bar(65, 30, 0.18, "hp", "残血 18%"),
                  ring(65, 45, kind="finisher", r=12), keyhint(52, 36, "F", "active", "处决"),
                  badge("处决解锁")], title="达线解锁"),
            beat("窗口内按处决", "播专属处决，否则窗口关闭后恢复普攻", "收掉", "tps",
                 [hero(50, 50), unit(65, 45, team="enemy", size=0.85),
                  ring(65, 45, kind="finisher", r=12), arrow(50, 50, 62, 45, "attack"),
                  impact(65, 45, 16, heavy=True), badge("处决打出")], title="打出"),
        ], ["战神", "动作RPG", "魂like"],
    ))
    c.append(case(
        "dyn-ctx-held-item-verb", "dynamic-context", "手里拿着什么，交互动词跟着变",
        "空手靠近水桶是「提起」；拿着火把靠近火堆是「点燃」；拿着钥匙靠近门是「开锁」。"
        "同一对象，因持有物不同，context 解锁不同动词。",
        [
            beat("空手靠近可搬物", "提示「提起/搬运」", "能搬", "tps",
                 [hero(34, 62), held(), prop(72, 54, "水桶", kind="item", highlight=True),
                  keyhint(48, 28, "F", "active", "提起"), badge("提起")], title="空手"),
            beat("手持火把靠近火盆/墙缝", "提示改成「点燃/烧开」", "能烧", "tps",
                 [hero(34, 62), held("火把"), prop(72, 54, "火盆", kind="item", highlight=True),
                  keyhint(48, 28, "F", "active", "点燃"), badge("点燃")], title="持火"),
            beat("手持钥匙靠近上锁门", "提示「开锁」；无钥匙则「上锁/需要钥匙」", "对上了", "tps",
                 [hero(34, 62), held("钥匙"), prop(72, 54, "上锁的门", kind="door", highlight=True),
                  keyhint(48, 28, "F", "active", "开锁"), badge("开锁")], title="持钥匙"),
        ], ["塞尔达", "浸入式模拟", "动作RPG"],
    ))
    c.append(case(
        "dyn-ctx-env-ledge-cover", "dynamic-context", "环境位姿解锁：翻墙 / 掩体 / 抓檐",
        "贴墙才出「掩体」；到檐下才出「抓举」；到崖边才出「攀爬/跳下」。"
        "离开碰撞体积提示收回，键位可与战斗键复用。",
        [
            beat("走到可翻越矮墙前", "提示「翻越」", "能翻", "tps",
                 [hero(38, 60), prop(62, 48, "矮墙", kind="wall", highlight=True),
                  keyhint(62, 26, "空格", "active", "翻越"), badge("翻越")], title="翻墙"),
            beat("贴入掩体体积", "提示「掩体/探头」；射击改成探头射", "躲好了", "tps",
                 [hero(50, 56), prop(56, 46, "掩体", kind="cover", highlight=True),
                  keyhint(56, 24, "空格", "active", "掩体/探头"),
                  crosshair(78, 40), badge("掩体")], title="掩体"),
            beat("离开有效体积", "环境交互提示消失，键回战斗默认", "没了", "tps",
                 [hero(26, 64), prop(62, 48, "矮墙", kind="wall"), keyhint(62, 26, "空格", "off"),
                  badge("提示收回")], title="离开"),
        ], ["TPS", "刺客信条", "动作RPG"],
    ))
    c.append(case(
        "dyn-ctx-downed-ally", "dynamic-context", "倒地队友解锁救援，否则同键是别的事",
        "平时 F 可能是拾取；队友倒地进圈后 F 变成「救援」读条；救完提示再变回拾取。",
        [
            beat("无倒地者时靠近掉落物", "F=拾取", "捡东西", "tps",
                 [hero(38, 58), prop(64, 48, "掉落物", kind="item", highlight=True),
                  keyhint(64, 30, "F", "active", "拾取"), badge("F:拾取")], title="默认拾取"),
            beat("队友倒地进入救援圈", "F 改「救援」，掉落提示降为次选", "先救人", "tps",
                 [hero(38, 58), unit(58, 50, team="ally", state="downed"), ring(58, 50, kind="buff"),
                  prop(76, 46, "掉落物", kind="item"),
                  keyhint(58, 28, "F", "active", "救援"), badge("F:救援")], title="解锁救援"),
            beat("按住救完", "队友起身；若仍在掉落旁，提示回到拾取", "救起来了", "tps",
                 [hero(40, 58), unit(58, 50, team="ally"), prop(76, 46, "掉落物", kind="item"),
                  bar(48, 34, 1.0, "cast", "救援完成"),
                  keyhint(76, 28, "F", "active", "拾取"), badge("救起→可拾取")], title="救完复原"),
        ], ["TPS", "MMO", "合作动作"],
    ))
    c.append(case(
        "dyn-ctx-vehicle-board", "dynamic-context", "靠近载具解锁「上车」，离开就收回",
        "走到载具交互点才出现上车/上炮位；开车门朝向或满员时提示换成「不可上」原因。",
        [
            beat("进入上车点", "提示「驾驶/炮位/乘客」", "能上车", "tps",
                 [hero(34, 60), vehicle(62, 50, "car"),
                  keyhint(62, 30, "F", "active", "驾驶"), badge("上车")], title="解锁上车"),
            beat("载具远离或满员", "提示消失或变灰并说明原因", "上不了", "tps",
                 [hero(30, 62), vehicle(76, 40, "car", occupied=True),
                  keyhint(76, 20, "F", "off", "满员"), badge("不可上")], title="收回/拒绝"),
        ], ["TPS", "MMO", "开放世界"],
    ))

    # ===== 二十四、自动施法 =====
    c.append(case(
        "auto-cast-toggle-ability", "auto-cast", "技能右键亮绿点：交给自动施法",
        "像魔兽争霸：对技能图标右键打开自动施法（绿点）。"
        "条件满足时系统自己放，再右键关掉。玩家随时可手动抢按。",
        [
            beat("在技能图标上右键", "出现自动施法标记（绿点）", "交给它看着放", "topdown",
                 [unit(45, 55, sel=True), ring(45, 55), hotbar(dot=0), badge("自动施法ON")], title="打开"),
            beat("敌人进入范围且 CD/耗蓝满足", "不按键也放出该技能", "自己放了", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  arrow(42, 54, 66, 42, "attack"), hotbar(dot=0, cd=0), badge("自动放出")], title="自动触发"),
            beat("再右键图标", "绿点灭，恢复纯手动", "我自己来", "topdown",
                 [unit(45, 55, sel=True), hotbar(), badge("自动施法OFF")], title="关闭"),
        ], ["魔兽争霸3", "RTS", "MMO"],
    ))
    c.append(case(
        "auto-cast-when-in-range", "auto-cast", "进距才放：自动施法也要够得着",
        "开了自动仍要满足距离、视线、面向、资源；不够时不偷放，满条件瞬间补放。"
        "和「一开局就乱放」要区分开。",
        [
            beat("自动开着但敌人在射程外", "单位追击或等待，技能不浪费", "先靠近", "topdown",
                 [unit(30, 60, sel=True), ring(30, 60), circle_ind(30, 60, 24, False),
                  unit(80, 30, team="enemy"), hotbar(dot=0), badge("射程外不放")], title="等待"),
            beat("进入射程且面向合法", "自动施法立刻出手", "够着了就放", "topdown",
                 [unit(50, 50, sel=True, face=20), ring(50, 50), circle_ind(50, 50, 24, True),
                  unit(68, 42, team="enemy"), arrow(52, 50, 64, 44, "attack"),
                  hotbar(dot=0, cd=0), badge("进距放出")], title="进距"),
        ], ["魔兽争霸3", "RTS", "MMO"],
    ))
    c.append(case(
        "auto-cast-priority-multi", "auto-cast", "多个自动技能：谁先放",
        "治疗光环、减速、爆发都开了自动时，要有优先级或互斥："
        "例如保命治疗 > 控制 > 填充伤害，避免同一帧抢蓝互殴。",
        [
            beat("多颗技能都开自动，遇战", "按优先级只放当前最该放的", "先救再打", "topdown",
                 [unit(40, 55, sel=True), unit(55, 50, team="ally"), unit(72, 40, team="enemy"),
                  ring(55, 50, kind="buff"), hotbar(active=0, dot=0), badge("优先治疗")], title="优先级"),
            beat("高优先在 CD", "轮到下一优先自动技", "补位放", "topdown",
                 [unit(40, 55, sel=True), unit(72, 40, team="enemy"),
                  arrow(42, 54, 68, 42, "attack"), hotbar(active=1, cd=0, dot=1),
                  badge("次优先")], title="降级"),
        ], ["RTS", "MMO", "设计选项"],
    ))
    c.append(case(
        "auto-cast-pet-ability", "auto-cast", "宝宝 / 召唤物自动技能",
        "宠物技能可单独开自动：自爆、嘲讽、治疗图腾在条件满足时自己放；"
        "关掉则宝宝只普攻或跟随。",
        [
            beat("给宠物技能开自动", "宠物面板现自动标记", "宝宝看着放", "topdown",
                 [hero(35, 60), unit(48, 55, sel=True),
                  hotbar(dot=2), menu_box(62, 40, ["宠技", "自动 ON"], active=1),
                  badge("宠技自动ON")], title="开"),
            beat("条件满足（主人残血/敌人进圈）", "宠物自行放该技", "它放了", "topdown",
                 [hero(35, 60), unit(48, 55), unit(72, 40, team="enemy"),
                  arrow(50, 54, 68, 42, "attack"), hotbar(dot=0, cd=0),
                  badge("宠技自动放出")], title="触发"),
        ], ["魔兽世界", "魔兽争霸3", "ARPG"],
    ))
    c.append(case(
        "auto-cast-assist-repeat", "auto-cast", "按住重复尝试 / 辅助连放",
        "按住技能键时，CD 一好转且目标合法就再放（辅助连发）；"
        "松开停止。不同于永久绿点自动，是「按住期间的自动」。",
        [
            beat("按住技能键不放", "第一次放出后进入等待 CD", "按住蓄着", "moba",
                 [hero(40, 55), circle_ind(70, 40, 12, True), badge("按住")], title="按住"),
            beat("CD 好转且目标仍合法", "不松手自动再放一次", "又放了一发", "moba",
                 [hero(40, 55), arrow(45, 52, 68, 42, "attack"), badge("连放")], title="自动再放"),
            beat("松手", "不再自动尝试", "停", "moba",
                 [hero(40, 55), badge("松手停止")], title="停止"),
        ], ["MMO", "ARPG", "MOBA"],
    ))
    c.append(case(
        "auto-cast-condition-script", "auto-cast", "条件自动：残血才喝药 / 见硬控才开减伤",
        "不是无脑 CD 好了就放，而是挂条件：生命低于 30% 自动用药；"
        "自己被点名点名技能时自动开护盾。要在设置里看得懂条件，误触能关。",
        [
            beat("设置「生命低于三成自动喝药」", "条件显示在自动规则里", "定好规矩", "moba",
                 [hero(48, 55), menu_box(24, 36, ["生命<30% → 用药", "见硬控 → 减伤"]),
                  badge("自动规则")], title="设条件"),
            beat("战斗中掉血过线", "不按键也喝药，物品 CD 走起", "自动一口", "moba",
                 [hero(48, 55), bar(48, 34, 0.25, "hp", "生命 25%"), ring(48, 55, r=12, kind="buff"),
                  hotbar(cd=0, dot=0), badge("自动用药")], title="触发"),
            beat("关闭该自动规则", "掉血不再自动喝", "改回手动", "moba",
                 [hero(48, 55), bar(48, 34, 0.25, "hp", "生命 25%"), hotbar(),
                  menu_box(24, 36, ["生命<30% → 已关"]), badge("规则OFF")], title="关闭"),
        ], ["MMO", "ARPG", "设计选项"],
    ))
    c.append(case(
        "auto-cast-vs-manual-preempt", "auto-cast", "手动抢按：打断即将自动的那一下",
        "自动马上要放时，玩家手动点了另一技能或强制移动：自动应让路，不双放抢资源。"
        "手感上「我按的算数」。",
        [
            beat("自动即将放出时，玩家按了别的技能", "自动取消或延后，先执行手动", "听我的", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  cursor(55, 40, "down"), circle_ind(70, 40, 12, True),
                  arrow(45, 52, 66, 42, "attack"), hotbar(active=0, defer=1, dot=1),
                  badge("手动优先·自动让路")], title="抢占"),
            beat("手动完成且自动条件仍在", "按规则决定是否补一次自动", "再交给自动", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  arrow(42, 54, 66, 42, "attack"), impact(70, 40, 12),
                  hotbar(active=1, cd=1, dot=1), badge("自动补上了")], title="恢复"),
        ], ["RTS", "MMO", "设计选项"],
    ))
    c.append(case(
        "auto-cast-attack-assist", "auto-cast", "自动普攻 vs 技能自动：别混成一种",
        "自动攻击是开关态普攻；技能自动是条件施法。两者可同时开，但 UI 要分开标记，"
        "停手时要分清停的是普攻还是某颗技能。",
        [
            beat("只开自动攻击", "靠近敌人只普攻，不大招", "平A着", "tps",
                 [hero(40, 55, face=20), unit(68, 42, team="enemy"),
                  arrow(45, 52, 64, 44, "attack"), hotbar(),
                  deny(26, 34, "技能不自动"), badge("仅自动普攻")], title="仅普攻"),
            beat("普攻自动 + 某技能自动都开", "普攻填充，技能见缝插入", "两套都在跑", "tps",
                 [hero(40, 55, face=20), unit(68, 42, team="enemy"),
                  arrow(45, 52, 64, 44, "attack"), circle_ind(68, 42, 15, True),
                  hotbar(dot=1, cd=1), badge("普攻+技能自动")], title="并行"),
        ], ["魔兽世界", "MMO", "RTS"],
    ))

    # ===== 二十五、走路：WASD / 摇杆 / 点地 =====
    c.append(case(
        "loco-wasd-camera", "locomotion", "WASD：相对镜头方向走",
        "W 朝镜头前方，A/D 平移，S 后退。转镜头时「前方」跟着变。"
        "魔兽、多数第三人称开放世界的默认走法。",
        [
            beat("按住 W", "角色朝镜头前方移动", "往前走", "tps",
                 [hero(45, 60, face=-90), wasd(["W"]), arrow(45, 55, 45, 35, "move"),
                  badge("W·镜头前")], title="前进"),
            beat("转动镜头再按 W", "前进方向随新镜头改变", "前方换了", "tps",
                 [hero(45, 55, face=20), wasd(["W"]), arrow(48, 52, 68, 40, "move"),
                  badge("镜头变→前方变")], title="转镜后"),
            beat("按 A 或 D", "相对镜头左右平移（侧移）", "横着走", "tps",
                 [hero(45, 55, face=-90), wasd(["A"]), arrow(40, 55, 25, 55, "move"),
                  badge("A/D 侧移")], title="侧移"),
        ], ["魔兽世界", "MMO", "TPS", "开放世界"],
    ))
    c.append(case(
        "loco-wasd-tank", "locomotion", "WASD：车式转向（W 朝角色面朝）",
        "W 始终朝角色自己面对的方向；A/D 转身，不是平移。"
        "老式坦克手感/部分载具；和「相对镜头」是两种设计。",
        [
            beat("按住 W", "沿角色面朝前进", "朝鼻子走", "tps",
                 [hero(45, 55, face=20), wasd(["W"]), arrow(48, 52, 65, 42, "move"),
                  badge("W·面朝")], title="前进"),
            beat("按 A/D", "角色原地转向，不侧移", "拧身子", "tps",
                 [hero(45, 55, face=-40), wasd(["A"]), badge("A/D 转向")], title="转向"),
        ], ["载具", "设计选项", "TPS"],
    ))
    c.append(case(
        "loco-stick-walk", "locomotion", "左摇杆走：推多少走多快",
        "摇杆轻推慢走、推满跑步；方向相对镜头或面朝，由设定决定。"
        "和 WASD 数字键不同，有速度曲线。",
        [
            beat("左摇杆轻推", "角色慢走", "踱步", "tps",
                 [hero(45, 55, face=-90), stick("L", 0, -0.35), arrow(45, 52, 45, 42, "move"),
                  badge("轻推慢走")], title="慢走"),
            beat("左摇杆推满", "角色跑起来", "跑", "tps",
                 [hero(45, 55, face=-90), stick("L", 0, -1), arrow(45, 50, 45, 30, "move"),
                  badge("推满跑")], title="快跑"),
            beat("摇杆回中", "停下（或依惯性滑一小步）", "停", "tps",
                 [hero(45, 55), stick("L", 0, 0), badge("回中")], title="停"),
        ], ["手柄", "TPS", "动作RPG", "双摇杆"],
    ))
    c.append(case(
        "loco-click-move-avatar", "locomotion", "点地走路：只指挥自己（ARPG）",
        "鼠标点地板，自己的角色跑过去；不是 RTS 给一群单位下命令。"
        "可与 WASD 并存，但同时开时要定谁覆盖谁。",
        [
            beat("左键/右键点地面", "角色朝落点跑，脚下出现目标点", "去那儿", "topdown",
                 [hero(30, 60, face=20), arrow(32, 58, 70, 40, "move"),
                  circle_ind(70, 40, 10, True), cursor(70, 40, "up"),
                  badge("点地走")], title="点地"),
            beat("跑动中再点新落点", "改道去新点，旧点取消", "改主意了", "topdown",
                 [hero(45, 50, face=10), arrow(48, 48, 75, 55, "move"),
                  circle_ind(75, 55, 10, True), cursor(75, 55, "up"),
                  badge("改落点")], title="改道"),
        ], ["暗黑", "ARPG", "MOBA补刀走位"],
    ))
    c.append(case(
        "loco-sprint", "locomotion", "冲刺 / 跑步：叠在 WASD 上",
        "WASD 走的同时按冲刺：变快跑，常耗耐力；可按住或切换。"
        "松 WASD 通常停，只按冲刺不给方向则无效或原地踏步。",
        [
            beat("WASD + 按住冲刺", "速度加快，耐力下降", "跑起来", "tps",
                 [hero(40, 55, face=-90), wasd(["W"]), arrow(40, 48, 40, 25, "move"),
                  bar(50, 28, 0.35, "charge", "耐力"), keyhint(72, 72, "Shift", "active"),
                  badge("冲刺中")], title="冲刺"),
            beat("耐力耗尽", "被迫降回走路，冲刺键暂不可用", "喘不上来", "tps",
                 [hero(45, 55, face=-90), wasd(["W"]), arrow(45, 52, 45, 40, "move"),
                  bar(50, 28, 0.0, "charge", "耐力见底"), keyhint(72, 72, "Shift", "off"),
                  deny(72, 52, "冲刺不可用"), badge("耐力空")], title="耗尽"),
        ], ["MMO", "动作RPG", "TPS", "开放世界"],
    ))
    c.append(case(
        "loco-strafe-backpedal", "locomotion", "侧移与后撤：速度可以不一样",
        "A/D 侧移常与前进同速或略慢；S 后撤往往更慢且不能暴击/射击惩罚（看品类）。"
        "这是 WASD 手感的重要调参，不是「四个方向等价」。",
        [
            beat("按住 S 后撤", "背对移动方向慢退，可边退边打", "往后蹭", "tps",
                 [hero(50, 52, face=-90), wasd(["S"]), arrow(50, 56, 50, 74, "move"),
                  unit(50, 22, team="enemy"), arrow(50, 46, 50, 28, "attack"),
                  badge("后撤慢·仍可还击")], title="后撤"),
            beat("按住 A/D 边走边瞄", "侧移保持面朝/准星方向", "拉枪线", "tps",
                 [hero(45, 55, face=0), wasd(["D"]), crosshair(70, 40),
                  arrow(50, 55, 65, 55, "move"), badge("侧移瞄准")], title="侧移"),
        ], ["FPS", "TPS", "MMO", "MOBA"],
    ))
    c.append(case(
        "loco-release-stop", "locomotion", "松键就停 vs 有一点惯性",
        "多数 MMO 松 WASD 立刻停；少数动作/载具会滑行。"
        "手感选择要一眼能懂，不要有时停有时滑却不说明。",
        [
            beat("松开所有方向键（立刻停设定）", "角色马上站住，脚下没有余量", "站定", "tps",
                 [hero(48, 55, face=-90), wasd(), ring(48, 55, r=10, kind="select"),
                  badge("松键·即停")], title="即停"),
            beat("松键但带惯性", "键已松开，角色再滑一小段才停", "滑一下", "tps",
                 [hero(48, 42, face=-90), wasd(), path([(48, 58), (48, 44)], "move"),
                  ring(48, 42, r=10, kind="select"), badge("松键·仍在滑")], title="惯性"),
        ], ["MMO", "动作RPG", "设计选项"],
    ))
    c.append(case(
        "loco-wasd-vs-click", "locomotion", "WASD 与点地同时存在时谁说了算",
        "ARPG/部分 MMO 两套都开：按 WASD 应打断点地跑位；点地应打断当前 WASD 路径意图。"
        "冲突规则要写进手感，不能两人各走各的。",
        [
            beat("点地跑动中按下 WASD", "取消点地目标，改由键盘方向走", "键盘接手", "topdown",
                 [hero(45, 50, face=-90), wasd(["W"]), arrow(45, 45, 45, 28, "move"),
                  badge("WASD 覆盖点地")], title="键优先"),
            beat("WASD 走着时再点地", "改去鼠标落点（或忽略点地，看设定）", "点地接手", "topdown",
                 [hero(40, 55), wasd(["W"]), cursor(72, 40, "up"), arrow(42, 53, 70, 42, "move"),
                  badge("点地覆盖键")], title="点地优先设定"),
        ], ["ARPG", "MMO", "设计选项"],
    ))
    c.append(case(
        "loco-autorun", "locomotion", "自动前进：锁 W",
        "按自动前进后，不按 W 也持续朝前走，再按一次或用后退取消。"
        "长途跑图用，和跟随队友不同。",
        [
            beat("按下自动前进", "角色自己往前走，W 呈锁定态", "甩手跑图", "tps",
                 [hero(45, 55, face=-90), arrow(45, 50, 45, 30, "move"),
                  keyhint(76, 76, "W", "active", "锁定前进"), badge("自动前进ON")], title="锁前进"),
            beat("再按取消或按 S", "自动前进关闭，停下或改手动", "解锁", "tps",
                 [hero(45, 55), keyhint(76, 76, "W", "off"), badge("自动前进OFF")], title="取消"),
        ], ["魔兽世界", "MMO"],
    ))

    # ===== 二十六、平板触控 / 卡牌拖放 =====
    c.append(case(
        "touch-drag-deploy-card", "touch-tablet", "拖拽手牌部署到战场（皇室战争式）",
        "按住手牌卡片拖到可部署区域松手；未进合法区松手则弹回。"
        "费用不够时卡片不可拖出或落点拒绝。",
        [
            beat("手指按住手牌一张卡", "卡提起、原槽位空、费用高亮", "捏住了", "topdown",
                 [card(28, 52, "兵", 3, True), box(22, 72, 12, 14),
                  card(48, 82, "法", 2), card(68, 82, "建筑", 5),
                  bar(50, 90, 0.8, "charge", "圣水"), touch_point(28, 52, "hold"),
                  building(70, 30), unit(30, 40, team="enemy"), badge("按住手牌")], title="按住"),
            beat("拖到己方半场合法格", "落点范围高亮，卡随手指移动", "找落点", "topdown",
                 [card(55, 48, "兵", 3, True), circle_ind(55, 48, 16, True),
                  path([(28, 78), (55, 48)], "move"), touch_point(55, 48, "drag"),
                  badge("拖向战场")], title="拖拽"),
            beat("在合法区松手", "卡消失，单位/建筑在落点生成，费用扣除", "放下！", "topdown",
                 [unit(55, 48, sel=True), ring(55, 48), badge("部署成功")], title="松手部署"),
            beat("拖出界或费用不足松手", "卡弹回手牌，不扣费", "放不成", "topdown",
                 [card(28, 78, "兵", 3), circle_ind(80, 20, 12, False), badge("弹回/拒绝")], title="非法"),
        ], ["皇室战争", "平板", "卡牌RTS"],
    ))
    c.append(case(
        "touch-drag-aim-skill", "touch-tablet", "按住技能键拖出方向/落点再松手",
        "触控上常见：按住技能图标，拖出箭头或圈，松手放出；滑回图标取消。",
        [
            beat("按住触控技能钮", "出现方向/范围指示器", "还没放", "topdown",
                 [hero(40, 55), cone(40, 55, angle=-20, spread=40, length=30),
                  card(22, 78, "技", 0), touch_point(22, 78, "hold"), badge("按住技能")], title="按住"),
            beat("拖向目标方向或落点", "指示器跟随手指", "瞄准中", "topdown",
                 [hero(40, 55), cone(40, 55, angle=10, spread=40, length=34),
                  card(22, 78, "技", 0), touch_point(70, 40, "drag"), badge("拖瞄")], title="拖瞄"),
            beat("松手确认", "技能放出", "放！", "topdown",
                 [hero(40, 55), arrow(42, 52, 70, 40, "attack"),
                  card(22, 78, "技", 0), badge("松手确认")], title="确认"),
            beat("滑回图标松手", "指示器收回，技能不放", "取消", "topdown",
                 [hero(40, 55), card(22, 78, "技", 0), touch_point(22, 78, "tap"),
                  badge("取消")], title="取消"),
        ], ["平板", "MOBA触控", "皇室战争"],
    ))
    c.append(case(
        "touch-tap-select-unit", "touch-tablet", "点触选中单位 / 再点地下令",
        "点自己单位选中，再点地面移动或点敌人攻击；无悬停，靠高亮与二次点选。",
        [
            beat("点触己方单位", "选中圈出现", "选中了", "topdown",
                 [unit(40, 55, sel=True), ring(40, 55), touch_point(40, 55, "tap"), badge("点选")], title="点选"),
            beat("再点地面或敌人", "下达走/打", "下令", "topdown",
                 [unit(40, 55, sel=True), ring(40, 55), unit(72, 40, team="enemy"),
                  arrow(42, 54, 68, 42, "attack"), touch_point(72, 40, "tap"), badge("点目标")], title="点目标"),
        ], ["平板", "RTS触控", "COC式"],
    ))
    c.append(case(
        "touch-pinch-pan", "touch-tablet", "双指缩放地图 / 单指拖地图",
        "双指捏合拉远推近；单指拖平移镜头。与拖卡片手势要分区，避免误触。",
        [
            beat("双指捏合", "地图缩放，单位图标大小变化", "拉远/推近", "topdown",
                 [unit(30, 40, size=1.5), unit(55, 50, size=1.5), unit(70, 35, size=1.5),
                  touch_point(38, 66, "pinch", 62, 78), badge("捏合缩放")], title="缩放"),
            beat("单指拖空白地", "镜头平移", "挪地图", "topdown",
                 [unit(55, 48), unit(70, 42), unit(78, 55), touch_point(35, 70, "drag"),
                  badge("拖地图")], title="平移"),
        ], ["平板", "RTS", "策略"],
    ))
    c.append(case(
        "touch-longpress-info", "touch-tablet", "长按看详情 / 短按执行",
        "短按是主操作；长按弹出属性卡或次级菜单，避免误开。",
        [
            beat("短按单位/卡", "执行默认动作（选中/使用）", "点一下", "topdown",
                 [unit(50, 50, sel=True), touch_point(50, 50, "tap"), badge("短按")], title="短按"),
            beat("长按同一对象", "弹出详情/环形次级菜单", "按久一点", "topdown",
                 [unit(30, 55, sel=True), wheel(62, 48, ["详情", "锁定", "取消", "标记"], active=0),
                  touch_point(30, 55, "hold"), badge("环形菜单")], title="长按"),
        ], ["平板", "设计选项"],
    ))
    c.append(case(
        "touch-card-cycle", "touch-tablet", "滑动切换手牌 / 下一张",
        "手牌区左右滑换焦点卡；或出牌后自动抽到下一张顶牌。",
        [
            beat("在手牌区左右滑", "焦点卡切换，中间放大", "换一张", "topdown",
                 [card(25, 78, "A", 2), card(50, 66, "B", 4), card(75, 78, "C", 3),
                  path([(28, 72), (72, 72)], "move"), touch_point(62, 72, "drag"),
                  badge("滑动手牌")], title="滑动"),
            beat("打出焦点卡", "空位从牌库自动补进下一张", "又摸一张", "topdown",
                 [unit(55, 45, sel=True), ring(55, 45),
                  card(25, 78, "A", 2), card(50, 78, "D", 1), card(75, 78, "C", 3),
                  badge("自动补牌")], title="补牌"),
        ], ["卡牌", "平板", "皇室战争"],
    ))

    # ===== 二十七、选单式指令 =====
    c.append(case(
        "menu-rotk-layered", "menu-cmd", "分层选单：先选武将，再选指令，再选目标（三国志式）",
        "不是实时 WASD，而是回合/半即时里点人物 → 菜单出「移动/攻击/计策」→ 再点格子或对象。"
        "每层可取消返回上一层。",
        [
            beat("点选己方武将", "脚下光标；弹出指令菜单", "叫他做事", "topdown",
                 [unit(40, 55, sel=True), ring(40, 55),
                  menu_box(58, 42, ["移动", "攻击", "计策", "待命"]), badge("指令菜单")], title="点武将"),
            beat("点「攻击」", "菜单收起，可攻击格/对象高亮", "选怎么打", "topdown",
                 [unit(40, 55, sel=True), ring(40, 55), unit(70, 40, team="enemy"),
                  circle_ind(55, 48, 10, True), circle_ind(62, 44, 10, True),
                  circle_ind(70, 40, 14, True), cursor(58, 48), badge("攻击范围")], title="选指令"),
            beat("点敌方或格子确认", "指令提交，进入演出/结算", "打他", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  arrow(44, 52, 66, 42, "attack"), badge("确认目标")], title="选目标"),
            beat("按取消或点空白", "回到上一层菜单或待机", "算了", "topdown",
                 [unit(40, 55, sel=True), menu_box(58, 42, ["移动", "攻击", "计策"]), badge("返回上层")], title="取消"),
        ], ["三国志", "战棋", "回合策略"],
    ))
    c.append(case(
        "menu-grid-command-panel", "menu-cmd", "底部/侧栏技能格点选",
        "点命令面板上一格技能，再按该技能的目标规则点地/点人。"
        "适合主机键位少或平板，和键位热键并行。",
        [
            beat("点面板上的技能格", "进入该技能瞄准或立即释放", "点了技能", "moba",
                 [hero(40, 55), hotbar(active=0), badge("点技能格")], title="点格"),
            beat("再点目标/地面（若需要）", "技能放出", "放完", "moba",
                 [hero(40, 55), circle_ind(68, 40, 14, True), cursor(68, 40),
                  hotbar(cd=0), badge("补目标")], title="补目标"),
        ], ["MOBA", "平板", "RTS面板"],
    ))
    c.append(case(
        "menu-confirm-queue", "menu-cmd", "菜单确认后才入队（防误触）",
        "选完指令与目标后，还要按「执行」才提交；或弹出确认条。"
        "战棋/经营常见，和 RTS 右键即下令相反。",
        [
            beat("选好指令与目标", "出现确认条：执行 / 取消", "再问一次", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  menu_box(55, 70, ["执行", "取消"]), badge("确认条")], title="确认"),
            beat("点执行", "指令入队并播放", "定了", "topdown",
                 [unit(40, 55, sel=True), unit(70, 40, team="enemy"),
                  arrow(44, 52, 66, 42, "attack"), badge("执行")], title="执行"),
        ], ["三国志", "战棋", "经营"],
    ))

    # ===== 二十八、放不了 =====
    c.append(case(
        "block-resource", "blocked", "蓝 / 怒气 / 弹药不足",
        "按下后明确拒绝，并提示缺什么。",
        [
            beat("资源不够时按技能", "图标闪红/飘字「魔法不足」，不进瞄准", "放不出", "moba",
                 [hero(50, 55), hotbar(deny=0), bar(50, 34, 0.15, "cast", "蓝量不足"),
                  deny(72, 42, "魔法不足"), badge("资源不足")], title="缺资源"),
            beat("回蓝攒够后再按", "图标恢复亮起，正常进入施法", "缓过来了", "moba",
                 [hero(50, 55), hotbar(active=0), bar(50, 34, 0.85, "cast", "蓝量"),
                  badge("可施放")], title="恢复"),
        ], ["全品类"],
    ))
    c.append(case(
        "block-cooldown", "blocked", "冷却中",
        "CD 转圈，按下有拒绝音效/闪烁。",
        [
            beat("CD 未好时按技能", "图标转着遮罩拒绝施放，可有拒绝音效", "再等等", "moba",
                 [hero(50, 55), hotbar(cd=0, deny=0), deny(72, 42, "冷却中"),
                  badge("冷却中")], title="CD中"),
            beat("CD 转完", "图标亮起可再次施放", "好了", "moba",
                 [hero(50, 55), hotbar(active=0), badge("冷却完毕")], title="转好"),
        ], ["全品类"],
    ))
    c.append(case(
        "block-fog-illegal", "blocked", "雾中 / 类型不对",
        "看不见或对象非法时准星禁止态。",
        [
            beat("对雾里看不见的目标确认", "禁止图标，技能不出去", "不行", "moba",
                 [hero(35, 60), unit(72, 38, team="enemy"), fog(58, 0, 42, 100),
                  circle_ind(70, 40, 14, False), crosshair(70, 40, locked=True),
                  hotbar(deny=0), deny(70, 62, "看不见"), badge("雾里·禁止")], title="禁止"),
        ], ["RTS", "MOBA"],
    ))
    c.append(case(
        "block-bag-full", "blocked", "背包满 / 负担不够",
        "拾取或购买时包满：明确说满了，东西留在地上或交易取消，不静默吞。",
        [
            beat("包满时拾取或购买", "红字「背包已满」，物品留在原地不进包", "拿不下", "moba",
                 [hero(40, 58), prop(66, 48, "掉落物", kind="item"),
                  menu_box(22, 62, ["背包已满！"]), deny(66, 66, "包满"),
                  badge("拒绝拾取")], title="包满"),
            beat("清出格子再捡", "物品正常进包，地上消失", "腾出手了", "moba",
                 [hero(52, 50), card(62, 38, "拾得", 0), menu_box(22, 62, ["背包 +1"]),
                  badge("拾取成功")], title="再捡"),
        ], ["MMO", "ARPG"],
    ))
    c.append(case(
        "block-range-los-trade", "blocked", "超距 / 遮挡 / 交易条件不满足",
        "技能超距、没视线、交易一方移动过远：取消并说明原因。",
        [
            beat("超距或没视线时确认", "拒绝并提示原因", "够不着", "moba",
                 [hero(30, 60), unit(80, 30, team="enemy"), circle_ind(30, 60, 26, False),
                  building(52, 42), path([(34, 56), (50, 46)], "move"),
                  crosshair(80, 30, locked=True), hotbar(deny=0), deny(56, 62, "超距/被挡"),
                  badge("超距/视线")], title="超距"),
            beat("交易中走太远", "交易窗关闭，物品回各方", "交易取消", "moba",
                 [hero(20, 60), unit(80, 30, team="ally"), card(26, 44, "我的货"), card(74, 44, "他的货"),
                  menu_box(40, 55, ["交易已取消"]), badge("交易取消")], title="交易断"),
        ], ["MMO", "MOBA", "RTS"],
    ))
    c.append(case(
        "block-crowd-control", "blocked", "被控：沉默 / 晕眩 / 变形",
        "被控时按键明确无效，状态图标说清是哪种控制，控结束才恢复。",
        [
            beat("被晕/沉默时按技能", "整栏变灰拒绝，头顶控制图标闪烁说明控制类型", "动不了", "moba",
                 [hero(48, 55), hotbar(off=[0, 1, 2, 3]), deny(48, 32, "沉默·技能全封"),
                  arrow(44, 58, 28, 64, "move"), badge("沉默中·还能走")], title="被控"),
            beat("控制时间结束", "技能栏恢复亮起，立刻能反打", "缓过来了", "moba",
                 [hero(48, 55), hotbar(active=0), badge("控制解除")], title="解控"),
        ], ["MMO", "MOBA"],
    ))

    # ===== 二十八、联机：进一局并待在里面 =====
    c.append(case(
        "net-create-join-room", "netplay", "建房 / 输房号加入",
        "自己开一间房等人，或者拿到房号点进别人的房。进错房号要明确说进不去，"
        "不能卡在转圈里让人猜。",
        [
            beat("点「创建房间」", "房间开出来，我是房主，名单只有我", "先占个坑", "moba",
                 [roster(18, 24, [{"name": "我（房主）", "state": "waiting"}], "房间 4821"),
                  cursor(30, 60, "up"), badge("建好房了")], title="建房"),
            beat("朋友输房号点加入", "名单多出一行，房主看得到谁进来了", "有人来了", "moba",
                 [roster(18, 24, [{"name": "我（房主）", "state": "waiting"},
                                  {"name": "阿强", "state": "waiting"}], "房间 4821"),
                  netstat(72, 30, 38, "ok"), badge("2/4 人")], title="有人进来"),
            beat("输错房号或房间已满", "明确告诉你进不去和为什么，退回房号输入", "白输了", "moba",
                 [menu_box(20, 30, ["房号 9999", "查无此房"]), deny(64, 45, "进不去"),
                  cursor(34, 62, "up"), badge("加入失败")], title="加入失败"),
        ], ["联机对局", "开黑组队"],
    ))
    c.append(case(
        "net-lobby-ready", "netplay", "大厅准备：全绿房主才能开",
        "每个人自己点准备，名单上变成勾；有人还没准备，开始按钮点不动。"
        "这是开局前最后一道「大家都跟上了吗」。",
        [
            beat("我点准备", "我这行变成勾，其他人还是等待", "我好了", "moba",
                 [roster(16, 20, [{"name": "我", "state": "ready"},
                                  {"name": "阿强", "state": "waiting"},
                                  {"name": "小美", "state": "waiting"}], "准备中"),
                  cursor(30, 70, "up"), badge("我已准备")], title="我准备"),
            beat("有人没准备就点开始", "开始按钮按不动，并指出还差谁", "开不了", "moba",
                 [roster(16, 20, [{"name": "我", "state": "ready"},
                                  {"name": "阿强", "state": "ready"},
                                  {"name": "小美", "state": "waiting"}], "准备中"),
                  deny(68, 52, "小美还没准备"), badge("开始被挡住")], title="还差人"),
            beat("最后一人也准备了", "名单全绿，开始按钮亮起，进开局倒计时", "走起", "moba",
                 [roster(16, 20, [{"name": "我", "state": "ready"},
                                  {"name": "阿强", "state": "ready"},
                                  {"name": "小美", "state": "ready"}], "全员就绪"),
                  bar(70, 40, 0.6, "cast", "开局 3s"), badge("全绿·可开")], title="全绿开局"),
        ], ["联机对局", "竞技匹配"],
    ))
    c.append(case(
        "net-matchmaking-queue", "netplay", "匹配排队：等人、接受、有人跑了",
        "点开始匹配就进队列，能看到自己等了多久、排在什么位置；"
        "配到人要在限时内点接受，有人不点就散伙重新排。",
        [
            beat("点开始匹配", "进入队列，显示已等时间和队列位置", "开始等", "moba",
                 [menu_box(18, 22, ["匹配中…", "已等 0:42"]), queue_no(52, 40, 3, "waiting"),
                  netstat(74, 30, 42, "ok"), badge("排队中")], title="进队列"),
            beat("配到人了，限时接受", "弹出接受窗与倒计时，谁接了谁亮", "赶紧点", "moba",
                 [roster(16, 20, [{"name": "我", "state": "ready"},
                                  {"name": "阿强", "state": "ready"},
                                  {"name": "路人", "state": "waiting"}], "等待接受"),
                  bar(70, 40, 0.35, "cast", "接受 7s"), cursor(34, 72, "up"),
                  badge("接受对局")], title="限时接受"),
            beat("有人没点接受", "这局散掉，明确说是谁没接，重新回队列", "又得等", "moba",
                 [roster(16, 20, [{"name": "我", "state": "ready"},
                                  {"name": "阿强", "state": "ready"},
                                  {"name": "路人", "state": "offline"}], "有人未接受"),
                  deny(70, 50, "路人未接受"), queue_no(52, 74, 3, "waiting"),
                  badge("回到队列")], title="有人跑了"),
        ], ["联机对局", "竞技匹配"],
    ))
    c.append(case(
        "net-disconnect-reconnect", "netplay", "我掉线了：还能回来吗",
        "网断了先明确告诉我「在重连」和还剩多久，别让我以为游戏卡死；"
        "重连成功回到原来的位置，超时才算退出。",
        [
            beat("网络中断", "画面明确进入重连态，显示剩余重连时间", "别慌", "moba",
                 [hero(48, 55), netstat(72, 28, 999, "lost"),
                  bar(48, 34, 0.8, "cast", "重连 24s"), badge("正在重连")], title="断了"),
            beat("重连成功", "回到原来位置继续打，延迟恢复正常", "接上了", "moba",
                 [hero(48, 55), ring(48, 55, r=12, kind="buff"), netstat(72, 28, 45, "ok"),
                  badge("已回到对局")], title="接回来"),
            beat("重连超时", "明确判定退出这局，并说明后果（惩罚/可再进）", "回不去了", "moba",
                 [netstat(72, 28, 999, "lost"), menu_box(24, 34, ["重连超时", "已退出本局"]),
                  deny(52, 62, "本局结束"), badge("掉出对局")], title="超时退出"),
        ], ["联机对局", "竞技匹配"],
    ))
    c.append(case(
        "net-teammate-drop-ai", "netplay", "队友掉线：交给托管还是空着",
        "队友断线时我得一眼看出「他不是在挂机，是掉了」；他的角色是留在原地、"
        "被 AI 托管、还是直接消失，规则要写清楚。",
        [
            beat("队友断线", "他的名字变灰并标掉线，角色留在原地不动", "他掉了", "topdown",
                 [unit(35, 55, sel=True), ring(35, 55), unit(60, 48, team="ally"),
                  playertag(60, 34, "阿强", "p2"), netstat(74, 26, 999, "lost"),
                  roster(14, 60, [{"name": "阿强", "state": "offline"}], "队伍"),
                  badge("队友掉线")], title="队友掉了"),
            beat("交给 AI 托管", "他的角色开始自动跟着打，标记写明这是托管", "先顶着", "topdown",
                 [unit(35, 55, sel=True), ring(35, 55), unit(58, 46, team="ally"),
                  playertag(58, 32, "AI托管", "p3"), unit(78, 38, team="enemy"),
                  arrow(60, 45, 74, 40, "attack"), badge("AI 接手")], title="AI 托管"),
            beat("他重连回来", "托管标记撤掉，控制权交还给他本人", "人回来了", "topdown",
                 [unit(35, 55, sel=True), ring(35, 55), unit(58, 46, team="ally"),
                  playertag(58, 32, "阿强", "p2"), netstat(74, 26, 48, "ok"),
                  badge("交还控制")], title="交还"),
        ], ["联机对局", "开黑组队"],
    ))
    c.append(case(
        "net-lag-rollback", "netplay", "延迟卡了：我按的那一下被拉回去",
        "网络卡的时候我按了技能、走了两步，然后被服务器拉回原地。"
        "这件事必须让玩家看懂是网络问题，而不是「游戏吞了我的操作」。",
        [
            beat("延迟升高时按技能", "本地先演出来，服务器还没确认", "先动起来", "moba",
                 [hero(40, 58), netstat(72, 26, 260, "lag"), circle_ind(64, 44, 14, True),
                  hotbar(active=0), badge("本地先放")], title="本地先动"),
            beat("服务器不认，拉回原位", "角色被拉回按之前的位置，技能退回可用", "被拽回来了", "moba",
                 [hero(30, 62), path([(40, 58), (30, 62)], "move"), netstat(72, 26, 260, "lag"),
                  deny(52, 44, "服务器未确认"), hotbar(active=0), badge("回滚")], title="被拉回"),
            beat("延迟恢复", "动作正常生效，不再拉扯", "顺了", "moba",
                 [hero(44, 55), netstat(72, 26, 46, "ok"), unit(70, 42, team="enemy"),
                  arrow(48, 54, 66, 44, "attack"), impact(70, 42, 13), hotbar(cd=0),
                  badge("恢复正常")], title="恢复"),
        ], ["联机对局", "竞技匹配"],
    ))
    c.append(case(
        "net-push-to-talk", "netplay", "按住说话 / 麦克风开关",
        "按住一个键才说话，松开就闭麦；也可以切成常开。"
        "关键是我随时知道自己是不是在外放，以及现在谁在说。",
        [
            beat("默认闭麦", "麦克风图标是关着的，我说话别人听不到", "先静音", "moba",
                 [hero(40, 58), voice(74, 30, "off"), keyhint(24, 80, "V", "idle", "按住说话"),
                  badge("闭麦")], title="闭麦"),
            beat("按住说话键", "图标变成正在说话，队友那边看到是我在说", "我说两句", "moba",
                 [hero(40, 58), voice(74, 30, "talking"), keyhint(24, 80, "V", "active", "按住说话"),
                  roster(14, 58, [{"name": "我（说话中）", "state": "ready"}], "语音"),
                  badge("正在说话")], title="按住说"),
            beat("切成常开", "松手也一直开着，图标保持开启提醒我别乱说", "一直开着", "moba",
                 [hero(40, 58), voice(74, 30, "on"), keyhint(24, 80, "V", "active", "常开"),
                  badge("麦克风常开")], title="常开"),
        ], ["联机对局", "开黑组队"],
    ))
    c.append(case(
        "net-surrender-vote", "netplay", "发起投降，队伍表决",
        "一个人想投降不算，要凑够票数。发起后大家看到票数进度，"
        "没凑够就继续打，并且要说明多久之后才能再发起。",
        [
            beat("我发起投降", "弹出表决，票数从我这一票开始", "打不过了", "moba",
                 [hero(40, 58), vote(20, 34, 1, 4, "投降表决"),
                  cursor(30, 70, "up"), badge("已发起")], title="发起"),
            beat("队友陆续投票", "票数进度往前走，谁投了谁亮", "看队友", "moba",
                 [hero(40, 58), vote(20, 34, 3, 4, "投降表决"),
                  roster(14, 58, [{"name": "阿强", "state": "ready"},
                                  {"name": "小美", "state": "waiting"}], "投票"),
                  badge("3/4 票")], title="投票中"),
            beat("票数不够，表决失败", "继续打，并明确说多久后才能再发起", "接着打", "moba",
                 [hero(40, 58), vote(20, 34, 3, 4, "表决失败"),
                  deny(66, 52, "3分钟后可再发起"), badge("没凑够")], title="表决失败"),
        ], ["联机对局", "竞技匹配"],
    ))
    c.append(case(
        "net-report-mute", "netplay", "屏蔽某个玩家 / 举报",
        "对着某个人做处理：先能立刻屏蔽让他不再打扰我（当场生效），"
        "再决定要不要举报。两件事要分开，别把「静音」藏进举报流程里。",
        [
            beat("在名单上选那个人", "弹出对他的处理项：屏蔽、举报、看资料", "就是他", "moba",
                 [roster(14, 20, [{"name": "路人甲", "state": "ready"}], "队伍"),
                  menu_box(52, 30, ["屏蔽", "举报", "看资料"], active=0),
                  cursor(58, 55, "up"), badge("选人处理")], title="选人"),
            beat("点屏蔽", "他的语音与文字当场消失，图标标明已屏蔽", "清静了", "moba",
                 [roster(14, 20, [{"name": "路人甲（已屏蔽）", "state": "offline"}], "队伍"),
                  voice(70, 34, "off"), badge("已屏蔽·立刻生效")], title="屏蔽"),
            beat("点举报并选原因", "提交后给回执，明确说不会当场处理", "交上去了", "moba",
                 [menu_box(20, 26, ["消极比赛", "言语辱骂", "作弊"], active=1),
                  menu_box(58, 44, ["举报已提交", "会另行处理"]), cursor(30, 58, "up"),
                  badge("举报回执")], title="举报"),
        ], ["联机对局", "开黑组队"],
    ))
    c.append(case(
        "net-crossplay-prompt-kbm", "netplay", "跨平台同队：我这边按键盘提示",
        "同一局里我用键鼠、队友用手柄。提示必须按各自设备显示，"
        "不能让手柄玩家看到「按 F」，也不能让我看到「按 A 键」。",
        [
            beat("队友用手柄，我用键鼠", "名单上标出各自设备，我这边提示键盘键", "各按各的", "tps",
                 [hero(38, 58), unit(62, 50, team="ally"), playertag(62, 34, "手柄队友", "p2"),
                  keyhint(38, 34, "F", "active", "复活队友"),
                  roster(12, 62, [{"name": "我（键鼠）", "state": "ready"},
                                  {"name": "阿强（手柄）", "state": "ready"}], "跨平台"),
                  badge("键鼠提示")], title="我这边"),
        ], ["联机对局", "开黑组队"], cross_device=True,
    ))
    c.append(case(
        "net-crossplay-prompt-pad", "netplay", "跨平台同队：我这边按手柄提示",
        "同一件事在手柄那边显示的是面键，不是键盘字母。"
        "同一句「救他」，两边看到的按钮不一样才算做对。",
        [
            beat("我用手柄，队友用键鼠", "名单上标出各自设备，我这边提示手柄面键", "各按各的", "tps",
                 [hero(38, 58), unit(62, 50, team="ally"), playertag(62, 34, "键鼠队友", "p2"),
                  keyhint(38, 34, "A键", "active", "复活队友"),
                  roster(12, 62, [{"name": "我（手柄）", "state": "ready"},
                                  {"name": "小美（键鼠）", "state": "ready"}], "跨平台"),
                  badge("手柄提示")], title="我这边"),
        ], ["联机对局", "开黑组队"], cross_device=True,
    ))

    # ===== 二十九、同屏多人：加入、分屏、抢东西 =====
    c.append(case(
        "couch-pad-join", "couch-play", "第二个手柄按一下就进来",
        "不用回主菜单：拿起另一个手柄按确认键，P2 当场出现在场上。"
        "空槽位要一直提示「按键加入」，让人知道还能再来人。",
        [
            beat("只有我在玩，旁边有空槽", "空槽位提示按键加入", "还能再来人", "tps",
                 [hero(40, 58), playertag(40, 36, "P1", "p1"),
                  padslot(["joined", "waiting"]), badge("等人加入")], title="等人"),
            beat("旁边的人拿起手柄按确认", "P2 当场出现，槽位变成已加入", "他进来了", "tps",
                 [hero(34, 58), playertag(34, 36, "P1", "p1"),
                  unit(58, 55, team="ally"), playertag(58, 34, "P2", "p2"),
                  padslot(["joined", "joined"]), badge("P2 加入")], title="加入"),
        ], ["同屏双人", "派对游戏"],
    ))
    c.append(case(
        "couch-pad-drop", "couch-play", "手柄没电掉出去了",
        "手柄断开要立刻暂停并说清是谁的手柄掉了，别让另一个人在那儿硬撑；"
        "重新连上应该接回原来那个角色，而不是变成新玩家。",
        [
            beat("P2 手柄断开", "游戏暂停，明确说是 P2 的手柄断了", "先停一下", "tps",
                 [hero(34, 58), playertag(34, 36, "P1", "p1"),
                  unit(58, 55, team="ally"), playertag(58, 34, "P2 断开", "p2"),
                  padslot(["joined", "lost"]), deny(58, 74, "P2 手柄断开"),
                  badge("已暂停")], title="断开暂停"),
            beat("重新连上并按键", "接回原来那个角色，不是新开一个玩家", "还是他", "tps",
                 [hero(34, 58), playertag(34, 36, "P1", "p1"),
                  unit(58, 55, team="ally"), playertag(58, 34, "P2", "p2"),
                  padslot(["joined", "joined"]), badge("接回原角色")], title="接回来"),
        ], ["同屏双人", "派对游戏"],
    ))
    c.append(case(
        "couch-split-mode", "couch-play", "分屏怎么切：左右分、上下分、还是共享一块屏",
        "两个人一台机器，画面怎么分是手感问题：左右分适合看远，"
        "上下分适合看宽，共享一块屏最省但会互相拉扯。切换要当场看到效果。",
        [
            beat("默认左右分屏", "屏幕竖着切两半，各自占一边", "各看各的", "tps",
                 [splitscreen("v"), hero(24, 55), playertag(24, 34, "P1", "p1"),
                  unit(74, 55, team="ally"), playertag(74, 34, "P2", "p2"),
                  badge("左右分")], title="左右分"),
            beat("切成上下分屏", "屏幕横着切两半，视野变宽变矮", "换个分法", "tps",
                 [splitscreen("h"), hero(30, 30), playertag(30, 16, "P1", "p1"),
                  unit(30, 78, team="ally"), playertag(30, 64, "P2", "p2"),
                  badge("上下分")], title="上下分"),
            beat("切成共享一块屏", "不再分屏，两人挤同一个镜头，离太远会被拉住", "凑一起", "tps",
                 [splitscreen("shared"), hero(38, 58), playertag(38, 38, "P1", "p1"),
                  unit(62, 52, team="ally"), playertag(62, 34, "P2", "p2"),
                  camera(50, 78, angle=-90, mode="lock"), badge("共享单屏")], title="共享"),
        ], ["同屏双人", "分屏"],
    ))
    c.append(case(
        "couch-shared-camera-tether", "couch-play", "共享一块屏：走太远会被镜头拉住",
        "两个人挤一个镜头时，谁想跑远都会被拽住：先是镜头拉远，"
        "再是走到屏幕边推不动。这个限制必须让玩家看懂，不然只会觉得卡住了。",
        [
            beat("两人靠在一起", "镜头贴得近，画面细节看得清", "挨着走", "tps",
                 [camera(50, 80, angle=-90, mode="lock"), hero(44, 55),
                  playertag(44, 36, "P1", "p1"), unit(56, 55, team="ally"),
                  playertag(56, 36, "P2", "p2"), badge("镜头贴近")], title="挨着"),
            beat("一个人往外跑", "镜头自动拉远，把两人都收进画面", "拉远了", "tps",
                 [camera(50, 84, angle=-90, mode="lock"), hero(24, 58),
                  playertag(24, 40, "P1", "p1"), unit(76, 50, team="ally"),
                  playertag(76, 32, "P2", "p2"), arrow(72, 52, 84, 48, "move"),
                  badge("镜头拉远")], title="拉远"),
            beat("再往外就到屏幕边了", "人贴住画面边缘走不动，明确提示是被同屏拉住", "推不动", "tps",
                 [camera(50, 84, angle=-90, mode="lock"), hero(20, 58),
                  playertag(20, 40, "P1", "p1"), unit(90, 50, team="ally"),
                  playertag(90, 30, "P2", "p2"), deny(90, 70, "同屏边界"),
                  badge("被拉住")], title="到边界"),
        ], ["同屏双人", "分屏"],
    ))
    c.append(case(
        "couch-loot-race", "couch-play", "同屏抢同一个东西：谁先按谁拿",
        "两个人同时按同一个箱子，只能有一个人拿到。"
        "没拿到的那个必须收到明确反馈「被 P1 拿走了」，而不是按了没反应。",
        [
            beat("两人同时靠近同一个箱子", "两边都亮起拾取提示", "都想要", "tps",
                 [prop(50, 46, "宝箱", kind="chest", highlight=True),
                  hero(30, 60), playertag(30, 40, "P1", "p1"),
                  unit(70, 58, team="ally"), playertag(70, 38, "P2", "p2"),
                  keyhint(50, 26, "A键", "active", "拾取"), badge("两边都能按")], title="都能按"),
            beat("P1 先按到", "箱子归 P1，他这边入包", "我抢到了", "tps",
                 [hero(38, 56), playertag(38, 36, "P1", "p1"), card(24, 76, "战利品", 0),
                  unit(70, 58, team="ally"), playertag(70, 38, "P2", "p2"),
                  queue_no(38, 72, 1, "done"), badge("P1 拿到")], title="P1 拿到"),
            beat("P2 慢了一步", "他这边明确说被 P1 拿走了，提示收回", "手慢了", "tps",
                 [hero(38, 56), playertag(38, 36, "P1", "p1"),
                  unit(70, 58, team="ally"), playertag(70, 38, "P2", "p2"),
                  deny(70, 74, "被 P1 拿走"), keyhint(70, 24, "A键", "off"),
                  badge("P2 没拿到")], title="P2 落空"),
        ], ["同屏双人", "派对游戏"],
    ))
    c.append(case(
        "couch-menu-owner", "couch-play", "同屏开一个菜单：谁在操作它",
        "两人一台机器时最容易吵架的地方：背包只有一个，谁的手柄在动它？"
        "要么各自一个光标，要么明确标出「现在是 P2 在操作」，不能默认听 P1。",
        [
            beat("P2 打开共用菜单", "菜单标出当前操作者是 P2，P1 的输入不动它", "他在翻", "moba",
                 [menu_box(30, 26, ["装备", "道具", "退出"], active=1),
                  playertag(30, 18, "P2 操作中", "p2"),
                  hero(20, 70), playertag(20, 56, "P1", "p1"),
                  padslot(["joined", "joined"]), badge("P2 在操作")], title="P2 操作"),
            beat("P1 也想动这个菜单", "要么各给一个光标，要么明确挡下并说清归谁", "别抢", "moba",
                 [menu_box(30, 26, ["装备", "道具", "退出"], active=1),
                  playertag(30, 18, "P2 操作中", "p2"),
                  deny(70, 44, "菜单归 P2"), hero(20, 70),
                  playertag(20, 56, "P1", "p1"), badge("P1 被挡下")], title="谁说了算"),
        ], ["同屏双人", "派对游戏"],
    ))
    c.append(case(
        "couch-mixed-devices-pad", "couch-play", "一台机器两种设备：我用手柄",
        "同一台机器上 P1 用键鼠、P2 用手柄，各自的提示要按自己的设备显示，"
        "而且键盘的输入不能串到手柄玩家身上。",
        [
            beat("我用手柄操作我的角色", "我这边提示手柄面键，键鼠的输入不动我", "各归各", "tps",
                 [hero(60, 55), playertag(60, 34, "P2 手柄", "p2"),
                  unit(26, 58, team="ally"), playertag(26, 38, "P1 键鼠", "p1"),
                  stick("L", 0, -0.7), keyhint(60, 20, "A键", "active", "交互"),
                  padslot(["joined", "joined"]), badge("手柄侧")], title="手柄侧"),
        ], ["同屏双人", "分屏"], cross_device=True,
    ))
    c.append(case(
        "couch-mixed-devices-kbm", "couch-play", "一台机器两种设备：我用键鼠",
        "同一台机器上的另一半：我用键鼠，提示是键盘键；"
        "手柄玩家的摇杆输入不会把我的角色带跑。",
        [
            beat("我用键鼠操作我的角色", "我这边提示键盘键，手柄的输入不动我", "各归各", "tps",
                 [hero(26, 58), playertag(26, 38, "P1 键鼠", "p1"),
                  unit(60, 55, team="ally"), playertag(60, 34, "P2 手柄", "p2"),
                  wasd(["W"]), keyhint(26, 20, "F", "active", "交互"),
                  padslot(["joined", "joined"]), badge("键鼠侧")], title="键鼠侧"),
        ], ["同屏双人", "分屏"], cross_device=True,
    ))

    return enrich_all(c)


def _git_head() -> str:
    try:
        return subprocess.check_output(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=str(OUT.parents[2]),
            text=True,
        ).strip()
    except Exception:
        return "unknown"


_HOTBAR_WORDS = ("技能栏", "快捷栏", "命令卡", "技能格", "按技能", "技能键", "技能图标",
                 "图标亮", "图标组", "栏位", "键位提示", "多出", "新键")
_HOTBAR_CD_WORDS = ("冷却", "CD", "转 CD", "进入物品 CD")
_HOTBAR_DENY_WORDS = ("拒绝", "闪红", "禁止", "无效", "放不出", "不可用")
_HOTBAR_GONE_WORDS = ("消失", "收回", "复原", "灰掉", "回到原位", "切回原")
_HOTBAR_DOT_WORDS = ("绿点", "自动施法标记", "自动标记")
_BAR_WORDS = ("读条", "蓄力", "引导", "倒计时")
_BAR_BROKEN_WORDS = ("打断", "取消", "中断", "失败")
_KEY_WORDS = ("交互键", "按 F", "按F", "交互提示", "按交互", "提示键", "键位提示")


def _anchor(castl):
    for el in castl:
        if el.get("t") == "hero":
            return el["x"], el["y"]
    for el in castl:
        if el.get("t") == "unit" and el.get("sel"):
            return el["x"], el["y"]
    return 50, 55


def augment_ui_glyphs(cases: list) -> dict:
    """Deterministic pass: when beat text names a UI element the cast does not
    draw, add the matching glyph. Explicit rules, no silent guessing beyond them."""
    added = {"hotbar": 0, "bar": 0, "key": 0}
    for c in cases:
        for b in c["beats"]:
            castl = b["cast"]
            kinds = {el.get("t") for el in castl}
            text = f"{b['input']}{b['screen']}"
            ax, ay = _anchor(castl)
            if "hotbar" not in kinds and any(w in text for w in _HOTBAR_WORDS):
                hb = hotbar(active=0)
                if any(w in text for w in _HOTBAR_DOT_WORDS):
                    on = not any(w in text for w in ("灭", "恢复纯手动", "OFF"))
                    hb = hotbar(dot=0 if on else None, active=None)
                elif any(w in text for w in _HOTBAR_GONE_WORDS):
                    hb = hotbar(off=[3], active=None)
                elif any(w in text for w in ("多出", "新键", "临时", "换成", "替换")):
                    hb = hotbar(extra=3, active=None)
                elif any(w in text for w in _HOTBAR_DENY_WORDS):
                    hb = hotbar(deny=0, active=None)
                elif any(w in text for w in _HOTBAR_CD_WORDS):
                    hb = hotbar(cd=0, active=None)
                castl.append(hb)
                added["hotbar"] += 1
            elif "hotbar" not in kinds and any(w in text for w in _HOTBAR_CD_WORDS) and "menu" not in kinds and "card" not in kinds:
                castl.append(hotbar(cd=0, active=None))
                added["hotbar"] += 1
            if "bar" not in kinds and any(w in text for w in _BAR_WORDS):
                broken = any(w in text for w in _BAR_BROKEN_WORDS)
                done = any(w in text for w in ("完成", "读完", "满"))
                castl.append(bar(ax, max(8, ay - 22),
                                 ratio=1.0 if done else 0.55,
                                 kind="charge" if "蓄力" in text else "cast",
                                 broken=broken))
                added["bar"] += 1
            if "key" not in kinds and any(w in text for w in _KEY_WORDS):
                gone = any(w in text for w in ("消失", "收回", "无提示"))
                castl.append(keyhint(min(92, ax + 14), max(10, ay - 16), "F",
                                     state="off" if gone else "active"))
                added["key"] += 1
    return added


def _audit_casts(cases: list) -> list[str]:
    """Warn beats whose cast is badge-only (weak storyboard)."""
    weak = []
    for c in cases:
        for i, b in enumerate(c["beats"]):
            cast = b.get("cast") or []
            kinds = {el.get("t") for el in cast}
            if not cast or kinds <= {"badge"}:
                weak.append(f"{c['id']}#T{i+1}")
    return weak


def apply_action_index(cases: list) -> list[dict]:
    """Attach UX-NNN / platform on each case; return unique action rows for the UI list."""
    by_id = {c["id"]: c for c in cases}
    seen: set[str] = set()
    actions: list[dict] = []
    for action_no, key, title, members in ACTION_GROUPS:
        variants = []
        target_union: set[str] = set()
        genre_union: set[str] = set()
        summaries: list[str] = []
        beat_total = 0
        for case_id, platform in members:
            if case_id in seen:
                raise SystemExit(f"action index duplicate case: {case_id}")
            if case_id not in by_id:
                raise SystemExit(f"action index unknown case: {case_id}")
            if platform not in PLATFORM_LABEL:
                raise SystemExit(f"action index bad platform {platform} on {case_id}")
            seen.add(case_id)
            c = by_id[case_id]
            label = PLATFORM_LABEL[platform]
            c["actionNo"] = action_no
            c["actionKey"] = key
            c["actionTitle"] = title
            c["platform"] = platform
            c["platformLabel"] = label
            target_union.update(c.get("targets") or [])
            genre_union.update(c.get("genres") or [])
            summaries.append(c.get("summary") or "")
            beat_total += len(c.get("beats") or [])
            variants.append(
                {
                    "platform": platform,
                    "platformLabel": label,
                    "caseId": case_id,
                }
            )
        variants.sort(key=lambda v: PLATFORM_ORDER.index(v["platform"]))
        for v in variants:
            by_id[v["caseId"]]["variants"] = [
                {"platform": x["platform"], "platformLabel": x["platformLabel"], "caseId": x["caseId"]}
                for x in variants
            ]
        primary = by_id[variants[0]["caseId"]]
        actions.append(
            {
                "actionNo": action_no,
                "key": key,
                "title": title,
                "summary": primary.get("summary") or title,
                "platforms": [v["platform"] for v in variants],
                "platformLabels": [v["platformLabel"] for v in variants],
                "variants": variants,
                "targets": sorted(target_union),
                "genres": sorted(genre_union),
                "beatCount": beat_total,
                "caseCount": len(variants),
            }
        )
    missing = sorted(set(by_id) - seen)
    if missing:
        raise SystemExit(f"cases missing from action index: {missing[:20]}")
    return actions


def apply_hotbar_keycaps(cases: list) -> int:
    """按 case 的平台给技能栏格子印上对应键位：键盘 Q/W/E/R、手柄面键、触控序号。

    渲染器不再兜底猜键位；标签缺了直接报错，免得手柄玩家看到一排键盘字母。
    """
    filled = 0
    for c in cases:
        caps = HOTBAR_KEYCAPS.get(c.get("platform"))
        if caps is None:
            raise SystemExit(f"{c['id']} platform={c.get('platform')} 没登记技能栏键位")
        for b in c["beats"]:
            for e in b.get("cast") or []:
                if e.get("t") != "hotbar":
                    continue
                slots = e.get("slots") or 4
                if slots > len(caps):
                    raise SystemExit(f"{c['id']} 技能栏 {slots} 格超出 {c['platform']} 键位表")
                e["labels"] = list(caps[:slots])
                filled += 1
    return filled


def _cursor_candidates(hit: dict) -> list[tuple[float, float]]:
    """按「箭尖贴着目标、箭身朝空处」给出候选落点，从最自然的右下开始试。"""
    r = _entity_radius_px(hit)
    ux = lambda p: p / STAGE_W_PX * 100.0  # noqa: E731
    uy = lambda p: p / STAGE_H_PX * 100.0  # noqa: E731
    bw, bh = CURSOR_BODY_PX
    return [
        (hit["x"] + ux(r + 2), hit["y"] + uy(r + 2)),          # 右下：目标在左上
        (hit["x"] - ux(r + 2), hit["y"] + uy(r + 2)),          # 左下：目标在正上
        (hit["x"] + ux(r + 2), hit["y"]),                       # 正右：目标在正左
        (hit["x"] - ux(bw + r + 3), hit["y"]),                  # 左侧：箭身在目标前收住
        (hit["x"], hit["y"] - uy(bh + r + 3)),                  # 上方：箭身在目标前收住
    ]


def deocclude_cursors(cases: list) -> int:
    """箭头光标压在单位/建筑身上时，把箭尖挪到目标的右下边缘外。

    箭头图形从箭尖往右下伸展，压在正中就把目标盖掉（玩家看不见自己点的是谁）。
    箭尖落在目标右下角外侧时，箭尖依旧贴着目标，箭身朝空地伸展。
    """
    moved = 0
    for c in cases:
        for b in c["beats"]:
            cast = b.get("cast") or []
            bodies = [e for e in cast if e.get("t") in CURSOR_OCCLUDERS]
            for cur in cast:
                if cur.get("t") != "cursor":
                    continue
                if cur.get("mode") not in CURSOR_MODES_OFFSET:
                    continue
                hit = next((e for e in bodies if _cursor_covers(cur, e)), None)
                if hit is None:
                    continue
                spot = next(
                    (
                        (x, y)
                        for x, y in _cursor_candidates(hit)
                        if 3.0 <= x <= 94.0 and 4.0 <= y <= 88.0
                        and not any(_cursor_covers({"x": x, "y": y}, e) for e in bodies)
                    ),
                    None,
                )
                if spot is None:
                    raise SystemExit(
                        f"{c['id']} 这一拍摆不下光标：目标太挤，手工调 cast 坐标 "
                        f"(光标 {cur['x']},{cur['y']})"
                    )
                cur["x"], cur["y"] = round(spot[0], 2), round(spot[1], 2)
                moved += 1
    return moved


def _audit_storyboard(cases: list) -> None:
    """画面必须画出文案承诺的东西；对不上就 fail，不许悄悄糊过去。"""
    bad_view: list[str] = []
    bad_cursor: list[str] = []
    occluded: list[str] = []
    missing_reticle: list[str] = []
    missing_menu: list[str] = []
    for c in cases:
        for i, b in enumerate(c["beats"]):
            tag = f"{c.get('actionNo') or c['id']} {c['id']}#T{i+1}"
            cast = b.get("cast") or []
            kinds = {e.get("t") for e in cast}
            if b.get("view") not in VIEW_LABELS:
                bad_view.append(f"{tag} view={b.get('view')}")
            cursors = [e for e in cast if e.get("t") == "cursor"]
            for cur in cursors:
                if cur.get("mode") not in CURSOR_MODES:
                    bad_cursor.append(f"{tag} mode={cur.get('mode')}")
            bodies = [e for e in cast if e.get("t") in CURSOR_OCCLUDERS]
            for cur in cursors:
                if cur.get("mode") not in CURSOR_MODES_OFFSET:
                    continue
                if any(_cursor_covers(cur, e) for e in bodies):
                    occluded.append(tag)
                    break
            blob = f"{b['input']} {b.get('logic') or ''} {b['screen']}"
            if ("专属准星" in blob or "变准星" in blob) and not (
                "crosshair" in kinds or any(e.get("mode") == "aim" for e in cursors)
            ):
                missing_reticle.append(tag)
            if "菜单项" in blob and "menu" not in kinds:
                missing_menu.append(tag)
    promise: list[str] = []
    structure: list[str] = []
    platform: list[str] = []
    for c in cases:
        for p in check_platform(c):
            platform.append(f"{c.get('actionNo')} {c['id']}: {p}")
        for i, b in enumerate(c["beats"]):
            tag = f"{c.get('actionNo')} {c['id']}#T{i+1}"
            for why, howto in check_beat(b):
                promise.append(f"{tag}: {why} → {howto}")
            for p in check_structure(b):
                structure.append(f"{tag}: {p}")
    problems = []
    if bad_view:
        problems.append(f"未知镜位（VIEW_LABELS 未登记）: {bad_view}")
    if bad_cursor:
        problems.append(f"未知光标状态（渲染器画不出）: {bad_cursor}")
    if occluded:
        problems.append(f"箭头光标压住单位: {occluded}")
    if missing_reticle:
        problems.append(f"文案说变准星但画面没准星: {missing_reticle}")
    if missing_menu:
        problems.append(f"文案说点菜单项但画面没菜单: {missing_menu}")
    if platform:
        problems.append("平台标注和画面/文案矛盾:\n    " + "\n    ".join(platform))
    if promise:
        problems.append("文案承诺的东西画面没画:\n    " + "\n    ".join(promise))
    if structure:
        problems.append("画面本身不合法:\n    " + "\n    ".join(structure))
    if problems:
        raise SystemExit("分镜画面与文案对不上：\n- " + "\n- ".join(problems))


def _write_checkpoint(cases: list, weak: list[str], head: str, actions: list[dict] | None = None) -> None:
    todos = sorted({t for c in cases for t in c.get("todos") or []})
    from collections import Counter

    target_counts = Counter()
    for c in cases:
        for t in c.get("targets") or []:
            target_counts[t] += 1
    lines = [
        "# 玩家动作 UX 图鉴 · Agent Checkpoint",
        "",
        "后续 Agent 读此页再改，避免和旧口头结论打架。",
        "",
        "## 生成时身份",
        "",
        f"- 生成脚本：`scripts/generate-player-action-ux-catalog.py`",
        f"- 逻辑文案：`scripts/player_action_ux_beat_logic.py`",
        f"- 实现标注：`scripts/player_action_ux_impl_notes.py`",
        f"- 动作编号/平台变体：`scripts/player_action_ux_action_index.py`",
        f"- 生成时 HEAD：`{head}`（以你拉取后的 `git rev-parse` 为准；合并后会变）",
        f"- 分支语境：`cursor/ux-action-id-platform-tabs-4211`（unique 动作编号 + 主机/键鼠/触控 tab）",
        f"- 已合 main：图鉴 #743–#755（含按游戏分类、时序三参与者、一镜一对、双人审核）",
        "",
        "## 页面交互约定（改 UI 前先读）",
        "",
        "- 三栏：**复刻目标游戏** | **唯一动作列表（UX-NNN）** | 详情",
        "- 左栏 id 来自 `TARGET_GAMES`；筛选看动作任一平台变体的 `targets`，允许重复",
        "- 中间列表按 `actions[]`（查重后的 unique 交互），不是原始 case 堆叠",
        "- 同一交互在主机 / 键鼠 / 触控上的不同实现 → 详情顶部分 tab 切换 `variants[]`",
        "- `case.category` / `family` = 功能族，只给 impl_notes 与详情副标",
        "- 详情内：**一镜一对**——每拍一行，左 Mermaid / 右分镜，共用一条滚动轴（禁止左右各自滚）",
        "- 拍号芯片 / ←→ / h·l 跳到对应那一对；每拍单独一张时序图",
        "- **时序图参与者只有三个：设备输入 → 逻辑处理 → 画面输出**；禁止再把手感/爽点做成泳道",
        "- 每拍必有 `input` / `logic` / `screen`；`logic` 来自 `BEAT_LOGIC`，缺键直接 fail 生成",
        "- 每个 case 必有 `ludots` / `todos` / `actionNo` / `platform`；勿手改 `catalog-data.js`",
        "",
        "## 双人交叉审核（本轮）",
        "",
        "- 10 路审核：5 批 × 双人独立，覆盖全部 168 case",
        "- 双方一致 225 条（high 96 / med 95 / low 34）；单人 high 19 作参考",
        "- F0–F4 修复合并后，R1×R2 再审原 high 案；双方仍共指 5 案已补修",
        "- 重点：缺 cast（框/圈/锥/菜单/卡/键/条）、该拆未拆拍、文案与画面不一致",
        "",
        "## 复刻目标分类",
        "",
    ]
    for gid, title, blurb in TARGET_GAMES:
        lines.append(f"- `{gid}` **{title}**（{target_counts.get(gid, 0)}）— {blurb}")
    lines.extend([
        "",
        "- 同一动作出现在多个游戏下是预期",
        "- 仍可后续补目标：战棋格子、塔防造塔、炉石对战、观战裁判（见 todos）",
        "",
        "## 规模",
        "",
        f"- unique_actions = {len(actions or [])}",
        f"- multi_platform_actions = {sum(1 for a in (actions or []) if a.get('caseCount', 0) > 1)}",
        "- 平台覆盖（唯一动作计）："
        + "、".join(
            f"{label} {sum(1 for a in (actions or []) if label in (a.get('platformLabels') or []))}"
            for label in PLATFORM_LABEL.values()
        )
        + " —— 图鉴目前以键鼠为主，主机/触控实现是内容缺口，不是渲染 bug",
        f"- cases = {len(cases)}（含平台变体实现）",
        f"- beats = {sum(len(c['beats']) for c in cases)}",
        f"- target_games = {len(TARGET_GAMES)}",
        f"- target_memberships = {sum(len(c.get('targets') or []) for c in cases)}（含跨游戏重复）",
        "",
        "## 分镜画面审计",
        "",
        f"- 仅 badge / 空 cast 的弱分镜拍数：{len(weak)}",
        "- 弱分镜不阻断生成，但改数据时应补单位/光标/指示器，禁止「只有字没有画面」",
        "- `_audit_storyboard()` 是硬闸，规则表在 `scripts/player_action_ux_storyboard_rules.py`：",
        "  1. 平台标注 vs 画面元件（键鼠不许画摇杆、触控不许画鼠标、主机不许画鼠标）",
        "  2. 平台标注 vs 文案设备词（键鼠文案不许出现摇杆/扳机，反之同理）",
        "  3. 文案承诺的元素必须画出（准星/菜单/选中圈/读条/落点圈/扇形/摇杆/选框/技能栏/"
        "键帽/敌人/卡牌/轨迹/触点/WASD/轮盘/锚点）",
        "  4. 画面本身合法（有看得见的主体、坐标不出界、同类元件不画重）",
        "  5. 镜位未登记、光标状态渲染器画不出、箭头压住单位",
        "  6. 元件参数只能用白名单里的枚举值（写别的渲染器会静默画错）",
        "- 任一命中直接 fail 生成；要放宽先改规则表，不许在数据里绕开",
        "- 机器判不出来、要人做内容决策的遗留项在 `AUDIT-BACKLOG.md`（平台补齐、编号合并、"
        "该拆的动作、缺的失败拍、还缺的元件）—— 动图鉴前先读那一页",
        f"- 光标状态白名单：{' / '.join(CURSOR_MODES)}；`aim`=施法准星，`up`=松手波纹",
        f"- 镜位角标只出人话：{'、'.join(VIEW_LABELS.values())}",
        "",
        "## 高频 TODO（去重）",
        "",
    ])
    for t in todos:
        lines.append(f"- {t}")
    lines.append("")
    CHECKPOINT_MD.write_text("\n".join(lines), encoding="utf-8")


def main():
    head = _git_head()
    cases = build_cases()  # already enrich_all inside
    assign_game_targets(cases)
    apply_beat_logic(cases)
    actions = apply_action_index(cases)
    augmented = augment_ui_glyphs(cases)
    keycaps = apply_hotbar_keycaps(cases)
    nudged = deocclude_cursors(cases)
    _audit_storyboard(cases)
    weak = _audit_casts(cases)
    empty = [c["id"] for c in cases if not c.get("targets")]
    if empty:
        raise SystemExit(f"cases missing targets: {empty}")
    no_logic = [
        f"{c['id']}#T{i+1}"
        for c in cases
        for i, b in enumerate(c["beats"])
        if not b.get("logic")
    ]
    if no_logic:
        raise SystemExit(f"beats missing logic after apply: {no_logic[:20]}")
    no_action = [c["id"] for c in cases if not c.get("actionNo") or not c.get("platform")]
    if no_action:
        raise SystemExit(f"cases missing action index fields: {no_action[:20]}")
    payload = {
        "title": "玩家动作体验图鉴",
        "subtitle": "按复刻目标浏览。唯一动作编号 UX-NNN；跨平台同一交互用主机/键鼠/触控分 tab。",
        "taxonomy": "game-targets",
        "sequenceModel": ["device-input", "logic", "screen-output"],
        "platforms": [
            {"id": pid, "label": PLATFORM_LABEL[pid]} for pid in PLATFORM_ORDER
        ],
        "views": [{"id": vid, "label": label} for vid, label in VIEW_LABELS.items()],
        "cursorModes": list(CURSOR_MODES),
        "checkpoint": {
            "head": head,
            "branch_hint": "cursor/ux-action-id-platform-tabs-4211",
            "impl_notes": "scripts/player_action_ux_impl_notes.py",
            "beat_logic": "scripts/player_action_ux_beat_logic.py",
            "action_index": "scripts/player_action_ux_action_index.py",
            "note": "列表=unique actions；详情平台 tab；时序=设备/逻辑/画面；勿手改 catalog-data.js",
            "weak_storyboard_beats": weak,
            "unique_actions": len(actions),
            "multi_platform_actions": sum(1 for a in actions if a["caseCount"] > 1),
        },
        "categories": [
            {"id": gid, "title": title, "blurb": blurb}
            for gid, title, blurb in TARGET_GAMES
        ],
        "actions": actions,
        "cases": cases,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(
        "// AUTO-GENERATED by scripts/generate-player-action-ux-catalog.py — do not hand-edit.\n"
        + "window.PLAYER_ACTION_UX_CATALOG = "
        + json.dumps(payload, ensure_ascii=False, indent=2)
        + ";\n",
        encoding="utf-8",
    )
    _write_checkpoint(cases, weak, head, actions)
    print(
        f"Wrote {OUT.relative_to(OUT.parents[2])}  actions={len(actions)}  "
        f"cases={len(cases)}  beats={sum(len(x['beats']) for x in cases)}  "
        f"multi_platform={sum(1 for a in actions if a['caseCount'] > 1)}  "
        f"weak_casts={len(weak)}  ui_augmented={augmented}  "
        f"hotbar_keycaps={keycaps}  cursor_nudged={nudged}  head={head}"
    )


if __name__ == "__main__":
    main()

