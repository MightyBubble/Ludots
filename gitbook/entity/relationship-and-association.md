# 实体关系与 Association

本文是关系类型和关系实体出生状态的正式合同。它描述关系实体如何从数据目录进入运行时，不重复定义关系指标、旗标、回调和知识授权的业务语义。

## 1. 概述

关系类型由 `RelationshipCatalogConfig.Types` 声明。类型可以选择一个 `Template`，用与 `EntityTemplate.Components` 相同的组件字典形状声明关系实体出生时的组件值。

模板在目录安装时通过 `ComponentRegistry` 应用到临时 prototype，再编译成已缓存的组件值；关系实体物化时直接应用缓存结果，不在热路径解析 JSON。

## 2. 结构

```text
RelationshipCatalogConfig
└─ Types[]
   ├─ Id
   ├─ IsSymmetric
   └─ Template.Components
      └─ ComponentRegistry authoring chain
         └─ RelationshipTypeTemplate (baked patch)
            └─ relationship entity materialization
```

## 3. 详情

### 3.1 作者配置

```json
{
  "types": [
    {
      "id": "relationship.alliance",
      "isSymmetric": true,
      "template": {
        "components": {
          "GameplayTagContainer": { "tags": ["Relationship.Alliance"] }
        }
      }
    }
  ]
}
```

`id` 是关系类型身份；`isSymmetric` 仍由关系运行时解释；`template.components` 只声明出生组件。组件名和值必须经过 `ComponentRegistry` 的正式 authoring 链，不得在文档或 Mod 中再造一套 JSON 解析器。

### 3.2 运行时合同

- 模板编译阶段创建临时 prototype，应用所有作者组件，再把组件值分成“新增组件”和“覆盖物化组件”两组缓存。
- `RelationshipInstanceCm` 等关系实体运行时身份组件由 Core 物化流程拥有，作者模板不得声明或覆盖它。
- `AttributeBuffer`、`GameplayTagContainer`、`TagCountContainer`、`DirtyFlags`、`ActiveEffectContainer` 等物化时已存在的组件由模板值覆盖；其他作者组件按缓存值新增。
- 关系实体物化后，指标、旗标、回调、协同和知识授权继续走 `RelationshipCatalogConfig` 对应的正式运行时，不由模板承担。

### 3.3 性能与错误边界

- JSON 只在目录安装/authoring 阶段解析；关系实体物化阶段使用缓存对象值。
- 模板不能写入运行时拥有的关系身份；违规配置必须在安装时明确抛错。
- 未注册组件、非法组件值或无效关系类型不能静默跳过。

## 4. 场景

- Mod 作者定义 `relationship.alliance`，为新关系实体补上阵营标签和初始属性；玩家在关系 Showcase 中看到关系建立后对应的状态投影。
- 同一关系类型被批量物化时，每个实体复用已编译的出生补丁，运行时不重复读取配置文本。
- Mod 作者误把关系身份组件写进模板时，加载阶段直接失败，并指出冲突组件名。

## 5. 边界

- 模板不能替代 `RelationshipRuntime` 的关系查询、指标变更、旗标和回调逻辑。
- 模板不能通过新增开关表达业务变体；需要组合关系效果或图操作时，使用现有 GAS/Graph 组合链。
- 本文不定义 team/player participant 的地图绑定；该合同见[Map-Owned Participant Contract](../architecture/map-owned-participant-contract.md)。

## 6. UAT

```gherkin
Feature: 关系类型出生模板

  Scenario: 关系实体按类型获得出生组件
    Given Mod 已注册一个带模板的关系类型
    When 游戏物化一条该类型的关系
    Then 关系实体拥有模板声明的初始组件值
    And 关系实体的运行时身份仍由 Core 生成

  Scenario: 作者不能接管关系实体身份
    Given 关系类型模板声明了运行时拥有的身份组件
    When 游戏安装关系目录
    Then 加载明确失败
    And 不生成半初始化的关系实体
```

自动化证据：`src/Tests/GasTests/Association/RelationshipTypeTemplateTests.cs`。

## 7. 证据

- `src/Core/Gameplay/Relationships/Config/RelationshipCatalogConfig.cs`
- `src/Core/Gameplay/Relationships/RelationshipTypeTemplate.cs`
- `src/Tests/GasTests/Association/RelationshipTypeTemplateTests.cs`
- Showcase 注册：`showcase.registry.json` 中的 `association_stress`、`fourx_association`、`relationship`
- 可玩关系 Showcase 的既有 UAT 矩阵：`gitbook/architecture/uat-playable-showcase-matrix.md`
