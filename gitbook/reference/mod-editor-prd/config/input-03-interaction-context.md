# input-03 配置说明 · 交互上下文档案

> 配置写法与行为。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；编辑器需求见 [UXD](../uxd/input-03-interaction-context.md)；现状见 [reference](../reference/input-03-interaction-context.md)。

## 1. 示例配置

引擎根资产现状为空表（`assets/Input/interaction_context_profiles.json` 全量）：

```json
{ "profiles": [] }
```

教学骨架（引导技能激活敌我判定上下文）：

```json
{ "profiles": [
  { "id": "ctx.guided",
    "activeCollectionKey": "collection.guided.targets",
    "activeEntityViewKey": "view.enemies.visible",
    "filterProfileId": "filter.controllable.default",
    "inputContextId": "GuidedAim",
    "commandIntentId": "intent.command.default" } ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 档案标识；能力 exec 声明 `interactionContextProfile` 时引用 |
| `activeCollectionKey` | 上下文激活期间生效的实体集合键 |
| `activeEntityViewKey` | 上下文激活期间生效的实体视图键（声明数据，运行期暂无 id 消费方） |
| `filterProfileId` | 过滤档案（input-05）；可空 = 不过滤直通 |
| `inputContextId` | 上下文激活期间该座位应激活的输入上下文（default_input 的 contexts，input-05）；由 `InputContextProjectionSystem` 每 tick 按座位 diff 派生 push/pop，上下文回收后下一 tick 弹出 |
| `commandIntentId` | 挂载上下文携带的命令意图；仲裁时优先于玩家默认（DEC-14） |

## 3. 文件结构

`assets/Input/interaction_context_profiles.json`（引擎根资产现为空表；档案由 mod 贡献并在能力表引用）。

## 4. 运行时加载效果

档案注册后，能力加载仅校验声明非空串；档案的全部 id 字段在安装期解析（未知引用启动期失败）；声明档案的 exec 开始时把档案挂载为实体交互状态（档案缺失此时报错）、exec 结束回收，期间集合/过滤/意图生效。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 能力声明的档案名未注册 | 该能力执行开始时报错（非启动期） |
| 档案声明的过滤/意图名未安装 | 档案安装期失败（启动期） |
| `interactionContextProfile` 为空串 | 能力加载失败 |
| exec 结束 | 挂载上下文随下一次系统更新回收（系统负责） |

## 6. 实例

- 根空表：`assets/Input/interaction_context_profiles.json`

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [input-05 配置说明](input-05-filters-and-schemes.md)
