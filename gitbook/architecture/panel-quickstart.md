# 面板快速上手：10 分钟给你的 mod 加一个血条

第三方 mod 作者入口。合同全文见[面板目录设计](panel-catalog-designs.md)，35 个现成设计见[典型案例全设计](panel-case-designs.md)。本文只回答一件事：**照抄什么、放哪、怎么跑**。

## 你要写的三个文件（全部数据，零 C#）

以"给英雄挂一个血条/蓝条面板"为例（= 仓库里的 fireball showcase，可直接对照 `mods/showcases/panel_fireball_shared/FireballSharedMod/assets/`）：

**① 面板模板** `assets/Panels/panel_templates.json`——声明"读什么"：

```jsonc
[{ "id": "panel.mygame.hero",
   "graph": "Graph.MyGame.HeroValues",        // 数据从哪张图来（见②）
   "pins": [
     { "name": "health", "key": "mygame.hero.hp", "mode": "realtime", "default": 0 },
     { "name": "mana",   "key": "mygame.hero.mp", "mode": "realtime", "default": 0 } ] }]
```

**② 值图** `assets/GAS/graphs.json`——计算与输出（节点随便组合，下面是最小例）：

```jsonc
[{ "id": "Graph.MyGame.HeroValues", "kind": "Query", "entry": "hp",
   "nodes": [
     { "id": "hp", "op": "LoadSelfAttribute", "attribute": "Health" },
     { "id": "mp", "op": "LoadSelfAttribute", "attribute": "Mana" } ],
   "controlEdges": [ { "from": "hp", "fromPort": "next", "to": "mp" } ],
   "valueEdges": [],
   "outputs": [
     { "id": "health", "destination": "Summary", "type": "Float", "source": "hp", "key": "mygame.hero.hp" },
     { "id": "mana",   "destination": "Summary", "type": "Float", "source": "mp", "key": "mygame.hero.mp" } ] }]
```

**③ 建面板的挂载图**（加进你地图已挂的 TriggerGraph，或新建）+ 地图声明：

```jsonc
// 图节点：scope(取英雄) → create(建面板) → show(亮出来)
// 值边：scope.value → create.source   ← 实体从这里进面板
// 地图 JSON：MapTriggerGraphs: [{ "graph": "<你的挂载图>", "scopeInstanceId": "<英雄实例id>" }]
```

**实体链路（新人最常问）**：地图的 `scopeInstanceId` 决定挂载图代表谁 → 图里 `LoadExplicitTarget` 取到该实体 → 值边喂给 `CreatePanel` 的 `source` 口 → 从此它就是这个面板的 **scope** → 每次求值时 scope 就是值图②里的 "self"——`LoadSelfAttribute` 读的就是它的属性。**同一个模板给小兵用就再调一次 CreatePanel 传小兵**，各读各的。

## 跑起来

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:<你的预设> --adapter raylib
```

预期：进游戏右上角即见面板，血蓝数字实时跟随；**图没跑/没注册/执行失败时显示 pins 里声明的 default，不报错不留空**——这是数据合同，不是 bug。

## 三个高频坑

1. **字段名**：`events` 块用 `eventId:`，`intents` 块用 `event:`——两处不同名，抄错装载即抛；
2. **图名即 id**：模板 `graph` 字段必须与图 JSON 的 `id` 完全一致（GraphIdRegistry 按名解析）；
3. **pins 的 key** 必须与图 `outputs[].key` 一致——对不上不报错，显示 default（数据缺失合同）；结构错误（字段拼错/pin 重复/mode 拼错）才是装载期抛。

## 换皮换主题（各一行）

```jsonc
// game.json：
"panelSkin": "markup",      // default|markup|compose|reactive|web
"panelTheme": "ink-wash"    // 水墨|fantasy|极简|cyber|bronze——CSS/图/字体正交于皮；cyber/bronze 另带九宫框与三宫条
```

## 下一步

- 35 个现成面板设计（线框+配置+验收）：[典型案例全设计](panel-case-designs.md)
- 交互（按钮→意图→admission）：#1015 交付后开放，设计已就绪
- 图节点全集：`gitbook/reference/graph-node-op-wiki/`
