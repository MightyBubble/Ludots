# 查询图集合输出：成员身份 · 输出类型 · 面板只消费

本页是 **Query 图「集合类输出」扩展** 的设计 SSOT。  
下游消费者：[面板视图投影](panel-view-projection.md)（G12）、指令/聚合旁路最终应收口到本类型表。  
现状单出口合同见编辑器侧 [gr-09 输出](../reference/mod-editor-prd/config/gr-09-outputs.md)（今日仅 `Summary` + `EntityCollection`）。

**原则**：不为面板业务改玩法规则；先扩展 **查询图引脚与输出数据类型**；面板 `subject` 只声明「我解哪种成员」，并与图写出的集合类型做相容校验。

---

## 1. 概述

今日查询图集合出口只有一种：**实体有序列表**（`EntityCollection` ← `TargetList`）。  
名册、选中源、附近敌人都能走这条。但作者真实需要远不止「一串实体」：

| 作者想圈什么 | 今天硬塞 EntityCollection？ |
|---|---|
| 身上正在生效的 buff（有剩余时间） | 勉强：效果实例往往已是实体 |
| 「有哪些可用的效果模板」 | **不行**：模板不是实例 |
| 技能栏 8 个槽 | **不行**：槽位不是实体 |
| 身上枚举到的 tag | **不行**：tag id 不是实体 |
| 背包里的物品实例 / 图鉴里的物品定义 | 实例可塞；**定义 id 不行** |
| 任务实例 / 活动实例 | 多已物化为实体，可走实体集 |
| 科技树节点 | 多为定义/节点 id，不是实体 |

若不先把 **输出类型** 设计清楚，面板侧会出现三种坏味道：

1. 为 list 方便伪造假实体；  
2. 旁路扫容器、正式图写不出同款集合（双轨）；  
3. `subject` 枚举膨胀，却没有对应的图写出种类。

本设计回答五句话：

1. 集合成员的 **身份** 有哪几类；  
2. 查询图应新增哪些 **destination / type**；  
3. 面板（及其他消费者）如何 **只消费、不发明类型**；  
4. **复合结构**（嵌套名单、反查名单、聚合展示）如何仍用配置表达，而不是硬编码业务 case；  
5. 未接线类型如何 **fail-closed**。

落地实现分阶段；**未落地的类型写了配置 → 装载或绑定时 fail-closed**，禁止静默降级成实体集。

---

## 2. 结构

```text
查询图（Query）
  │  outputs[]
  ├─ Summary              → 标量 / 单实体键值
  └─ Collection*          → 有序成员袋（本设计扩展点）
        │
        ▼  collectionKey + 成员身份
运行时集合库（按类型分仓或带判别的统一仓）
        │
        ▼  消费者只读
  面板 collections[].template（subject 相容）
  指令聚合 / Web dataplane / 其他系统（逐步收口）
```

### 2.1 两根轴（必须同时声明）

```text
轴 A · 成员身份（MemberIdentity）
  InstanceEntity     世界里已物化的实体实例
  DefinitionId       定义/模板表中的稳定 id（未物化或与实例分离）
  SlotIndex          宿主上的槽位下标（如技能栏）
  TagId              玩法标签 id
  NodeId             进度/树节点等非实体节点 id
  （预留）ChoiceId   对话选项等嵌套 UI 成员——不进本页顶层表，见对话合同

轴 B · 域标签（Domain）——说明这袋成员「属于哪张表」
  Entity | Effect | Ability | Item | Task | Activity | Tag | Progression | …
```

**同一轴 A 可服务多域**：例如 `DefinitionId + Effect` 与 `DefinitionId + Item` 都是「模板 id 集」，但域不同，禁止混袋。

### 2.2 与面板的关系（消费者）

```text
图写出 Collection(Domain=Effect, Identity=InstanceEntity)
        ↓
面板 collections 引用该 collectionKey
        ↓
元素模板 subject 必须与 (Domain, Identity) 相容
        ↓
每一行：把该成员设为元素图的求值 scope（透传）
```

面板 **不** 决定集合里有谁、如何排序；那是查询图的事。

### 2.3 复合结构（配置轴，不是业务硬编码）

除「一张平面名单」外，作者常见三种 **编排形态**。它们 **不** 新增玩法规则，只组合已有集合类型 + **显式引脚接线**：

```text
① 嵌套（Compound / Nested）
   单位详情 ──内嵌──► 该单位的技能槽名单

② 反查（Reverse / Holders）
   技能图标 ──内嵌──► 「谁拥有此技能」的实体名单

③ 聚合（Aggregate present）
   输入仍是完整集合袋；画面配置成首位图标 + 总数
```

### 2.4 复合数据一律显式接线（强类型）

跨层级的名单/标量 **禁止靠同名 key 猜**。合同要求：

```text
父级空间 graph
  outputs[]  ──额外写出──►  类型化引脚（Summary 或 Collection*）
        │
        ▼  子模板 inputs[] 明文声明来源 + 期望类型
子级（元素模板 / 内嵌控件）
  inputs 满足后 → 再跑子 graph / 绑定 list·聚合
```

| 规则 | |
|---|---|
| **明文** | 子级必须写 `inputs[]`：从父级哪个 output / 哪个集合键取数 |
| **类型** | 每条 input 声明期望的集合类型或标量 kind；与父级 output 的 destination/type **装载期强校验** |
| **方向** | 数据只从「已求值的父级空间输出」流入子级；子级不隐式扫全局 Store 碰运气 |
| **再输出** | 子 graph 仍可写出自己的集合（如 holders），供自己的 list 绑定；那是子级自己的 output，不是偷父级的 |

嵌套与反查在面板 JSON 上都可能长得很像（都有子 `collections`）；差别在 **inputs 从哪来、子图约束是什么**——见 §3.7。

---

## 3. 详情

### 3.1 今日基线（不可破坏）

| destination | type | 含义 |
|---|---|---|
| `Summary` | Bool/Int/Float/Entity | 单值写入 |
| `EntityCollection` | `TargetList` | `(owner, collectionKey)` → 有序 **实体实例** |

`EntityCollection` **语义收窄为**：成员身份 = `InstanceEntity`，域默认 = `Entity`（单位/道具实例等凡「就是一个 Entity」）。  
效果实例若本身是 Entity，**可以** 仍写入 `EntityCollection`，但推荐在扩展落地后改用带域标签的效果实例集，避免消费者靠猜测区分「人」和「buff 实体」。

### 3.2 目标输出形态（合同名，实现可映射枚举）

下列名为 **合同标识**；装载期封闭集合。未实现项保持「可解析、不可绑定」。

| 合同 destination（示意） | 成员身份 | 域示例 | 成员载体 |
|---|---|---|---|
| `EntityCollection`（已有） | InstanceEntity | Entity（默认） | `Entity` |
| `EffectInstanceCollection` | InstanceEntity | Effect | 效果实例 `Entity` |
| `EffectTemplateCollection` | DefinitionId | Effect | `EffectTemplate` id |
| `ItemInstanceCollection` | InstanceEntity | Item | 物品实例 `Entity` |
| `ItemDefinitionCollection` | DefinitionId | Item | `ItemDefinition` id |
| `AbilitySlotCollection` | SlotIndex | Ability | 槽位下标（相对宿主） |
| `AbilityDefinitionCollection` | DefinitionId | Ability | 技能定义 id |
| `TagIdCollection` | TagId | Tag | tag id（可附带层数走并行 Summary 或成员表面） |
| `TaskInstanceCollection` | InstanceEntity | Task | 任务实例 `Entity` |
| `ActivityInstanceCollection` | InstanceEntity | Activity | 活动实例 `Entity` |
| `ProgressionNodeCollection` | NodeId | Progression | 进度节点 id |

> 实现策略（二选一，落地 PR 拍板，合同不绑死）：  
> **A.** 每域独立 Store + destination；  
> **B.** 统一 `TypedCollectionStore`，条目带 `(Domain, Identity, payload)`。  
> 无论 A/B，**图 outputs 的 destination/type 必须能区分上表**，禁止「全是 EntityCollection」。

### 3.3 Effect：必须拆成两种查询结果（样板）

这是本设计的样板域——其它域按同构复制。

#### 3.3.1 身上正在生效的 buff（实例）

- **作者意图**：打开单位面板，看到当前 debuff/buff，剩余时间在跳。  
- **图**：从宿主的活跃效果容器枚举 → 写出 **效果实例实体** 有序袋。  
- **输出**：`EffectInstanceCollection`（过渡期可暂用 `EntityCollection`，但文档与面板 subject 仍按 Effect 实例语义验收）。  
- **元素**：`subject` 对齐效果 **实例**；元素图读剩余时间、层数、驱散标记等；表面身份优先模板显示名（定义表），不是单位 `Name` 组件。

#### 3.3.2 效果模板列表（定义）

- **作者意图**：图鉴/调试器/掉落表预览——列出「有哪些效果模板」，没有「剩余 3 秒」。  
- **图**：从注册表或作者配置的 id 列表圈选 → 写出 **模板 id** 有序袋。  
- **输出**：`EffectTemplateCollection`（`DefinitionId`）。  
- **元素**：`subject` 对齐效果 **模板**；元素图读默认时长、标签、图标 token；**禁止** 去读实例 RemainingTicks。

两种结果 **不得** 共用同一个 destination 而不带身份判别。

### 3.4 面板 subject 相容表（消费者合同）

面板元素 `subject` 是 **成员求值视角**，必须与集合的 `(Domain, Identity)` 相容。示意（落地时写入 [panel-view-projection](panel-view-projection.md) 正表）：

| 元素 subject（示意名） | 可消费的集合 |
|---|---|
| `Entity` | `EntityCollection`（域 Entity） |
| `EffectInstance` | `EffectInstanceCollection` |
| `EffectTemplate` | `EffectTemplateCollection` |
| `ItemInstance` / `ItemDefinition` | 对应 Item 两袋 |
| `AbilitySlot` / `AbilityDefinition` | 对应 Ability 两袋 |
| `Tag` | `TagIdCollection` |
| `Task` / `Activity` | 对应实例袋 |
| `ProgressionNode` | `ProgressionNodeCollection` |

旧预留名 `Ability` / `Task` 在落地时 **拆清身份**（槽 vs 定义、实例 vs 定义），避免一个单词对应两种成员。

不相容 → **装载或绑定失败**，指出集合类型与 subject。

### 3.5 控件与 present（消费形态）

控件（label / progressBar / badge / list / 日后图标冷却环）与 subject **正交**。  
缺图标不等于缺 subject；缺集合类型才是类型轴范围。

同一 `collections[]` 绑定可配置不同 **present**（正式字段落在面板合同）：

| present（示意） | 画面 | 数据 |
|---|---|---|
| `list` / `grid` | 逐行/逐格展开成员 | 读窗口或全量成员袋 |
| `aggregate` | 配置化聚合，例如首位图标 + `count` | 仍绑同一集合；`count` 来自袋 `TotalCount` 或并行 Summary |

`aggregate` **不是** 新的 destination，也 **不是** 「只输出第一个实体」的特殊集合类型。

### 3.6 查询图侧要补的能力（实现清单，非本页实现）

1. **outputs 扩展**：destination / type 封闭集按上表增长；编译期校验。  
2. **枚举 op / 子图**：从活跃效果、库存、技能槽、Tag 计数等 **读域容器 → 填集合**（图能力，不是面板能力）。  
3. **回写器**：按 destination 写入对应 Store；容量、替换语义对齐今日 EntityCollection（整表替换）。  
4. **复合查询**：支持「以当前成员为 scope」再写出子集合（嵌套 / 反查）；见 §3.7。  
5. **禁止**：为写集合而改 Effect/Ability 生命周期规则；禁止模板 id 伪造成假实体。

### 3.7 复合结构详解（配置合同）

下列例子只说明 **形状**；正式字段名以落地 schema 为准。**禁止** 在引擎里写死「EntityInfo」「AbilityIcon」「ItemStack」三类特判控件。

#### 3.7.1 嵌套：单位详情里挂技能名单

- **作者意图**：一个单位信息面板上，除血条外，还列出该单位可用技能。  
- **数据路径（显式）**：  
  - 单位元素 `subject=Entity`；其 graph（或以该实体为 owner 的查询段）**output** `AbilitySlotCollection`；  
  - 元素 `collections` 绑定该 output 的 key；或父宿主先写出「焦点单位技能袋」再经 `inputs` 传入——**来源必须在配置写明**。  
- **合同要点**：子袋类型与技能元素 subject 相容；过滤排序仍在图内。

```jsonc
{
  "id": "panel.unit.info",
  "subject": "Entity",
  "graph": "Graph.Unit.Info",
  // Graph.Unit.Info outputs: AbilitySlotCollection key=unit.info.abilities
  "collections": [
    {
      "name": "abilities",
      "collectionKey": "unit.info.abilities", // 明文：来自本元素 graph output
      "source": "selfGraph",                  // 示意判别；与 from.parent 互斥
      "template": "panel.ability.slot"
    }
  ],
  "layout": {
    "controls": [
      { "type": "label", "bind": "displayName" },
      { "type": "list", "bind": "abilities", "present": "grid" }
    ]
  }
}
```

#### 3.7.2 反查：技能图标上挂「谁会这招」（显式 inputs）

- **作者意图**：技能图标旁显示「当前候选部队里谁拥有该技能」。  
- **科学拆法**：  
  1. **父级空间**（编队/技能板宿主）的 graph **额外 output** 写出候选实体袋（及技能槽袋）；  
  2. **技能元素**明文 `inputs` 声明：我要消费父级的 `candidates`（类型 = EntityCollection）；  
  3. **技能元素自己的 graph** 以 `self=当前技能槽` + input `candidates` 为约束，再 **output** `holders`；  
  4. layout 的 list **只 bind 自己的 holders**（或 bind 某条 input，若父级已算好持有者——两态都须类型吻合）。  

没有「Ability 控件内置扫全图」；没有「同名 collectionKey 自动撞上」。

```jsonc
// ① 父级宿主（示意）——多写一袋候选给子级用
{
  "id": "panel.squad.abilityBoard",
  "graph": "Graph.Squad.AbilityBoard",
  // Graph outputs（概念）：
  //   EntityCollection  collectionKey=squad.candidates
  //   AbilitySlotCollection collectionKey=squad.focus.abilities
  "collections": [
    {
      "name": "abilities",
      "collectionKey": "squad.focus.abilities",
      "template": "panel.ability.slot"
    }
  ],
  "layout": {
    "controls": [
      { "type": "list", "bind": "abilities", "present": "grid" }
    ]
  }
}

// ② 技能元素——明文声明父级引脚输入 + 自己再算出 holders
{
  "id": "panel.ability.slot",
  "subject": "AbilitySlot",
  "graph": "Graph.Ability.SlotCard",
  "inputs": [
    {
      "name": "candidates",
      "from": { "space": "parent", "output": "squad.candidates" },
      "type": "EntityCollection"   // 装载期与父 graph output 强校验
    }
  ],
  "collections": [
    {
      "name": "holders",
      "collectionKey": "ability.slot.holders", // 本元素 graph 的 output
      "template": "panel.unit.chip"
    }
  ],
  "layout": {
    "controls": [
      { "type": "label", "bind": "displayName" },
      { "type": "list", "bind": "holders", "present": "list" }
    ]
  }
}
```

**接线表（装载期要对账）**

| 子配置 | 必须对上 | 失败时 |
|---|---|---|
| `inputs[].from.space` | 封闭：`parent`（本切片）；日后可扩 `root` | 未知 space → 失败 |
| `inputs[].from.output` | 父 graph 确有该 collectionKey / Summary key | 找不到 → 失败 |
| `inputs[].type` | 与父 output 的 destination/成员身份一致 | 类型不合 → 失败 |
| `collections[].collectionKey` | 若声称来自「本元素 graph output」，则子 graph outputs 必须声明同 key+类型 | 未声明 → 失败 |
| list `bind` | 只能绑：本模板 `collections` 名，或已声明的 input 名（若允许控件直绑 input） | 悬空 bind → 失败 |

**两种合法数据路径（作者二选一，不可混而不宣）**

```text
路径 A · 子图再计算（反查主路径）
  parent output candidates ──input──► child graph ──output──► holders ──list──► UI

路径 B · 父级已算好持有者（少见，适合「当前焦点技能」单格）
  parent output holdersForFocus ──input──► child list 直接 bind 该 input
  （此时子模板可不跑反查图；inputs.type 仍须是 EntityCollection）
```

路径必须在配置上可区分（例如 list `bind` 指向 `collections.holders` vs 指向 `inputs.holders`）；装载期校验唯一来源。

#### 3.7.3 聚合：输入是集合，画面是「首位 + 总数」

- **作者意图**：一堆同类物品只显示一个图标，角标是数量；或一组选中单位只露队长头像 + 「×12」。  
- **数据**：查询图仍写出 **完整** 类型化集合（必要时另写 Summary `count`，与袋 `TotalCount` 二选一作 SSOT，装载期校验一致）。  
- **面板**：同一 `bind` 上 `present: "aggregate"`，用配置声明取哪名成员的哪路表面/图标 pin、总数绑哪。  
- **合同要点**：聚合是 **投影模式**，不是把集合偷偷裁成单元素；虚拟列表窗口化与聚合可并存（聚合通常不需要窗口，但仍可读 TotalCount）。

```jsonc
{
  "type": "collection",
  "bind": "stacks",
  "present": "aggregate",
  "aggregate": {
    "head": { "from": "first", "icon": "icon", "label": "displayName" },
    "count": { "from": "totalCount" } // 或 "pin": "stackCount"
  }
}
```

字段名示意；落地时收成封闭 schema。`from: "first"` 指有序袋下标 0；空袋 fail-closed 或作者显式 `empty` 控件——**禁止** 静默画空白当真有货。

#### 3.7.4 复合与类型轴的关系

| 形态 | 是否新 destination | 图要多做什么 | 面板要多做什么 |
|---|---|---|---|
| 嵌套 | 否 | 以成员为 owner **output** 子集合 | 元素 `collections` 明文 `source=selfGraph`（或 parent input） |
| 反查 | 否 | 父级 **output** 候选袋；子图 ingest + **output** holders | 元素 `inputs[]` 声明父引脚；list bind 自有 holders 或 input |
| 聚合 | 否 | 可选并行 Summary count | `present: aggregate`；bind 仍指向完整集合 |

凡跨层数据：**有 `from` / `source`，有 `type`，装载期对账**；禁止靠 key 重名碰运气。

### 3.8 推进顺序（建议）

| 序 | 交付 | 理由 |
|---:|---|---|
| 0 | 本页合同 + gr-09 / G12 交叉引用 | 先钉类型与复合形态 |
| 1 | Effect 实例集合 + 面板 buff 条 showcase | 实例路径短、玩家可感 |
| 2 | Effect 模板集合 + 最小图鉴/调试条 | 钉死 DefinitionId 路径 |
| 3 | Item 实例 / 定义 + **aggregate** 堆叠展示 | 同构复制并验收聚合 present |
| 4 | Ability 槽集合 + **嵌套**（单位信息内嵌技能栏） | 非实体 + 复合样板 |
| 5 | Ability 反查持有者名单（技能 → 实体） | 反查样板 |
| 6 | Task / Activity 实例 | 跟 narrative 出口对齐 |
| 7 | Tag 枚举 / Progression 节点 | 明确「全量枚举 vs 固定 mask」后再做 |
| — | Dialogue | **不进本页顶层集合表**；选项列表属对话 UI 嵌套合同 |

点选/释放/追踪等交互仍属 **#1015**，与集合输出正交。

---

## 4. 场景

### 4.1 单位状态条上的 buff

玩家点开一名中了「晕眩」的卫士：状态条列出当前效果，晕眩一行显示剩余时间。  
查询图输出 **效果实例袋**；元素模板解实例；剩余时间来自实例，名字来自模板表。

### 4.2 效果图鉴

作者在调试图鉴里浏览全部「控制类」效果模板：只有名字与描述，没有剩余时间。  
查询图输出 **效果模板 id 袋**；元素模板解定义；配置成实例袋则装载失败。

### 4.3 技能栏

玩家看到 8 个技能槽：冷却与是否解锁跟槽走。  
查询图输出 **槽位袋**（相对该单位）；不是技能定义实体列表。技能书界面另用 **技能定义 id 袋**。

### 4.4 背包与图鉴

背包：物品 **实例** 袋（堆叠、耐久在实例上）。  
图鉴：物品 **定义** 袋。两袋不可混写同一 destination。

### 4.5 任务追踪条

玩家看到进行中的任务标题与进度。  
查询图输出 **任务实例** 袋（narrative 实例实体稳定后）；元素解 Task 实例。  
活动条同构，subject 与集合域分开，不合并成「Quest」。

### 4.6 单位详情里嵌技能栏（复合 · 嵌套）

玩家打开某英雄详情：上方是名字与血条，下方是该英雄自己的技能格。  
外层解单位；内层名单是该单位 owner 下的技能槽袋——不是全图所有技能。

### 4.7 技能图标旁的持有者（复合 · 反查）

玩家看某个技能格：旁边列出当前编队里谁会这招。  
名单仍是实体袋，由「以该技能为约束」的查询写出；不是技能控件私自扫世界。

### 4.8 一堆物资只显示一个图标加数字（复合 · 聚合）

玩家背包里同类药剂有 12 瓶：界面只露出一枚图标和「12」。  
输入仍是完整实例袋；`present: aggregate` 取首位外观 + 总数。空袋按配置失败或走 empty，不假装有货。

---

## 5. 边界

- **不** 为面板或 list 虚拟化修改 GAS Effect / Ability / Tag 的生命周期与容器规则。  
- **不** 把模板 id、tag id、槽位下标伪装成 `Entity` 写入现有 `EntityCollection` 以求「先能显示」。  
- **不** 在面板模板内做过滤/排序替代查询图。  
- **不** 用旁路扫容器作为正式 SSOT；旁路仅过渡，正式路径必须是图写出类型化集合。  
- **不** 把 Dialogue 整场会话当成名册 subject；选项列表另约。  
- **不** 在本页规定皮层视觉（主题、动效）；只定数据类型与消费关系。  
- **不** 在引擎内硬编码 EntityInfo / AbilityIcon / ItemStack 等业务控件；嵌套、反查、聚合一律配置表达。  
- **不** 靠 collectionKey 同名或「当前成员上下文」隐式撞袋；跨层必须 `inputs`/`source` 明文 + 类型强校验。  
- **不** 把 `aggregate` 做成「图只输出第一个成员」的假集合类型以省事。  
- **不** 在 `docs/adr/` 另开平行 AAC ADR；集合与面板投影合同以 gitbook 本页 + [panel-view-projection](panel-view-projection.md) 为准。  
- 未实现的 destination / subject / present：**fail-closed**，禁止 fallback 到实体集或静默空画。

---

## 6. UAT

```gherkin
Feature: 查询图能诚实写出不同类型的名单，面板只按类型来画

  Scenario: 身上的 buff 是「正在生效」的一串，能看到剩余时间
    Given 晕眩卫士身上挂着仍在计时的晕眩效果
    And 名册或状态条绑定的是「效果实例」名单
    When 我打开该单位的状态条
    Then 我能看到晕眩这一行以及还在减少的剩余时间
    And 我不会把它当成「效果图鉴里的一条静态说明」

  Scenario: 效果图鉴是「模板说明书」，没有剩余时间
    Given 作者打开效果图鉴且名单来自「效果模板」查询
    When 我浏览控制类效果
    Then 我能看到效果名称与说明
    And 我看不到「剩余几秒」这类只有实例才有的信息

  Scenario: 实例名单和模板名单不能配错
    Given 某面板行卡片声明自己解的是效果实例
    And 查询图却写出了效果模板 id 名单
    When 关卡装载或面板绑定
    Then 配置失败并明确指出名单类型与行卡片声明不合

  Scenario: 技能栏按槽位展示，而不是假装成一串单位
    Given 英雄技能栏有若干槽位且部分在冷却
    And 查询图写出的是槽位名单
    When 我查看技能栏
    Then 每个槽位按栏位顺序出现且冷却跟槽走
    And 系统没有为此生成假的「技能实体」塞进单位名单

  Scenario: 背包与图鉴各走各的名单
    Given 背包查询写出物品实例名单
    And 图鉴查询写出物品定义名单
    When 两个界面分别打开
    Then 背包行能反映堆叠或耐久等实例信息
    And 图鉴行只反映定义信息且两份名单不会在配置里被当成同一种

  Scenario: 尚未开通的名单类型不能偷跑
    Given 某查询图写出了合同已预留但运行时未接线的名单类型
    When 装载或绑定依赖该名单的面板
    Then 失败并指出该类型未接线
    And 不会静默改写成普通单位名单继续显示

  Scenario: 单位详情里能看到属于自己的技能格
    Given 我打开指挥官的单位详情
    And 详情配置了嵌套的技能名单且查询以该单位为归属写出技能槽
    When 详情展开
    Then 我看到的是指挥官自己的技能格
    And 不会出现其它单位的技能混在同一栏

  Scenario: 技能旁能看到谁会这招
    Given 父级技能板查询额外写出了「候选编队」实体名单引脚
    And 火球技能格明文声明 inputs 消费该候选名单且类型为实体集合
    And 技能格自己的查询在候选里筛出会火球的人并写出持有者名单
    When 我查看火球技能格
    Then 持有者名单里只有会火球的人
    And 配置里能看出持有者数据来自「父引脚 → 子输入 → 子查询输出」而不是控件私扫

  Scenario: 父引脚类型与子输入声明不一致则失败
    Given 父级输出的是效果模板名单
    And 子技能格 inputs 却声明要实体集合
    When 装载或绑定
    Then 失败并指出父输出与子输入类型不合

  Scenario: 一堆同类物资可以聚合成一个图标加数量
    Given 背包查询写出了 12 瓶同类药剂的实例名单
    And 界面将该名单配置为聚合展示（首位外观 + 总数）
    When 我打开背包相关栏位
    Then 我看到一个药剂图标和数量 12
    And 名单类型仍然是完整的物品实例袋而不是「只含一个假实体」

  Scenario: 空名单做聚合不能假装有货
    Given 某聚合栏位绑定的集合当前一个成员都没有
    When 面板尝试按聚合配置绘制
    Then 按配置失败或走作者声明的空态
    And 不会画出仿佛有物品的图标
```

---

## 相关入口

- 面板消费：[panel-view-projection.md](panel-view-projection.md)  
- 今日输出字段：[gr-09-outputs 配置](../reference/mod-editor-prd/config/gr-09-outputs.md)  
- 图分层：[graph-layering-flow-and-behavior.md](graph-layering-flow-and-behavior.md)  
- 面板目录 G12：[panel-catalog-designs.md](panel-catalog-designs.md)  
- 交互点选（正交）：GitHub #1015  
