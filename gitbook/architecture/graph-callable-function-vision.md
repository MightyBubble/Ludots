# 可调用函数远景（Case E 驱动）

**给谁看**：下一任要出实施方案的人。  
**先做什么**：按本页第 7 节交方案；评审通过前不要大改 Core。  
**进度**：只看 [图能力唯一入口](graph-capability-status.md)。本页定目标，不当第二张进度表。  
**现网台阶**：PR #1444 已做到「命中图自己写预览、InvokeGraph 能调命中、continuous 不再强制 Query」。那是台阶，不是终态。  
**对照**：[#1398](https://github.com/MightyBubble/Ludots/issues/1398)、`mods/showcases/case_e_selection/CaseESelectionMod/docs/case-e-config-structure.html`、[FuncLib / ActionLib 合同](graph-funclib-actionlib-contract.md)。

---

## 1. 概述

### 玩家看到什么

按下拖，框跟着走，框里的兵亮黄环；松手黄灭蓝留；加选/减选只动蓝环；敌人不进黄也不进蓝。

作者只维护**一张命中函数**：拖拽每帧调一次，松手再调一次，不要第二套命中算法。

### 合同要点

1. 可复用的图体都是函数：要有入参声明，有返回，或写明副作用。打分返回分、校验返回是否通过、命中返回名单——调用方式同一套。  
2. 挂载和产品说明不要靠 `GraphKind` 开后门。禁止「因为 kind=Query 所以可以 continuous / 所以可以用 outputs 代写」。Kind 可以留作编译/策略标签。  
3. 边沿（按下/抬起）用 Trigger 当宿主；拖拽存活期用调度当宿主。宿主只接线、调用，必要时把返回交给后面的节点。函数不回头调宿主。  
4. continuous 路径禁止 `GraphReturnWriter` / `outputs[]` 代写预览集。现网是函数里 `WriteCollection` 自己写。终态在「图内副作用」和「纯返回再按类型写入」里**只留一条**，写清迁移，禁止两套并存。  
5. 三套集合分开：候选（命中入参）、预览（黄环）、已选中（蓝环）。

### 下一任交什么

一份可评审方案（issue / RFC / PR 描述均可）。方案要答：

1. 函数怎么登记（入参表、返回类型、副作用）？  
2. `whileActive`（已替 continuousQuery）还要不要再改产品名？  
3. 宿主调用叫 `InvokeGraph` 还是收成 `InvokeFunc`？和 FuncLib 纯函数怎么分开？  
4. 预览集走图内副作用，还是纯返回再写入？缺合同怎么失败关闭？  
5. Case E 资产改成什么样算过？哪些测试必须绿？  
6. 和 #1084 / #1099「Query 准纯」旧说法怎么并存或改写？

评审前不要做：第二套 VM、名叫 `InvokeQuery` 的操作、再给 profile 加一套无引脚挂载。

---

## 2. 结构

```text
玩家输入 / context 存活
        │
   ┌────┴────┐
   │ 边沿宿主 │  Trigger：按下 / 抬起 / 点选
   │ 存活宿主 │  profile 挂函数 id（现网字段 whileActive）
   └────┬────┘
        │  宿主 → 函数
        ▼
   命中函数（唯一）
     入参：候选 collection key + 矩形四角
     返回：TargetList
     现网副作用：WriteCollection → case_e.box_hover
        │
        ├─ 拖拽每 tick：刷新预览集 → 黄环
        └─ 松手：同一名单 + 修饰 → selected → 蓝环
                 停 boxing → 清空预览
```

| 层 | 管什么 | 不管什么 |
|----|--------|----------|
| profile | 边沿挂哪些 Trigger；存活期调哪个函数 id | 命中算法；把预览集合 key 写死在 profile |
| 命中函数 | 候选 ∩ 矩形 → 名单；合同允许时写预览 | 修饰键；停 boxing；写 selected |
| 抬起 Trigger | 调命中函数；按修饰写 selected；停 boxing | 再抄一份屏幕矩形命中链 |
| presenter | 订预览 / 已选中两个 collection key | 自己算谁进框 |

和 FuncLib 怎么摆：

| 种类 | 约定 | 例子 | 谁来调 |
|------|------|------|--------|
| FuncLib 纯函数 | 无 Yield、不改世界 | 距离衰减、破韧判断 | 各 Kind / Action |
| 可调用业务函数 | 可登记副作用（如写集合） | Case E 命中 | 宿主 Invoke |
| ActionLib | 可 Yield、可改世界 | 关卡动作 | 仅切片宿主 |

命中函数按**可调用业务函数**登记副作用，不要塞进「FuncLib 必须 pure」却不声明写集合。

---

## 3. 详情

### 3.1 PR #1444 已经有的（别重做）

- `graph.case_e.box_hit` 里 `WriteCollection` 写 `case_e.box_hover`。  
- continuous 每 tick 只 `Execute`，不用 `ExecuteAndWrite`，不靠 `outputs[]`。  
- continuous 不强制 `GraphKind.Query`；安装仍要求图里有 `WriteCollection`。  
- `InvokeGraph` 可调 Query，并把 `TargetList` 拷回宿主；`box_commit` 已调 `box_hit`（点选=零位移抬起，无 tap_commit）。  
- 集合事件：实例 scratch + `MapTrigger.CollectionEntityCount`。  
- 离开 boxing：按图里 `WriteCollection` 的集合 key（`EntityCollectionStore` 空间）清空预览。

### 3.2 还要补的

#### A. 图顶层入参表

和 `outputs[]` 对称，例如：

```json
"inputs": [
  { "id": "candidates", "type": "EntityCollection", "key": "case_e.selectable" },
  { "id": "pressX", "type": "Float" },
  { "id": "pressY", "type": "Float" },
  { "id": "liveX", "type": "Float" },
  { "id": "liveY", "type": "Float" }
]
```

挂载 / Invoke 只传函数 id 和值边。节点 imm、`LoadSelfAttribute` 可以暂时用，只能标成迁移期做法，不能当终态合同。

#### B. 预览写入：二选一

方案定一个主路径；若短暂双轨，写明废止条件。

| 选项 | 怎么做 | 好处 | 代价 |
|------|--------|------|------|
| **S1 图内副作用**（接近现网） | 函数声明要写哪些集合；自己 `WriteCollection` | 和现网一致；调度不用代写 | 和「Query 准纯」旧说法冲突；每 tick 仍走事件 |
| **S2 纯返回再写入** | 函数只返回 `TargetList`；调用方按返回类型写入声明好的 key | 纯度清楚，好测 | 必须是「按返回类型合同写入」，禁止猜着写；continuous 不得再扫 `outputs[]` |

禁止第三种：有时自写、有时代写、文档说不清。

#### C. 宿主怎么调

- 名字倾向 `InvokeFunc`；也可以保留 `InvokeGraph`，但语义改成「调已登记的可调用函数」。  
- 不要用 `InvokeQuery`。  
- 调完：名单进后续值边；副作用按登记发生。  
- `purity=pure` 进 FuncLib；带集合副作用的进业务函数目录（目录名以方案为准，全仓库一处 SSOT）。

#### D. 存活期字段

- 已废 `continuousQuery`；现网字段是 `whileActive`。  
- 目标：和 `triggers[]` 同级，例如 `whileActive: { function: "graph.case_e.box_hit" }`（最终字段名以方案为准）。  
- 行为：context 还在 → 每 tick 调函数；离开 → 按副作用声明或返回写入的 key 清空预览。  
- 迁移：旧字段失败关闭，或一次性改名；禁止默默两套都认。

#### E. Kind 标签（中长期）

- 编译器 / 策略表可以暂时留着 `GraphKind`。  
- 挂载面用「返回类型 + 副作用声明」决定能不能 continuous，不用 Kind 当门闩。  
- 方案必须写清怎么处理 #1084 / #1099：改关单说法，或把命中迁出 Query 标签。不能假装没冲突。

### 3.3 不做

- 不新建第二套 VM / Query 网关。  
- 不把 `activeEntityViewKey` 当预览出参。  
- 不在 commit / tap 再手抄矩形命中链。  
- 不为命中再双写 Press 当第二真相源（起角一份，活角跟指针）。  
- 不在本远景里重做编辑器里程碑、Codegen、分层物理化（那些仍看图能力入口）。

---

## 4. 场景

### 4.1 拖拽框选

1. 战斗 context 下按下。  
2. 开始 Trigger：写起角、刷新候选集、打开 boxing。  
3. boxing 存活：每 tick 调命中函数（候选 + 起角 + 当前指针）。  
4. 预览集变，黄环变。  
5. 松手 Trigger：再调同一命中函数 → 按修饰写 selected → 停 boxing → 黄环清空、蓝环留下。

### 4.2 点选

零位移时起角等于活角，仍调同一张命中函数，不另写点选几何。

### 4.3 加选 / 减选

修饰只改变写 `selected` 的集合运算，不改命中函数。

### 4.4 敌我

候选集在框开始就过滤好。命中函数吃的是候选 key，不是默认全图扫描。若某函数内部扫全图，那是实现细节，不是合同默认入参。

---

## 5. 边界

| 边界 | 要求 |
|------|------|
| 失败关闭 | 未知函数 id、入参缺边、未登记却写集合、清空时找不到集合 key → 抛错，禁止空跑蒙混 |
| 热路径 | 拖拽每 tick 禁止无界分配；事件载荷要有 count / scratch 约定 |
| 单命中体 | 拖拽和松手共用一个函数 id；验收时不得再有第二套屏幕矩形命中链 |
| 文档 | 现网和远景分开写；远景句子不得写成已经落地 |
| 合入顺序 | 方案评审 → Core 最小切片 → Case E 改资产验收 → 最后改名废旧字段 |

---

## 6. UAT（玩家话）

```gherkin
Feature: 框选只靠一张命中函数

  Scenario: 拖拽时黄环跟框走且不提前变蓝
    Given 我进入 Case E 战场且指挥官已挂战斗 context
    When 我按下并拖出盖住两名己方士兵的框
    Then 我应看到屏幕框跟着指针走
    And 框内士兵出现黄环
    And 框外士兵没有黄环
    And 此时还没有蓝环定选

  Scenario: 松手后黄灭蓝留
    Given 我正在拖拽且框内已有黄环士兵
    When 我松开指针
    Then 黄环消失
    And 原框内士兵留下蓝环
    And 屏幕框不再绘制

  Scenario: 加选与减选只动蓝环
    Given 我已框选两名士兵为蓝环
    When 我按住加选修饰再框第三名
    Then 三名士兵都是蓝环
    When 我按住减选修饰再框其中一名
    Then 那一名蓝环取消且其余蓝环仍在

  Scenario: 敌人永不入选
    Given 战场上有敌方单位落在我的框内
    When 我完成一次框选
    Then 敌方单位既无黄环也无蓝环

  Scenario: 作者只改一张命中函数
    Given 拖拽与松手配置都指向同一命中函数 id
    When 我只修改该函数的矩形命中连线
    Then 拖拽预览与松手定选的命中集合一起变化
    And 我不需要再维护第二份 commit/tap 命中链
```

自动化（方案须保留或写明等价替换）：

- `CaseESelectionShowcaseAcceptanceTests`  
- continuous 安装：缺 `WriteCollection`（或未来的副作用声明）要失败  
- commit / tap：没有手抄的 `ScreenRegionToEntities` 链

---

## 7. 方案怎么交

按这个顺序写，缺一项就退回：

1. **概述**：选 S1 还是 S2（或分阶段），一句话说理由。  
2. **结构**：登记资产路径、profile 字段、Invoke 名、和 FuncLib 目录的关系。  
3. **详情**：JSON 形状、加载校验、运行顺序、迁移步骤。  
4. **场景**：对照本页第 4 节逐条说明。  
5. **边界**：失败关闭表；#1084 / #1099 怎么处理。  
6. **UAT**：本页第 6 节 + 要改/增的测试名。  
7. **切片**：建议几刀 PR，每刀能单独验收。  
8. **不做**：以本页 3.3 为底，可以加，不能删硬禁令。

评审通过前只动文档和 issue。

---

## 8. 材料索引

| 材料 | 用途 |
|------|------|
| 本页 | 远景和方案任务书 |
| [图能力唯一入口](graph-capability-status.md) | 进度、还开着的活 |
| [FuncLib / ActionLib](graph-funclib-actionlib-contract.md) | 纯函数和可挂起动作 |
| Case E `case-e-config-structure.html` | Showcase 现网配置结构 |
| PR #1444 | 当前台阶实现 |
| Issue #1398 | Case E / 输入命令总题 |
| `mods/showcases/case_e_selection/CaseESelectionMod/docs/NEXT-AGENT-BRIEF.md` | 短任务条 |
