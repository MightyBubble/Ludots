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

旧书签 `/gas-graphs`、`/story-authoring` 仍打开对应房间。五个房间共用一张皮。底和字走常见 AI 编辑器那套深灰（近黑底、浅字）；线的三种颜色走同一张表里给图表用的蓝 / 暖黄 / 玫红。蓝是结构、数据和主按钮，黄是选项、控制流和时间，红是事件、结束和危险。滚动条也走这张表，没有另开一套紫或青。色值只写在 `index.css` 的 `--studio-*`，页面代码只引用变量，不再另抄一份。不另装聊天组件库：那是气泡和输入框，不是图画布。画布继续用已经在用的 `@xyflow/react`。

### 3.3 对话树（对照 FlowCanvas / NodeCanvas）

对话房是节点画布，不是长表单。合同仍是 `Dialogue/dialogues.json`，不另造一份树格式。节点样子对照 NodeCanvas 对话树：说话是一张，选项是另一张；线对照 FlowCanvas：从下口接到上口，线上不写字。

| 在 NodeCanvas / FlowCanvas 里 | 在这间房里 |
|------------------|------------|
| Say | 蓝头说话节点。台词走 `lineId`，说话人在 `Story/lines.json` |
| Multiple Choice | 单独的黄头节点，挂在该句下面。每个选项一个底边口。磁盘上仍写在该句的 `choices[]`，不另存一份 |
| Continue | 说话节点下口。没选项时接下句（`nextNode`）；有选项时接到黄头节点，这条线不写进 JSON |
| Finish | 没有出边，头是红的 |
| 线 | 下口到上口的弯线，约 3px，无标签。黄线是选项，蓝线是接下句 |
| Condition / Action | 不另做节点。条件/副作用是选项或进句上的蓝图 id |
| SubDialogue / Probability | 运行时没有这两类节点，画布也不发明 |

保存前按运行时规矩检查：入口存在、每句有 `lineId` 和 `presentationProfile`、选项 id 不重复。缺了当场失败，不会写出游戏加载会炸的树。右上「加一句」、检查器「+ 加选项 / 删除此选项 / 删除此句」和画布上删节点走同一份树；最后一句删不掉。增删先停在页面里，要写进磁盘得再按保存。

### 3.4 保存之后游戏看不看得见

工作室保存只改 Mod 磁盘上的 JSON。桥不会把正在玩的局热补进去。

| 房间 | 写到哪 | 游戏什么时候吃到 |
|------|--------|------------------|
| 蓝图 | `GAS/graphs.json` | 重开游戏（`ReloadConfigs` 也不重编图） |
| 行为树 / 状态机 | `AI/behavior_trees.json` / `AI/hfsm.json` | 重开，或局内触发 `ReloadConfigs(AI)` |
| 对话 | `Dialogue/dialogues.json` | 重开，或局内 `ReloadConfigs(Dialogue/Story)`（会清当前会话） |
| 时间轴 | `Sequencer/sequences.json` | 重开，或局内 `ReloadConfigs(Sequencer/Story)`。显示名写 `displayNameToken` |

保存成功时状态栏写清路径，并写明「正在玩的局要重开才会按这份走」。不假装点保存战场立刻变。

### 3.5 不在正门

| 入口 | 还在哪 |
|------|--------|
| 地图 / 寻路作者 | `/map`，`scripts/run-editor` 不再默认落到这里 |
| 面板皮肤 | `/ui-panel-authoring` |
| 场图层 | `scripts/run-field-editor.cmd` |

---

## 4. 场景

1. 我在仓库根跑 `./scripts/run-authoring-studio.sh`。过一会儿出现工作室窗口，五张卡片。
2. 我点「蓝图」，画布打开。顶栏仍能切到行为树。
3. 我点「对话」，看见蓝头说话节点和黄头选项节点连成的树。线上没有字。没有演出轨道当主目录。
4. 我点「时间轴」，只看到演出序列和轨道。
5. 我保存对话后，状态栏写出文件路径，并写明要重开游戏。
6. 桥没起来时，顶栏是红的「桥没连上」，首页也写明要用那条启动命令。

---

## 5. 边界

- 不把地图、面板、场编辑、技能数值塞进工作室卡片。
- 不新做 Electron / CEF 壳；桌面窗口复用本机 Chrome `--app=`。
- 不改蓝图 / 行为树 / 状态机 / 对话运行时合同。时间轴仍是现有 `Sequencer` 表单轨道，不是未合入的统一技能时间轴 PR。
- NodeCanvas 的 SubDialogue、Probability 没有运行时节点，画布不补这两类。
- 启动失败必须打出来。禁止端口不通还打开空页当成功。
- 五件工具的名单只写在 `authoringTools.ts`。色值只写在 `index.css` 的 `--studio-*`，`authoringTheme.ts` 只引用这些变量。启动脚本和文档不另列一套。
- 不把苹果系统色、自造青线、纯黑画布再叠进这张表。蓝图房的 `--gas-*` 也只是同一组变量的别名。

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

  Scenario: 对话是树
    Given 我在工作室
    When 我打开「对话」并选中一条对话
    Then 我看见蓝头的说话节点
    And 有选项的句子下面另有一张黄头选项节点
    And 选项从黄头下边拉出黄线，线上没有字
    And 画布底是近黑的深灰，线和节点头不是同一个颜色
    And 我打不开这页上的演出轨道当主目录
    And 侧栏和画布没有电光紫、青蓝那套旧色
    And 列表滚条是深灰细条，不是另一套颜色

  Scenario: 对话能加句、加选项、删句
    Given 我打开一条对话
    When 我点右上「加一句」
    Then 检查器出现新句，画布多一张蓝头
    When 我给这句加一个选项
    Then 这句下面出现黄头，检查器能删掉这个选项
    When 我再点「删除此句」
    Then 这句从画布和检查器里消失
    And 我没有点保存，磁盘上的对话文件没变

  Scenario: 行为树能加节点再删掉
    Given 我打开行为树并选中一条树
    When 我点右上「添加节点」并选 Action
    Then 画布多一个叶子，检查器能改它的 kind
    When 我点「删除此节点」
    Then 这个叶子从画布上消失
    And 根节点删不掉
    And 我没有点保存

  Scenario: 蓝图检查器能删节点和连线
    Given 我打开蓝图并选中一张函数图
    When 我选中一个节点并点「删除此节点」
    Then 这个节点和连到它的线从画布上消失
    And 我没有点保存

  Scenario: 时间轴是另一间房
    Given 我在工作室
    When 我打开「时间轴」
    Then 我编辑演出序列的镜头和字幕轨

  Scenario: 保存进游戏要重开
    Given 我改了一句对话并点保存
    Then 磁盘上的 Dialogue/dialogues.json 已更新
    And 页面写明正在玩的局要重开才会按新树走

  Scenario: 桥没连上不能假装能保存
    Given 前端开着但桥没起
    Then 顶栏写「桥没连上」
    And 我保存时失败，不会写出半份图
```
