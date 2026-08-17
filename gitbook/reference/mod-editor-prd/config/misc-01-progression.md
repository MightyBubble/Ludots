# misc-01 配置说明 · 进度域

> 配置写法与行为。第一性需求见 [misc-01 PRD](../prd/misc-01-progression.md)；编辑器需求见 [UXD](../uxd/misc-01-progression.md)；现状见 [reference](../reference/misc-01-progression.md)。

## 1. 示例配置

FourX 关联 showcase 真实三件（`mods/showcases/fourx_association/FourXAssociationShowcaseMod/assets/Progression/`，节选）：

```json
[
  {
    "id": "fourx.association.team",
    "memberSource": "EntityCollection",
    "collection": "fourx.association.team.members"
  }
]
```

```json
[ { "id": "fourx.association.tech", "scope": "fourx.association.team" } ]
```

```json
[
  {
    "id": "Req.FourXAssociation.SignalNet.Use",
    "root": {
      "kind": "EntityCount",
      "scope": "fourx.association.team",
      "entitySource": "ScopeMembers",
      "count": 2,
      "tags": ["Role.FourXAssociation.Researcher"]
    }
  }
]
```

GAS 侧的完成效果（教学骨架，合成；合同见 fx-23）：

```json
[ { "id": "Effect.FourX.TechStep", "presetType": "CompleteProgression",
    "lifetime": "Instant",
    "progression": { "id": "fourx.association.tech", "delta": 1 } } ]
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| scopes | `memberSource` | ScopeBinding（运行期绑定）或 EntityCollection（声明式集合） |
| scopes | `collection` | memberSource=EntityCollection 时的集合 id；必须已配置 |
| progressions | `scope` | 成绩归属的范围 id |
| requirements | `root.kind` | 条件种类（如 EntityCount） |
| requirements | `root.scope` / `entitySource` | 在哪个范围、取哪路实体计数 |
| requirements | `root.count` / `tags` | 达标数量与 tag 过滤 |
| effects | `progression.id` | 经 ProgressionIdRegistry 解析（上限与冻结见 reference）；未注册即抛 |
| effects | `presetType` + `lifetime` | CompleteProgression 必须 Instant，否则抛错 |

## 3. 文件结构

`assets/Progression/` 三件：`scopes.json`、`progressions.json`、`requirements.json`（均 ArrayById；目录计数见事实页）。根表现状有引擎占位行；玩法内容在 showcase mod。

## 4. 运行时加载效果

三表注册后由 ProgressionScopeBindingSystem 维护成员、RequirementEvaluator 求值；效果侧 CompleteProgression 在效果模板加载期完成 progression id 解析。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| scope 引用未配置集合 | 启动失败 |
| progression 引用未声明 scope | 启动失败 |
| 条件树未知 kind/缺参 | 启动失败，指明需求 |
| CompleteProgression 非 Instant / 缺块 / id 未注册 | 效果模板加载失败，指明效果 |

## 6. 实例

- `mods/showcases/fourx_association/FourXAssociationShowcaseMod/assets/Progression/scopes.json`、`progressions.json`、`requirements.json`

**相关文档**：[misc-01 PRD](../prd/misc-01-progression.md) · [fx-23 配置说明](fx-21-progression.md)
