#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate player-action UX catalog data (SSOT → catalog-data.js).

Cases are player/PM language only: what hands do, what the screen shows, what it feels like.
No technical layering. Storyboard visuals are comic-panel descriptors for the HTML renderer.
"""
from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "gitbook/reference/player-action-ux/catalog-data.js"


def beat(input_text, screen, feel, view, cast, title=None):
    return {
        "title": title,
        "input": input_text,
        "screen": screen,
        "feel": feel,
        "view": view,
        "cast": cast,
    }


def case(cid, category, title, summary, beats, genres=None):
    return {
        "id": cid,
        "category": category,
        "title": title,
        "summary": summary,
        "genres": genres or [],
        "beats": beats,
    }


# ---- visual helpers (normalized 0..100 stage coords) ----
def unit(x, y, sel=False, team="ally", face=0, size=1):
    return {"t": "unit", "x": x, "y": y, "sel": sel, "team": team, "face": face, "size": size}


def cursor(x, y, mode="idle"):
    return {"t": "cursor", "x": x, "y": y, "mode": mode}


def box(x, y, w, h):
    return {"t": "box", "x": x, "y": y, "w": w, "h": h}


def stick(side, nx, ny):
    return {"t": "stickL" if side == "L" else "stickR", "nx": nx, "ny": ny}


def crosshair(x, y, locked=False):
    return {"t": "crosshair", "x": x, "y": y, "locked": locked}


def ring(x, y, r=8, kind="select"):
    return {"t": "ring", "x": x, "y": y, "r": r, "kind": kind}


def cone(x, y, angle=0, spread=40, length=28):
    return {"t": "cone", "x": x, "y": y, "angle": angle, "spread": spread, "length": length}


def arrow(x1, y1, x2, y2, kind="move"):
    return {"t": "arrow", "x1": x1, "y1": y1, "x2": x2, "y2": y2, "kind": kind}


def circle_ind(x, y, r=16, ok=True):
    return {"t": "circle", "x": x, "y": y, "r": r, "ok": ok}


def building(x, y, ghost=False):
    return {"t": "building", "x": x, "y": y, "ghost": ghost}


def badge(text):
    return {"t": "badge", "text": text}


def path(points, kind="move"):
    return {"t": "path", "points": points, "kind": kind}


def hero(x, y, face=0):
    return {"t": "hero", "x": x, "y": y, "face": face}


CATEGORIES = [
    ("select", "一、谁听我的"),
    ("basic-order", "二、常规指令（走/停/打）"),
    ("aim", "三、对准世界"),
    ("attack", "四、基本攻击与射击"),
    ("twin-stick", "五、双摇杆射击"),
    ("instant-skill", "六、不用瞄的技能"),
    ("unit-skill", "七、要选单位的技能"),
    ("ground-skill", "八、要点地面的技能"),
    ("direction-skill", "九、要选方向的技能"),
    ("hold", "十、按住不放"),
    ("combo", "十一、一段接一段"),
    ("defense", "十二、防 / 躲 / 反击窗"),
    ("environment", "十三、和环境互动"),
    ("army", "十四、部队 / 宝宝 / 载具"),
    ("cast-habit", "十五、同技能不同手感"),
    ("blocked", "十六、放不了时的反馈"),
]


def build_cases():
    c = []

    # ===== 一、谁听我的 =====
    c.append(case(
        "select-click", "select", "点一下选中一个单位",
        "鼠标点到某个单位，它成为当前指挥对象。",
        [
            beat("把指针移到单位上", "单位可被高亮/描边提示", "准备选中", "topdown",
                 [unit(40, 50), unit(62, 42), unit(55, 68), cursor(62, 42), badge("悬停")], title="悬停"),
            beat("按下并松开左键", "该单位脚下出现选中圈，他人无圈", "它现在听你的", "topdown",
                 [unit(40, 50), unit(62, 42, sel=True), unit(55, 68), ring(62, 42), cursor(62, 42, "up"), badge("单击")], title="点选"),
        ], ["RTS", "MOBA", "SC2/War3"],
    ))
    c.append(case(
        "select-box", "select", "拖框选出一群",
        "按住拖出矩形，框内单位一起被选中。",
        [
            beat("在空地按下左键", "出现选框起点", "开始框选", "topdown",
                 [unit(35, 40), unit(48, 45), unit(58, 52), unit(70, 38), cursor(30, 30, "down"), badge("按下")], title="按下"),
            beat("拖动鼠标", "半透明选框扩大，框内单位闪一下", "还在框", "topdown",
                 [unit(35, 40), unit(48, 45), unit(58, 52), unit(70, 38), box(30, 30, 40, 30), cursor(70, 60, "drag"), badge("拖动")], title="拖框"),
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
        ], ["RTS", "SC2/War3"],
    ))
    c.append(case(
        "select-double-type", "select", "双击选同类型",
        "双击一个单位，同屏（或全局规则下）同类型一并选中。",
        [
            beat("双击士兵", "所有同造型士兵出现选中圈", "一键同型", "topdown",
                 [unit(30, 50, sel=True), unit(45, 55, sel=True), unit(60, 48, sel=True), unit(70, 70),
                  ring(30, 50), ring(45, 55), ring(60, 48), cursor(45, 55, "up"), badge("双击")], title="双击"),
        ], ["RTS", "SC2/War3"],
    ))
    c.append(case(
        "select-control-group", "select", "记编队 / 召编队",
        "Ctrl+数字记住当前选中；之后按数字召回。",
        [
            beat("选中一队后按 Ctrl+1", "界面出现编队 1 的肖像/计数", "记住了", "topdown",
                 [unit(40, 50, sel=True), unit(55, 52, sel=True), ring(40, 50), ring(55, 52), badge("Ctrl+1")], title="记住"),
            beat("稍后按 1", "镜头可跳转；那队重新被选中", "召回编队", "topdown",
                 [unit(40, 50, sel=True), unit(55, 52, sel=True), ring(40, 50), ring(55, 52), badge("按 1")], title="召回"),
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
        "附身、上车、切英雄、观战跟随时，手感跟到新主体。",
        [
            beat("对载具按下交互", "镜头切到载具视角/操控", "你变成车", "tps",
                 [hero(40, 60), building(65, 55), badge("上车")], title="切换前"),
            beat("完成切换", "准星/摇杆控制载具", "手感已换皮", "tps",
                 [building(50, 55), badge("载具中")], title="切换后"),
        ], ["TPS", "RTS", "MOBA"],
    ))
    c.append(case(
        "select-clear", "select", "取消全部选中",
        "点空地或按专用键，选中圈全部消失。",
        [
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
            beat("按下攻击移动键再点地面", "路径带剑标；途中遇敌会停下来打", "边走边警惕", "topdown",
                 [unit(30, 60, sel=True, face=20), ring(30, 60), arrow(30, 60, 75, 35, "attack"),
                  unit(60, 40, team="enemy"), badge("A+点地")], title="攻击移动"),
        ], ["RTS", "SC2/War3", "MOBA"],
    ))
    c.append(case(
        "order-stop-hold", "basic-order", "停止 / 原地坚守",
        "立刻打断当前行动；或站桩反击不追击。",
        [
            beat("按停止", "单位停下，移动线消失", "站住", "topdown",
                 [unit(50, 50, sel=True), ring(50, 50), badge("Stop")], title="停止"),
            beat("按坚守", "单位站桩，有敌人靠近才打", "不追", "topdown",
                 [unit(50, 50, sel=True), ring(50, 50), badge("Hold")], title="坚守"),
        ], ["RTS"],
    ))
    c.append(case(
        "order-smart-right", "basic-order", "右键智能指令",
        "点矿去采、点敌去打、点建筑去进——同一右键，目标不同结果不同。",
        [
            beat("右键敌人", "进入攻击该目标", "去干他", "topdown",
                 [unit(35, 55, sel=True, face=25), ring(35, 55), unit(70, 40, team="enemy"),
                  arrow(35, 55, 70, 40, "attack"), cursor(70, 40, "up"), badge("右键敌人")], title="打"),
            beat("右键矿点/资源", "工人去采集", "去挖", "topdown",
                 [unit(35, 55, sel=True), ring(35, 55), building(70, 45), arrow(35, 55, 70, 45, "move"),
                  cursor(70, 45, "up"), badge("右键资源")], title="采"),
        ], ["SC2/War3", "RTS"],
    ))
    c.append(case(
        "order-queue", "basic-order", "排队一串指令",
        "按住 Shift 连续下命令，做完一个接下一个。",
        [
            beat("Shift+右键点 A，再点 B", "地上出现 A→B 路点链", "排好队了", "topdown",
                 [unit(25, 60, sel=True), ring(25, 60), path([(25, 60), (50, 40), (75, 55)], "move"),
                  badge("Shift 排队")], title="队列"),
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
                 [unit(35, 55, sel=True), ring(35, 55), building(60, 50), arrow(35, 55, 60, 50, "move"),
                  badge("装载")], title="装"),
            beat("在别处下令卸载", "士兵在载具旁出现", "下车", "topdown",
                 [building(40, 50), unit(55, 48), unit(58, 58), badge("卸载")], title="卸"),
        ], ["SC2/War3", "RTS"],
    ))

    # ===== 三、对准 =====
    c.append(case(
        "aim-look", "aim", "转动视角 / 准星",
        "鼠标或右摇杆转动视野，准星落在世界某处。",
        [
            beat("移动鼠标/右摇杆", "画面旋转或准星移动", "我在看那儿", "fps",
                 [crosshair(55, 45), badge("瞄准")], title="转视角"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "aim-soft-lock", "aim", "软锁定附近敌人",
        "系统把攻击倾向吸向近处敌人，仍可挣脱。",
        [
            beat("靠近敌人进入锁定辅助", "目标描边；攻击朝向他", "咬住了", "tps",
                 [hero(40, 60, face=25), unit(65, 45, team="enemy"), ring(65, 45, kind="lock"),
                  badge("软锁")], title="软锁"),
        ], ["动作RPG", "蝙蝠侠/蜘蛛侠"],
    ))
    c.append(case(
        "aim-hard-lock", "aim", "硬锁定切换目标",
        "锁定一人；按键切换上一个/下一个。",
        [
            beat("按下锁定", "镜头与准星钉死目标", "死死咬住", "tps",
                 [hero(40, 60), unit(70, 40, team="enemy"), crosshair(70, 40, locked=True), badge("硬锁")], title="锁定"),
            beat("按切换", "锁跳到下一个敌人", "换人咬", "tps",
                 [hero(40, 60), unit(55, 35, team="enemy"), crosshair(55, 35, locked=True), badge("切换")], title="切换"),
        ], ["动作RPG", "TPS"],
    ))
    c.append(case(
        "aim-skill-indicator", "aim", "进入技能瞄准（指示器）",
        "按技能后地上出现范围/方向预览，确认前可移动预览。",
        [
            beat("按下技能键", "进入瞄准；出现圈/扇形指示器", "先瞄再放", "moba",
                 [hero(40, 60), circle_ind(65, 40, 18, True), cursor(65, 40), badge("技能瞄准")], title="出指示器"),
            beat("移动鼠标", "指示器跟随；非法区变红", "找落点", "moba",
                 [hero(40, 60), circle_ind(75, 55, 18, False), cursor(75, 55), badge("调整")], title="调整"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "aim-cancel", "aim", "瞄准中取消",
        "右键或取消键退出瞄准，不放出技能。",
        [
            beat("瞄准中按取消", "指示器消失，回到常态", "当没按过", "moba",
                 [hero(45, 55), cursor(60, 50), badge("取消")], title="取消"),
        ], ["MOBA", "RTS超武"],
    ))

    # ===== 四、基本攻击 =====
    c.append(case(
        "atk-melee-tap", "attack", "近战点一下",
        "轻按攻击键挥砍一下。",
        [
            beat("点攻击键", "角色挥砍，命中有反馈", "砍一下", "tps",
                 [hero(45, 55, face=20), unit(65, 45, team="enemy"), arrow(50, 52, 62, 47, "attack"), badge("轻击")], title="挥砍"),
        ], ["动作RPG", "ARPG"],
    ))
    c.append(case(
        "atk-melee-hold-chain", "attack", "按住/连点打连段",
        "连续输入打出轻攻击链。",
        [
            beat("连点攻击", "招式一段接一段", "连起来了", "tps",
                 [hero(45, 55, face=15), unit(68, 48, team="enemy"), badge("连击中")], title="连段"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "atk-gun-tap-spray", "attack", "枪械点射 / 按住扫射",
        "点一下一发；按住连续开火。",
        [
            beat("点射击键", "射出一发，准星微扬", "点射", "fps",
                 [crosshair(50, 48), badge("点射")], title="点射"),
            beat("按住射击键", "连续出弹，准星扩散", "压枪扫", "fps",
                 [crosshair(52, 46), badge("扫射")], title="扫射"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "atk-ads-reload-swap", "attack", "开镜 / 换弹 / 切枪",
        "肩键开镜稳定准星；换弹读条；切枪换手感。",
        [
            beat("按开镜", "视野拉近，准星变精准", "瞄稳", "fps",
                 [crosshair(50, 50, locked=True), badge("开镜")], title="开镜"),
            beat("按换弹", "弹药数刷新，短暂不能射", "换弹中", "fps",
                 [crosshair(50, 50), badge("换弹")], title="换弹"),
            beat("按切枪", "武器模型与弹种切换", "换一把", "fps",
                 [crosshair(50, 50), badge("切枪")], title="切枪"),
        ], ["FPS", "TPS"],
    ))
    c.append(case(
        "atk-grenade", "attack", "扔手雷到落点",
        "拿出手雷，看抛物线/落点圈，确认扔出。",
        [
            beat("按住手雷键瞄准", "地面落点圈 + 抛物预览", "看落点", "tps",
                 [hero(35, 60), circle_ind(70, 40, 12, True), path([(38, 55), (55, 30), (70, 40)], "arc"), badge("手雷")], title="预览"),
            beat("松开/再按确认", "手雷飞出，落点爆炸", "炸那儿", "tps",
                 [hero(35, 60), circle_ind(70, 40, 14, True), badge("投出")], title="投出"),
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
                 [hero(45, 55, face=90), stick("L", 0, -0.8), stick("R", 0.9, 0),
                  arrow(50, 55, 78, 55, "attack"), unit(80, 52, team="enemy"), badge("双摇杆")], title="分离"),
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
                 [hero(45, 55, face=40), unit(70, 40, team="enemy"), stick("L", 0.5, -0.5), stick("R", 0, 0),
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
            beat("右摇杆靠近敌人方向", "准星/朝向吸到敌人", "帮你咬准", "topdown",
                 [hero(40, 55, face=35), unit(70, 40, team="enemy"), ring(70, 40, kind="lock"),
                  stick("R", 0.6, -0.4), badge("磁吸")], title="磁吸"),
        ], ["双摇杆射击"],
    ))
    c.append(case(
        "twin-kb-mouse-equiv", "twin-stick", "键鼠等价：WASD + 鼠标瞄",
        "同一套移瞄分离，只是输入皮肤不同。",
        [
            beat("WASD 走，鼠标定朝向", "顶视角角色移瞄分离，鼠标方向开火", "键鼠双摇杆", "topdown",
                 [hero(45, 55, face=50), cursor(75, 40), arrow(50, 52, 72, 42, "attack"),
                  badge("WASD+鼠标")], title="键鼠"),
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
                 [hero(50, 55), circle_ind(50, 55, 22, True), unit(65, 50, team="enemy"), badge("自身AOE")], title="炸圈"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "skill-blink-facing", "instant-skill", "按键朝面向闪现",
        "瞬移一小段，方向跟面向或移动键。",
        [
            beat("按闪现", "残影到新位置", "闪过去", "topdown",
                 [hero(40, 55), arrow(40, 55, 65, 45, "move"), hero(65, 45), badge("闪现")], title="闪"),
        ], ["MOBA", "ARPG"],
    ))
    c.append(case(
        "skill-toggle-form", "instant-skill", "开关姿态 / 切形态",
        "再按关掉；或锤炮切换导致技能栏变化。",
        [
            beat("按切换", "造型与技能图标组切换", "换一套招", "moba",
                 [hero(50, 55), badge("形态切换")], title="切换"),
        ], ["MOBA", "RTS英雄"],
    ))

    # ===== 七、选单位技能 =====
    c.append(case(
        "skill-pick-enemy", "unit-skill", "技能后点敌人",
        "先按技能，再点合法敌方目标放出。",
        [
            beat("按技能", "鼠标变专属准星", "选目标", "moba",
                 [hero(35, 60), cursor(60, 40), badge("选敌")], title="进入"),
            beat("点敌人", "技能飞向该单位", "点到了", "moba",
                 [hero(35, 60), unit(65, 40, team="enemy"), arrow(40, 55, 62, 42, "attack"),
                  cursor(65, 40, "up"), badge("确认")], title="点中"),
        ], ["MOBA", "RTS", "War3"],
    ))
    c.append(case(
        "skill-smart-cast-unit", "unit-skill", "智能施法打准星下单位",
        "按键当下自动对准星下单位施放，不进瞄准态。",
        [
            beat("准星已在敌人上时按技能", "立刻出手，无指示器阶段", "又快又险", "moba",
                 [hero(35, 60), unit(65, 42, team="enemy"), cursor(65, 42), arrow(40, 55, 62, 44, "attack"),
                  badge("智能施法")], title="瞬放"),
        ], ["MOBA"],
    ))
    c.append(case(
        "skill-ally-only", "unit-skill", "只能点特定对象",
        "治疗只能点友军；不合规目标禁止态。",
        [
            beat("技能瞄准移到敌人上", "准星禁止，点不出去", "对象不对", "moba",
                 [hero(35, 60), unit(65, 40, team="enemy"), cursor(65, 40), badge("禁止")], title="非法"),
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
            beat("按技能后点地", "落点圈亮起并结算", "砸这儿", "moba",
                 [hero(30, 60), circle_ind(65, 40, 16, True), cursor(65, 40, "up"), badge("点地")], title="落点"),
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
                 [building(55, 45, ghost=True), cursor(55, 45), badge("建造预览")], title="预览"),
            beat("确认放置", "建筑开工/落地", "定了", "topdown",
                 [building(55, 45, ghost=False), badge("建造")], title="放下"),
        ], ["C&C", "RTS"],
    ))
    c.append(case(
        "skill-minimap-ground", "ground-skill", "小地图点落点",
        "大招/支援可在小地图点一下当世界落点。",
        [
            beat("技能瞄准时点小地图", "大地图对应位置落下技能", "远程点射地图", "moba",
                 [hero(25, 70), circle_ind(70, 30, 14, True), badge("小地图施法")], title="小地图"),
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
                 [hero(40, 55), cone(40, 55, angle=20, spread=55, length=32), badge("扇形")], title="扇形"),
        ], ["MOBA", "ARPG", "TPS"],
    ))
    c.append(case(
        "skill-dash-dir", "direction-skill", "指定方向冲刺",
        "朝选定方向突进一段距离。",
        [
            beat("选定方向确认", "角色沿箭头冲出", "冲！", "topdown",
                 [hero(35, 55), arrow(35, 55, 70, 40, "move"), path([(35, 55), (70, 40)], "move"), badge("冲刺")], title="冲刺"),
        ], ["MOBA", "ARPG", "动作RPG"],
    ))
    c.append(case(
        "skill-grapple", "direction-skill", "钩索荡出去",
        "朝方向甩钩，钩到锚点才位移。",
        [
            beat("甩出钩索", "线连向锚点/敌人", "钩住了吗", "tps",
                 [hero(35, 65), path([(38, 60), (70, 35)], "arc"), building(72, 32), badge("钩索")], title="甩钩"),
            beat("钩中后荡移", "角色被拉向锚点", "荡过去", "tps",
                 [hero(65, 40), building(72, 32), badge("荡")], title="荡"),
        ], ["蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "skill-vector", "direction-skill", "拉一条矢量再放",
        "拖出起点到终点的矢量技能。",
        [
            beat("按下定起点，拖到终点松开", "地面出现矢量箭头", "从这到那", "moba",
                 [hero(30, 60), arrow(40, 50, 75, 35, "move"), cursor(75, 35, "up"), badge("矢量")], title="矢量"),
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
                 [hero(40, 60, face=30), arrow(48, 55, 78, 40, "attack"), stick("R", 0.7, -0.3), badge("持续")], title="持续"),
        ], ["FPS", "双摇杆", "TPS"],
    ))
    c.append(case(
        "hold-block-channel", "hold", "举盾 / 站桩引导",
        "按住格挡；或读条引导（可慢走/不能动）。",
        [
            beat("按住格挡", "举盾姿态，减伤/弹反窗准备", "防住", "tps",
                 [hero(50, 55), badge("举盾")], title="盾"),
            beat("引导读条", "读条 UI，可规定能否移动", "读着", "moba",
                 [hero(50, 55), circle_ind(50, 55, 20, True), badge("引导")], title="引导"),
        ], ["动作RPG", "MOBA"],
    ))

    # ===== 十一、连招 =====
    c.append(case(
        "combo-light-chain", "combo", "轻攻击连段",
        "连点走出 1-2-3 段。",
        [
            beat("第一下", "第一段动画", "1", "tps", [hero(45, 55, face=10), badge("一段")], title="1"),
            beat("衔接窗内再按", "第二段", "2", "tps", [hero(48, 52, face=20), badge("二段")], title="2"),
            beat("再按", "第三段收招", "3", "tps", [hero(52, 50, face=30), badge("三段")], title="3"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "combo-heavy-branch", "combo", "轻重混按分支",
        "在链中插入重击走出另一条分支。",
        [
            beat("轻→重", "派生重击动画", "分支了", "tps",
                 [hero(48, 52, face=25), unit(70, 45, team="enemy"), badge("轻→重")], title="分支"),
        ], ["动作RPG"],
    ))
    c.append(case(
        "combo-recast-stages", "combo", "两段/多段再按",
        "第一次放前半段，再按放后半段。",
        [
            beat("第一次按大招", "前半段演出/位移", "上半段", "moba",
                 [hero(40, 55), badge("一段大招")], title="一段"),
            beat("提示窗内再按", "后半段爆发", "下半段", "moba",
                 [hero(55, 45), circle_ind(55, 45, 18, True), badge("二段")], title="二段"),
        ], ["MOBA"],
    ))
    c.append(case(
        "combo-dodge-attack", "combo", "闪避后接攻击",
        "闪避结束的专属窗内按攻击出派生。",
        [
            beat("闪避结束立刻按攻击", "闪攻专属动画", "漂亮", "tps",
                 [hero(50, 50, face=40), badge("闪攻")], title="闪攻"),
        ], ["动作RPG"],
    ))

    # ===== 十二、防御 =====
    c.append(case(
        "def-dodge", "defense", "翻滚 / 闪避",
        "按闪避键出无敌帧位移。",
        [
            beat("按闪避", "残影滚开", "躲过", "tps",
                 [hero(40, 55), path([(40, 55), (58, 48)], "move"), badge("闪避")], title="闪"),
        ], ["动作RPG", "TPS"],
    ))
    c.append(case(
        "def-perfect-dodge", "defense", "完美闪避",
        "在敌招判定前极短窗闪避，触发额外反馈。",
        [
            beat("红光提示时闪避", "慢镜/反击提示", "完美！", "tps",
                 [hero(45, 55), unit(70, 45, team="enemy"), badge("完美闪避")], title="完美"),
        ], ["蝙蝠侠", "动作RPG"],
    ))
    c.append(case(
        "def-parry-window", "defense", "弹反窗与反击",
        "敌招到来时按格挡；成功后可处刑/反击。",
        [
            beat("提示出现时按格挡", "弹反火花", "弹开！", "tps",
                 [hero(45, 55), unit(65, 48, team="enemy"), badge("弹反")], title="弹反"),
            beat("出现处刑提示再按", "处刑演出", "收掉", "tps",
                 [hero(50, 50), unit(60, 48, team="enemy"), badge("处刑")], title="处刑"),
        ], ["蝙蝠侠", "动作RPG"],
    ))

    # ===== 十三、环境 =====
    c.append(case(
        "env-throw", "environment", "捡起东西扔掉",
        "交互拾取，再瞄准扔出。",
        [
            beat("对可抓物按交互", "举在手上", "抓到了", "tps",
                 [hero(40, 55), badge("拾取")], title="抓"),
            beat("瞄准后扔出", "物体飞出砸中", "砸！", "tps",
                 [hero(40, 55), arrow(45, 50, 75, 40, "attack"), badge("投掷")], title="扔"),
        ], ["蝙蝠侠/蜘蛛侠", "动作RPG"],
    ))
    c.append(case(
        "env-wall-slam", "environment", "砸墙 / 推悬崖",
        "把敌人往环境特征上打。",
        [
            beat("朝墙方向攻击敌人", "敌人撞墙演出", "糊墙上", "tps",
                 [hero(40, 55), unit(55, 50, team="enemy"), building(70, 48), badge("砸墙")], title="砸墙"),
        ], ["蝙蝠侠", "动作RPG"],
    ))
    c.append(case(
        "env-destructible", "environment", "打可破坏物",
        "攻击木箱/墙开门路或掉资源。",
        [
            beat("攻击可破坏物", "物体碎裂", "砸开", "topdown",
                 [hero(40, 55), building(60, 50), arrow(45, 52, 58, 50, "attack"), badge("破坏")], title="破坏"),
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
            beat("对两名高阶单位下令合体", "融合成新单位", "合体！", "topdown",
                 [unit(40, 50, sel=True), unit(55, 50, sel=True), arrow(48, 50, 70, 45, "move"),
                  unit(70, 45, size=1.4), badge("合体")], title="合体"),
        ], ["SC2"],
    ))
    c.append(case(
        "army-vehicle", "army", "上车变载具手感",
        "交互上车后，摇杆/射击变成载具武器。",
        [
            beat("上车完成", "准星变为车炮；移动变车辆", "开炮车", "tps",
                 [building(50, 55), crosshair(60, 40), badge("载具")], title="载具"),
        ], ["TPS", "FPS"],
    ))

    # ===== 十五、手感变体 =====
    c.append(case(
        "habit-smart-vs-normal", "cast-habit", "智能施法 vs 先瞄后放",
        "同一技能：按下即打，或先出指示器再确认。",
        [
            beat("智能施法开启时按键", "立刻对向准星处出手", "快", "moba",
                 [hero(35, 60), circle_ind(70, 40, 14, True), badge("智能")], title="智能"),
            beat("普通模式按键", "先出指示器等确认", "稳", "moba",
                 [hero(35, 60), circle_ind(60, 45, 14, True), cursor(60, 45), badge("普通")], title="普通"),
        ], ["MOBA"],
    ))
    c.append(case(
        "habit-alt-self", "cast-habit", "Alt 对自己放",
        "按住 Alt 再按技能，强制以自己为目标。",
        [
            beat("Alt+技能", "技能打在自己身上", "自我施法", "moba",
                 [hero(50, 55), ring(50, 55, r=12, kind="buff"), badge("Alt自施")], title="自施"),
        ], ["MOBA"],
    ))
    c.append(case(
        "habit-shift-queue-cast", "cast-habit", "Shift 排队施法",
        "当前动作结束后再放这个技能。",
        [
            beat("Shift+技能点落点", "路点/技能队列出现标记", "排后面", "moba",
                 [hero(30, 60), path([(30, 60), (50, 50)], "move"), circle_ind(70, 40, 12, True),
                  badge("Shift排队")], title="排队"),
        ], ["SC2", "MOBA"],
    ))
    c.append(case(
        "habit-double-tap", "cast-habit", "双击技能",
        "双击触发与单击不同的变体（如闪回自己方向）。",
        [
            beat("双击技能键", "走出双击变体", "双击版", "moba",
                 [hero(50, 55), badge("双击")], title="双击"),
        ], ["MOBA"],
    ))

    # ===== 十六、放不了 =====
    c.append(case(
        "block-resource", "blocked", "蓝 / 怒气 / 弹药不足",
        "按下后明确拒绝，并提示缺什么。",
        [
            beat("资源不够时按技能", "图标闪红/飘字，不进瞄准", "放不出", "moba",
                 [hero(50, 55), badge("资源不足")], title="缺资源"),
        ], ["全品类"],
    ))
    c.append(case(
        "block-cooldown", "blocked", "冷却中",
        "CD 转圈，按下有拒绝音效/闪烁。",
        [
            beat("CD 未好转按技能", "图标仍在转 CD，拒绝施放", "再等等", "moba",
                 [hero(50, 55), badge("冷却中")], title="CD"),
        ], ["全品类"],
    ))
    c.append(case(
        "block-fog-illegal", "blocked", "雾中 / 类型不对",
        "看不见或对象非法时准星禁止态。",
        [
            beat("对不可见或非法目标确认", "禁止图标，技能不出去", "不行", "moba",
                 [hero(35, 60), cursor(70, 40), badge("禁止")], title="禁止"),
        ], ["RTS", "MOBA"],
    ))

    return c


def main():
    cases = build_cases()
    payload = {
        "title": "玩家动作体验图鉴",
        "subtitle": "只谈手怎么动、画面怎么变、爽点在哪——分镜式报菜名。不含技术分层。",
        "categories": [{"id": cid, "title": title} for cid, title in CATEGORIES],
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
    print(f"Wrote {OUT.relative_to(OUT.parents[2])}  cases={len(cases)}  beats={sum(len(x['beats']) for x in cases)}")


if __name__ == "__main__":
    main()
