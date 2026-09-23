# Entity Group / Override（实体组与覆盖）

## 1. 概述

一整套把**多个实体作为一棵可复用、可变异、可被引用的单位**来声明的正式基建。它不做新的模板类型、不开第二套命名空间、不新增"组模板 / prefab / 槽位表"等概念——而是把既有 `EntityTemplate` 升格为唯一本位，在其上长出递归 `children`、`localId` 路径、`attach` 标记与 `relations`，并给地图摆放的 `Overrides` 一套路径化的封闭操作集。

正式入口：

- 资产：`EntityTemplate`（唯一本位，`templates.json`）
- 摆放：地图 `Entities[]` 的 `instanceId + template + position`（复用既有语法，无新增 `group` 字段）
- 变体：实例级 `Overrides`（封闭 5 种路径操作）
- 装载：`MapLoader.LoadEntitiesAndIndex`（组摆放 → 前缀化实体条目，走既有 lane）
- 可寻址引用：统一"可寻址实体路径命名空间"（下详）

ADR / 计划 SSOT：GitHub `#1540`；本页是实体组覆盖合同的文档正本。表现层预置组合见 presenter children；物理挂接见 `entity-attachment.md`；实体域 Trigger 作用域见 `entity-trigger-graph-subworld.md`。

## 2. 核心原则（先立规矩）

1. **只有两个作用域**：预制体式复用（资产默认）与摆放实例（单份差异）。再无第三套。
2. **只有一个模板类型**：`EntityTemplate`。营地是它、塔是它、门是它，只差"带不带 children、带不带 relations"。无 prefab、无组模板。
3. **override 只有一个封闭开放集（5 种）**：`set / addChild / removeChild / addComponent / removeComponent`，一律按**绝对 localId 路径**寻址，deep-merge。无 duplicate、无 replace、无原型套、无槽位表。
4. **children 唯一且递归**；`attach` 是唯一用来区分"结构挂接子件"与"可动成员"的字段。
5. **关系（relations）**是本引擎一等公民，独立一段（资产级默认 + 实例级覆盖），按路径对寻址、实例化时 remap 成全局实体对。
6. **可寻址引用统一一套**：稳定**可寻址实体路径**是唯一对外引用口，真实体 id 只是该路径在当帧的运行时投影（见 §8）。

## 3. 资产结构：一棵可复用的实体树（营地 = 普通模板）

```jsonc
// Entities/templates.json —— 营地只是一个普通实体模板
[
  {
    "id": "ds.mule.camp",
    "components": { "Faction": { "id": "Mule" } },

    "children": [
      { "localId": "hq", "template": "ds.struct.tent", "attach": true,
        "children": [
          { "localId": "radio", "template": "ds.item.radio" },
          { "localId": "chest", "template": "ds.item.lootbox",
            "children": [ { "localId": "cargo", "template": "ds.item.rope" } ] }
        ] },
      { "localId": "tower", "template": "ds.struct.sentry",
        "children": [ { "localId": "light", "template": "ds.prop.spotlight" } ] },
      { "localId": "barricade", "template": "ds.struct.jersey" },
      { "localId": "campfire", "template": "ds.prop.campfire",
        "children": [ { "localId": "pot", "template": "ds.item.pot" } ] },
      { "localId": "parking", "template": "ds.prop.parking_spot",
        "children": [ { "localId": "bike", "template": "ds.vehicle.mule_bike" } ] },
      { "localId": "guard",  "template": "ds.unit.mule", "attach": false },
      { "localId": "guard2", "template": "ds.unit.mule", "attach": false,
        "localPose": { "offsetXCm": 360 } }
    ],

    "relations": [
      { "from": "hq",    "to": "hq.chest",  "type": "Owns" },
      { "from": "tower", "to": "tower.light","type": "Controls" },
      { "from": "guard", "to": "campfire",   "type": "WorksAt", "metric": { "Loyalty": 80 } }
    ]
  }
]
```

### 规则

- `attach:true`（默认）→ 结构挂接子件：物理父子，局部坐标继承；仍受 `MovementParticipation` 禁令（它是静态件）。
- `attach:false` → **独立实体**：按树的位姿出生，但不 Attach；建立 **MemberOf 边**连回根实体，可自由移动。这一条让"会动的哨兵也能进营地"，且不加第二套结构。
- 子件**可以有自己的递归 children**（`hq → chest → cargo` 穿了多层模板边界）。
- `relations` 的 `from/to` 用**局部路径**；`@root` 保留字指组根实体。关系类型必须已在 catalog 注册，装载期 fail-fast。

## 4. 摆放实例（最简）

```jsonc
// Maps/eg_camp_site.json -> entities[]（字段名与教科书 showcase 可运行样例一致）
{ "instanceId": "mule.harbor", "template": "eg.camp",
  "positionXCm": 1200, "positionYCm": -800 }
```

- `instanceId`（全局唯一命名空间根）+ `template` 必填；**带 localId 后代时 `instanceId` 强制**（缺失/空白/首尾空白均装载期 fail-fast）。`positionXCm`/`positionYCm` 可选、成对声明（锚点兜底：模板与 override 均未写 WorldPositionCm 时才落地）。
- 摆营地就是摆任何普通模板：**无新增 `group` 字段**。

## 5. 实例变体：封闭 5 种路径 override

overrides 只有一个封闭开放集（5 种），一律按**绝对 localId 路径**（穿模板，"爷爷可改到底"）寻址，deep-merge，不外散到每层模板。所有修改全平铺在**该实例的 override 段落**里。

**两个作用域，不是两套系统**：
- **自身（overrides）**——改的是这个实例自己的字段（根组件整替换），见 5a–5e。
- **后代（overridePaths）**——改的是"别人模板里长出来"的字段，必然要一个**绝对路径**穿透到底，见 5a–5e 的 `path` 一律指绝对 localId 路径。

两者都是"这个实例的一份差异"，区别只是**命中的目标是谁**（自身 vs 后代）。这是同一套差别账的两个抽屉，不是第二套模板类型、不是双轨。

地图装载目前只落地了后代的名字。`overridePaths` 的 `path` 写实例里面的 localId 路径，不带 instanceId；`set` 只接受 `Name`。名字按这个顺序叠：模板名字，再叠模板子节点自己的 `overrides.Name`，最后叠实例路径。没写进 `overridePaths` 的子实体就停在前两层。只带名字、没有别的组件的同一层子实体一次生成；带了别的组件的子实体仍逐个装配，名字按同样的顺序写上。路径对不上、名字空着，或 `set` 里写了名字以外的组件，加载直接失败。加子、删子、加组件、删组件还没进装载。

### 5a. 改字段（Property modification，deep-merge）

```jsonc
{ "path": "hq.chest.cargo.loot", "set": { "count": 12 } },
{ "path": "guard",                "set": { "weapon": { "kind": "musket" } } }
```

### 5b. 加子物体

```jsonc
{ "path": "campfire", "addChild": { "localId": "thermos", "template": "ds.item.thermos" } }
```

### 5c. 删子物体（整枝，子孙随父）

```jsonc
{ "path": "tower.light", "removeChild": true },
{ "path": "tower",       "removeChild": true }
```

### 5d. 加组件

```jsonc
{ "path": "guard", "addComponent": { "name": "Patrol",
                                      "data": { "points": ["campfire", "barricade"] } } }
```

### 5e. 删组件

```jsonc
{ "path": "hq", "removeComponent": "WorldPositionHeld" }
```

> **换** = remove + add（两个原子操作），**不设 replace**。**多几份** = 多条 `addChild`，各自独立 localId。

## 6. mod 改"默认"：ArrayById 资产编辑（两轴之一）

通过 config 的**同 id 合并**改到**资产本身**，影响所有摆它的实例——这是资产编辑，不是 override：

```jsonc
[
  { "id": "ds.mule.camp",
    "children": [
      { "template": "ds.struct.radar" },                                  // 新 localId → 附一整枝
      { "localId": "barricade", "template": "ds.struct.reinforced_jersey" } // 同名 → 覆盖
    ],
    "relations": [ { "from": "radar", "to": "hq", "type": "Senses" } ]
  }
]
```

两轴：**资产级 = 同 id keyed-merge（改所有）**；**实例级 = 绝对路径 override（改单个）**。互不串。

## 7. 关系：资产默认 + 实例覆盖

资产级 `relations`（§3）是默认；实例级可覆盖同一条边，按 `(from,to,type)` 定位、只改 metric/flag，**不新增命名空间**：

```jsonc
"relationOverrides": [
  { "from": "guard", "to": "campfire", "type": "WorksAt", "metric": { "Loyalty": 95 } }
]
```

边的整体增删属成员记录切片；本页先保障边的 metric/flag 可覆盖。

## 8. 统一可寻址实体路径（组件内部引用 · 完整缺口设计）

用户曾问：**组件里能不能引用"站岗三人的另外三个哨兵"（黑板的磁盘目标，指向同模板里的兄弟实例）？**

### 现状缺口

- 现有 `MapLoadEntityIndex` 是**扁平** InstanceId→Entity 表（§ 见 src/Core/Systems/MapLoadEntityIndex.cs）。
- `LoadPlacedEntity` 可按**地图全局 InstanceId** 拿放置顶层实体。
- 但**营地子件**的运行时身份只有 Arch 实体 id（飘摇、无法被作者书写、不进表）；作者在组件里没法稳定指向营地里某个子件。

这正是"黑板指向三名哨兵"的实际阻碍，也是"地图顶层一套、营地内部一套"双轨的根源。

### 设计：把"可寻址实体路径身份"做成唯一一套

1. **每个可寻址节点都有一个稳定**可寻址实体路径**身份**——顶层是 `camp1.tower`，营地子件是 `camp1.hq.chest`。它不是运行时 Arch id，而是**摆放树里的稳定路径**，可被作者书写、可被组件引用、可在文档化时确定。
2. `MapLoadEntityIndex` 升格：除扁平反查外，增加**路径树**能力——顶层按 InstanceId（既有破坏性保护），营地子件按绝对路径查，**走同一张表、同一条逻辑**，只是 key 是全路径。
3. **组件内引用用这个稳定路径字符串**：作者写 `"$camp1.guardA"` 之类；相对路径在展开时按"当前位置所在的树"解析成绝对路径——**结果是进同一张表的稳定路径，不是另一个轨道**。

### 运行时"真相"定义

- 组件作者写下的是**可寻址实体路径**（稳定字符串），生命周期持久的是**这一字符串**。
- 真实体 id 只是该路径在**当帧的运行时投影（materialize）**，经出生解析得到，不进入持久身份。
- 因此无需改动 `SaveEntityWorldIdNormalizer` / `SaveEntityReferenceValidator` 的 world-id 归一深水区——存读后**重新按路径 resolve** 即可。这满足"黑板指向哨兵"，且不为此做大而重的实体族改造。
- 代价：它**不保证"指针永远跟着某个人"**（哨兵死了不自动换指向）；若需要"活的、可变重定向的实体引用"，那是另一档（B 档：world-id 归一 + 双登记），属后续切片，本页先用 A 档（稳定路径）统一。

### 组件 authoring 示例（黑板 → 三名哨兵）

```jsonc
// templates.json 内，一个实体（黑板挂件）的组件
{ "id": "ds.prop.roster",
  "components": {
    "Roster": {
      "posts": [ { "key": "guard", "path": "@guard" },
                 { "key": "guard", "path": "@guard2" },
                 { "key": "towerwatch", "path": "@tower.light" } ]
    }
  } }
```

装载时，组件里的 `@xxx` 相对路径按"就近有名字的祖先"解析成**绝对可寻址路径**，再在实体出生时物化成真实体。实例可 override 改这些路径（把岗哨换人）。

## 9. 确定性 / 顺序 / 护栏

- **展开序** = 树深度优先（父先子后）× 声明序；override 按**路径表声明序**有序应用（后到生效）。两段只用数组序，不依赖字典序。
- 所有报错**装载期 fail-fast**，带完整上下文（哪个实例、哪个路径、哪类 override）。
- override 路径不能指向不存在节点 → 抛；但可指向由更低层模板定义的节点（set 只改字段，合法）。
- `attach:false` 节点仍可被打 override（它是实体，字段照样改，只是出生方式不同）。
- `@root` 保留字 = 组根实体；`@root` 不可被 remove。

## 10. 让组件引用不过存档深水区的边界声明

- 本页组件引用用**稳定可寻址路径**（A 档），持久化字符串，不进入 world-id 归一深水。
- 若某组件确实需要"持实时可变实体引用"，走既有 `SaveEntityWorldIdNormalizer` + `SaveEntityReferenceValidator` 双登记，属后续补充，不再平行开新机制。

## 11. 范围与切片

本页合同覆盖改到的代码面：

- `EntityTemplate`：加递归 `children`、`localId`、`attach`、`relations`；删"组模板 / slots / group 字段 / groups.json"。
- `MapLoadEntityIndex`：从扁平 InstanceId 升格为能按绝对路径寻址营地子件的统一路径命名空间。
- `MapLoader`：组摆放 → 前缀化实体条目（既有 lane）；`manifest` / `nav` 两条消费链仍用展开结果。
- `Overrides` 应用器：从"整组件替换"升级为封闭 5 种路径操作。

切片建议（可独立合，避免巨 PR）：

| 切 | 内容 | 关联 |
|---|---|---|
| 切A | 递归 children + localId + attach:true + 装载校验（路径唯一、环检测、attach 语义）；删曹魏表 | 结构底子 |
| 切B | 路径 `set`（deep-merge），允许跨模板深达 | 数据覆盖 |
| 切C | `addChild/removeChild/addComponent/removeComponent` | 结构覆盖 |
| 切D | 资产 `relations` + 实例 `relationOverrides` remap | 关系 |
| 切E | `attach:false` → 独立实体 + MemberOf 出生路径 | 可动成员 |
| 切F | 可寻址实体路径命名空间（MapLoadEntityIndex 升格）+ 组件相对引用 | 统一引用 |
| 切G | 成组 spawn（GroupSpawnRequest 展开器 + 组根实体 + 组原子性） | 成组出生 |
| 切H | 路径实体（WaypointCm/RouteCm + OrderArgs.Spatial 实体引用 kind + PatrolOrder） | 路径点线面 |
| 切I | 实例生命周期账本 + 存档 round-trip（#1199 双登记） | 记录 |

## 12. 一张总表（对照需求）

| 你要的 | 这里怎么给 | 归属 |
|---|---|---|
| 实体底下还有实体（层级深） | 递归 children，任意深度 | 切A |
| override 多 | 绝对路径 set，deep-merge，跨模板 | 切B |
| 结构增删（加/删） | addChild/removeChild/addComponent/removeComponent | 切C |
| 关系引用（含覆盖） | 资产 relations + 实例 relationOverrides | 切D |
| 可动单位（哨兵） | attach:false → 独立实体 + MemberOf | 切E |
| 组件引用兄弟实例（黑板→3哨兵） | 统一可寻址实体路径 + 组件相对引用 | 切F |
| 实例变体（复用换改） | 实例 overrides（唯一 5 种） | 切A–C |
| mod 改默认 | ArrayById 资产编辑（两轴之一） | 切A 底子 |
| 成组出生（POI 一键投放/回收） | GroupSpawnRequest 展开器 + 组根 + 组原子性 | 切G |
| 路径点线面（巡逻） | WaypointCm/RouteCm 实体化 + PatrolOrder 实体引用 | 切H |
| 增删改有记录 | 实例生命周期账本 + 存档 round-trip | 切I |
| 无两套 | 单一路径模型，无 prefab/组模板/槽位表/duplicate | 全域 |
