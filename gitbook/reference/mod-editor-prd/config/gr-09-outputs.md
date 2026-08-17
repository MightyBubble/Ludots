# gr-09 配置说明 · Query 图输出

> 配置写法与行为。第一性需求见 [gr-09 PRD](../prd/gr-09-outputs.md)；编辑器需求见 [UXD](../uxd/gr-09-outputs.md)；现状见 [reference](../reference/gr-09-outputs.md)。

## 1. 示例配置

主线资产现状无 Query 图与 outputs 实例，以下为教学骨架（通过 loader 校验逻辑推演）：

```json
{
  "id": "Graph.Query.NearbyEnemies", "kind": "Query", "entry": "q",
  "nodes": [ …空间查询链，TargetList 落在 targets… ],
  "controlEdges": [ … ], "valueEdges": [ … ],
  "outputs": [
    { "id": "nearby", "destination": "EntityCollection", "type": "TargetList",
      "source": "targets", "collectionKey": "combat.nearby", "title": "附近敌人" },
    { "id": "count", "destination": "Summary", "type": "Int",
      "source": "targets", "key": "combat.nearby.count" }
  ]
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `destination` | 两值：`EntityCollection`（整表落集合）/ `Summary`（单值按键写入） |
| `type` | Bool/Int/Float/Entity/TargetList 五值 |
| `source` | 值来源节点 id；Summary 必填且类型匹配，缺省可回落 outputId |
| `key` | Summary 写入键；缺省取 outputId |
| `collectionKey` | EntityCollection 必填，实体集合命名 |
| `id` / `role` / `title` / `summary` | 输出身份与展示元信息 |

硬规则：EntityCollection 必须 TargetList 且 collectionKey 必填；Summary 禁 TargetList。

## 3. 文件结构

outputs 是 graphs.json 文档顶层字段（gr-03），仅 Query 图允许。

## 4. 运行时加载效果

编译期建输出 schema 并校验；Query 图执行收尾时经回写器物化——EntityCollection 建/替换实体集合描述符，Summary 把寄存器值按键写入 owner。输出值进槽池（容量见 [事实与取值表](../facts.md) graphOutputValueCapacity），随实体销毁退休清理。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 Query 图声明 outputs | 编译拒绝 |
| EntityCollection 缺 collectionKey / 类型不符 | 编译拒绝 |
| Summary 声明 TargetList | 编译拒绝 |
| source 不存在或类型不匹配 | 编译拒绝 |
| 执行时 owner/caster 为空 | 物化失败 |

## 6. 实例

- 主线资产现状无实例（Query kind 零存量）；编辑器样本@@gr2@@ 第 6 节其余 kind
- 消费方：实体集合描述符与摘要键值（gr-09 次要挂点）

**相关文档**：[gr-09 PRD](../prd/gr-09-outputs.md) · [gr-03 配置说明](gr-02-document.md) · [gr-09 配置说明](gr-08-mount-points.md)
