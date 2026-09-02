# 可调用函数远景（Case E 驱动）

**状态**：远景合同 · 给下一任 agent **先出方案，未授权前不要大改 Core**。  
**进度入口**：仍只认 [图能力唯一入口](graph-capability-status.md)；本页是规矩与目标形状，不当第二张进度表。  
**现网落地切片**：PR #1444（框选预览自写 / InvokeGraph 调命中 / continuous 不强制 Query）只是台阶，不是终态。  
**相关**：[#1398](https://github.com/MightyBubble/Ludots/issues/1398)、Case E 配置结构 `mods/showcases/case_e_selection/CaseESelectionMod/docs/case-e-config-structure.html`、[FuncLib / ActionLib 合同](graph-funclib-actionlib-contract.md)。

---

## 1. 概述

### 玩家要看到什么

框选时：按下拖，框跟着走，框里的兵亮黄环；松手黄灭蓝留；加选/减选只动蓝环；敌人不进黄也不进蓝。  
作者只维护**一张命中函数**——拖拽每帧调、松手再调，不配第二套命中算法。

### 产品立场（第一性）

1. **函数等价**：可复用的图体都是函数——有声明入参、有返回（或声明副作用）。Score 返回分、Validation 返回是否通过、命中返回名单——**同一套调用模型**。  
2. **不靠 GraphKind 开特权通道**：禁止「因为 kind=Query 所以可以 continuous / 所以可以 outputs 代写」。Kind 可以继续当编译/策略标签，但**产品叙事与挂载面不得以 Kind 当能力开关**。  
3. **宿主调函数，函数不调宿主**：按下/抬起仍是 Trigger（边沿宿主）；拖拽存活期是调度宿主。宿主只做：接线 → 调用 →（可选）把返回交给后续节点。  
4. **预览写入归函数合同，不归 Core 偷写**：禁止 `GraphReturnWriter` / `outputs[]` 在 continuous 路径代写预览集。现网允许函数内 `DispatchCollectionEvent` 自写；远景仍可演进为「纯返回 + 统一按返回类型物化」，但**二选一必须写清，禁止两套并存**。  
5. **三套集合分离**：候选（命中入参）/ 预览（黄环）/ 已选中（蓝环）。

### 给下一任 agent 的任务（只做这一步）

**交付物 = 可评审的实施方案（issue / RFC / PR 描述均可），不是直接合大改。**

方案必须回答：

1. 函数登记长什么样（入参表、返回类型、副作用声明）？  
2. profile 存活期字段怎么从 `continuousQuery` 迁到「挂函数 id」？  
3. 宿主调用操作叫什么（延续 `InvokeGraph` 还是收成 `InvokeFunc`）？与 FuncLib 纯函数怎么划界？  
4. 预览集终态走「图内副作用」还是「返回物化」？迁移路径怎么失败关闭？  
5. Case E 资产改成什么样才算验收？哪些测试必须绿？  
6. 与 #1084/#1099「Query 准纯」旧关单叙事如何并存或改写？

未评审通过前：**禁止**新建平行 VM、禁止发明 `InvokeQuery`、禁止再给 profile 加第二套无引脚挂载对象。

---

## 2. 结构

```text
玩家输入 / context 存活
        │
   ┌────┴────┐
   │ 边沿宿主 │  Trigger：按下 / 抬起 / 点选
   │ 存活宿主 │  profile 挂「函数 id」（现网临时名 continuousQuery）
   └────┬────┘
        │  Invoke（宿主→函数）
        ▼
   命中函数（唯一）
     入参：候选 collection key + 矩形四角
     返回：TargetList（名单）
     副作用（现网）：DispatchCollectionEvent → case_e.box_hover
        │
        ├─ 拖拽每 tick：写/刷新预览集 → 黄环（成员变化）
        └─ 松手：同一返回名单 + 修饰语义 → selected → 蓝环
                 Deactivate boxing → 清空预览
```

| 层 | 职责 | 不该管 |
|----|------|--------|
| profile | 边沿挂哪些 Trigger；存活期调哪个函数 id | 命中算法；预览集合 key 硬编码在 profile |
| 命中函数 | 候选 ∩ 矩形 → 名单；（合同允许的）写预览 | 修饰键；Deactivate；写 selected |
| 抬起 Trigger | 调命中函数；按修饰写 selected；停 boxing | 再抄一份 ScreenRegion 链 |
| presenter | 订两个 collection key | 自己算命中 |

与 FuncLib 关系：

| 种类 | 纯度 | 例子 | 调用面 |
|------|------|------|--------|
| FuncLib 纯函数 | 无 Yield、无世界副作用 | 距离衰减、破韧判断 | 各 Kind / Action 可调 |
| 可调用业务函数 | 可声明副作用（如集合事件） | Case E 命中 | 宿主 Invoke；须登记副作用 |
| ActionLib | 可 Yield、可改世界 | 关卡动作 | 仅切片宿主 |

命中函数属**可调用业务函数**，不要硬塞进「FuncLib 必须 pure」而不声明副作用。

---

## 3. 详情

### 3.1 现网已具备（PR #1444 台阶，勿重做）

- `graph.case_e.box_hit` 图内 `DispatchCollectionEvent` 写 `case_e.box_hover`。  
- continuous tick 只 `Execute`，不 `ExecuteAndWrite` / 不靠 `outputs[]`。  
- continuous **不强制** `GraphKind.Query`；安装仍要求图内含 `DispatchCollectionEvent`。  
- `InvokeGraph` 可调 Query，并把 `TargetList` 拷回宿主；`box_commit` / `tap_commit` 已复用 `box_hit`。  
- 集合事件热路径：实例 scratch + `MapTrigger.CollectionEntityCount`。  
- 离开 boxing：按图内 `DispatchCollectionEvent` 的集合 key（EntityCollectionStore 空间）清空预览。

### 3.2 远景必须补齐

#### A. 图级入参表（作者面）

与 `outputs[]` 对等的顶层入参声明，例如：

```json
"inputs": [
  { "id": "candidates", "type": "EntityCollection", "key": "case_e.selectable" },
  { "id": "pressX", "type": "Float" },
  { "id": "pressY", "type": "Float" },
  { "id": "liveX", "type": "Float" },
  { "id": "liveY", "type": "Float" }
]
```

- 挂载 / Invoke 只传函数 id + 值边；禁止长期靠节点 imm / `LoadSelfAttribute` 藏入参当合同。  
- 现网属性/map var 可读，只能标成「迁移期凑合」，不能当终态。

#### B. 返回类型与副作用声明

方案必须**择一为主**（可保留迁移期双轨，但要写清废止日）：

| 选项 | 行为 | 利 | 弊 |
|------|------|----|----|
| **S1 图内副作用**（接近现网） | 函数声明 `sideEffects: [CollectionReplace:key]`，自己 `DispatchCollectionEvent` | 与现网一致；调度零物化 | 与「Query 准纯」旧叙事打架；热路径仍走事件 |
| **S2 纯返回 + 统一物化** | 函数只返回 TargetList；宿主/统一回写器按返回类型写入声明 key | 纯度清晰；好测 | 又接近曾被否决的「代写」——**必须**是「按返回类型合同物化」，禁止隐式偷写；continuous 不得再读 `outputs[]` 猜测 |

无论 S1/S2：禁止第三套「有时代写有时自写」无声明路径。

#### C. 宿主调用统一

- 产品名倾向：`InvokeFunc`（或保留 `InvokeGraph` 但语义改为宿主→任意已登记可调用函数）。  
- **禁止** `InvokeQuery` 这种暗示 Kind 特权的名字。  
- 调用后：返回名单进入后续值边；副作用按登记发生。  
- 与 FuncLib：`purity=pure` 走 FuncLib 目录；带集合副作用的走业务函数目录（名字以方案为准，SSOT 一处）。

#### D. 存活期调度字段

- 废止产品语义上的 `continuousQuery`（名字暗示 Query）。  
- 目标形状：与 `triggers[]` 同级，例如 `whileActive: { function: "graph.case_e.box_hit" }`（最终字段名以方案为准）。  
- 语义：context 存活 → 每 tick 调函数 id；离开 → 按函数副作用声明或返回物化 key 清空预览。  
- 迁移：读旧字段失败关闭或一次性改名工具；禁止静默双读。

#### E. Kind 动物园收敛（中长期）

- 编译器 / 策略表可暂时保留 `GraphKind`。  
- 产品与挂载：**用返回类型 + 副作用声明**区分能力，不用 Kind 当「能不能 continuous」的门闩。  
- 与已关单 #1084/#1099：方案须写清——是「改写关单叙事为：纯名单函数 vs 带副作用函数」，还是「命中迁出 Query 标签」；禁止假装没冲突。

### 3.3 明确不做

- 不新建第二套 VM / Query 网关。  
- 不把 `activeEntityViewKey` 当成预览出参。  
- 不在 commit/tap 再手抄矩形命中链。  
- 不为命中再双写 Press 属性当第二真相源（起角单一 map var / 属性，活角跟指针）。  
- 不在本远景里重做编辑器里程碑、Codegen、分层物理化（那些仍认图能力入口）。

---

## 4. 场景

### 4.1 拖拽框选（主路径）

1. 玩家在战斗 context 下按下。  
2. 开始 Trigger：写起角、刷新候选集、Activate boxing。  
3. boxing 存活：每 tick 调命中函数（候选 + 起角 + 当前指针）。  
4. 预览集变 → 黄环变。  
5. 松手 Trigger：再调同一命中函数 → 修饰语义写 selected → Deactivate → 黄环清空、蓝环留下。

### 4.2 点选

零位移：起角=活角，仍调**同一**命中函数；不写第三套点选几何。

### 4.3 加选 / 减选

修饰只影响「写 selected 的集合运算」；不改变命中函数本身。

### 4.4 敌我

候选集在框开始就过滤好；命中函数入参是候选 key，不是全图扫描（全图扫描若出现，只是某函数内部实现，不是合同默认入参）。

---

## 5. 边界

| 边界 | 合同 |
|------|------|
| 失败关闭 | 未知函数 id、入参缺边、副作用未登记却写集合、离开清空找不到集合 key → 抛错，禁止静默空跑 |
| 热路径 | 拖拽每 tick 禁止无界分配；事件载荷须有 count/scratch 合同 |
| 单命中体 | 拖拽与松手共用一个函数 id；验收禁止两套 ScreenRegion 链 |
| 文档 | 现网与远景必须分栏写；禁止把远景句子写成「已经落地」 |
| 合入顺序 | 先方案评审 → 再 Core 最小切片 → Case E 改资产当 UAT 车 → 最后改名废旧字段 |

---

## 6. UAT（Cucumber · 玩家视角）

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

自动化锚点（方案须保留或等价替换）：

- `CaseESelectionShowcaseAcceptanceTests`  
- continuous 安装：缺 `DispatchCollectionEvent`（或未来副作用声明）失败关闭  
- Invoke 调命中：commit/tap 无手抄 `ScreenRegionToEntities` 链

---

## 7. 下一任 agent 方案模板（请按此输出）

1. **概述**：选 S1 还是 S2（或分阶段），一句话为什么。  
2. **结构**：登记资产路径、profile 字段、Invoke 名、与 FuncLib 目录关系。  
3. **详情**：JSON 形状、加载校验、运行时序、迁移步骤。  
4. **场景**：对照本页 §4 逐条说明怎么走。  
5. **边界**：失败关闭表；与 #1084/#1099 冲突处理。  
6. **UAT**：本页 §6 + 计划改/增的测试名。  
7. **切片顺序**：建议的 PR 切分（每刀可独立验收）。  
8. **不做清单**：抄本页 §3.3，可加不可减关键禁令。

评审通过前只更新文档与 issue，不改 Core 大面。

---

## 8. 索引

| 材料 | 用途 |
|------|------|
| 本页 | 远景 + 方案任务书 |
| [图能力唯一入口](graph-capability-status.md) | 进度 / 开着的活 |
| [FuncLib / ActionLib](graph-funclib-actionlib-contract.md) | 纯函数 vs 可挂起动作 |
| Case E `case-e-config-structure.html` | Showcase 配置结构（现网+缺口） |
| PR #1444 | 已合入依赖链前的台阶实现 |
| Issue #1398 | 输入命令 / Case E 总题 |
