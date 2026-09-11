# 作者工作室：蓝图 / 行为树 / 状态机 / 对话 / 时间轴

一键打开这五件作者工具。地图、面板、场图层、技能数值不走这扇门。

---

## 1. 概述

作者要改关卡事件、行为树、状态机、对话和时间轴。以前要自己起桥、起前端，再记五条网址；打开默认还是地图编辑器。

现在正门是 **作者工作室**。一条命令拉起桥和页面，顶栏五个房间来回切。蓝图仍写 `GAS/graphs.json`；行为树 / 状态机仍写 `AI/*.json`；对话写 `Dialogue/` 与 `Story/lines.json`；时间轴写 `Sequencer/sequences.json`。不另起一套编辑器内核。

| 房间 | 打开后干什么 |
|------|----------------|
| 蓝图 | 关卡事件、查询、函数图画布 |
| 行为树 | 改树的结构，叶子进蓝图 |
| 状态机 | 改状态和转移，生命周期进蓝图 |
| 对话 | 台词、说话的人、选项 |
| 时间轴 | 镜头、字幕、过场轨道 |

---

## 2. 结构

```text
scripts/run-authoring-studio.sh|.cmd
        │
        ├─ Ludots.Editor.Bridge :5299   /health 探活
        └─ Editor.React :5173
                │
                └─ /                工作室首页（五张卡片）
                   /blueprint       蓝图（旧址 /gas-graphs 仍认）
                   /bt-editor       行为树
                   /fsm-editor       状态机
                   /dialogue        对话（旧址 /story-authoring 仍认）
                   /timeline        时间轴
                   /map            地图编辑（不在正门卡片上）
```

Chrome / Edge / Chromium 在本机有显示器时用 `--app=` 开独立窗口。没有这类浏览器就开普通标签，并打出说明。没有显示器（云端、CI）只打印地址，不装假窗口。

---

## 3. 详情

### 3.1 一键启动

Linux / macOS：

```bash
chmod +x scripts/run-authoring-studio.sh   # 首次
./scripts/run-authoring-studio.sh
```

Windows：

```powershell
.\scripts\run-authoring-studio.cmd
```

`scripts/run-editor.cmd` / `scripts/run-editor.sh` 走同一条路径。

| 开关 | 行为 |
|------|------|
| （默认） | 起桥 + 页面，等 `/health` 和前端都通，再开窗口 |
| `--no-browser` | 只起服务，打印地址 |
| `--headless` | 后台起、不开窗口；停用 `scripts/stop-editor.ps1` 或 pid 文件 |

桥或前端 45 秒内起不来：命令失败，打错误日志。已经在跑则直接开窗口，不另起一份。缺 `dotnet` 或 `npm` 当场失败。

### 3.2 正门卡片

首页只列出上面五张卡片。顶栏同一张表，切房间不丢工作室壳。桥没连上时顶栏写「桥没连上」，保存走原失败关闭，不假装写盘。

旧书签 `/gas-graphs`、`/story-authoring` 仍打开对应房间。

### 3.3 不在正门

| 入口 | 还在哪 |
|------|--------|
| 地图 / 寻路作者 | `/map`，`scripts/run-editor` 不再默认落到这里 |
| 面板皮肤 | `/ui-panel-authoring` |
| 场图层 | `scripts/run-field-editor.cmd` |

---

## 4. 场景

1. 我在仓库根跑 `./scripts/run-authoring-studio.sh`。过一会儿出现工作室窗口，五张卡片。
2. 我点「蓝图」，画布打开。顶栏仍能切到行为树。
3. 我点「对话」，看到台词和对话树，没有演出轨道列表。
4. 我点「时间轴」，只看到演出序列和轨道。
5. 桥没起来时，顶栏是红的「桥没连上」，首页也写明要用那条启动命令。

---

## 5. 边界

- 不把地图、面板、场编辑、技能数值塞进工作室卡片。
- 不新做 Electron / CEF 壳；桌面窗口复用本机 Chrome `--app=`。
- 不改蓝图 / 行为树 / 状态机 / 对话运行时合同。时间轴仍是现有 `Sequencer` 表单轨道，不是未合入的统一技能时间轴 PR。
- 启动失败必须打出来。禁止端口不通还打开空页当成功。
- 五件工具的名单只写在 `authoringTools.ts`，启动脚本和文档不另列一套。

---

## 6. UAT

```gherkin
Feature: 作者一键进工作室

  Scenario: 我双击启动就能看见五个房间
    Given 我在仓库根
    When 我运行 scripts/run-authoring-studio.sh（或 .cmd）
    Then 桥在 :5299 探活成功
    And 浏览器或应用窗口打开作者工作室首页
    And 我看见蓝图、行为树、状态机、对话、时间轴五张卡片
    And 我看不见地图、面板、场编辑作为正门卡片

  Scenario: 我从工作室走进蓝图
    Given 工作室首页已打开
    When 我点「蓝图」
    Then 我进入蓝图画布
    And 顶栏仍能切到行为树和对话

  Scenario: 对话和时间轴是两间房
    Given 我在工作室
    When 我打开「对话」
    Then 我编辑台词和对话树
    And 我打不开这页上的演出轨道当主目录
    When 我再打开「时间轴」
    Then 我编辑演出序列的镜头和字幕轨

  Scenario: 桥没连上不能假装能保存
    Given 前端开着但桥没起
    Then 顶栏写「桥没连上」
    And 我保存时失败，不会写出半份图
```
