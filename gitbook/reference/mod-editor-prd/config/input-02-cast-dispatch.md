# input-02 配置说明 · 施法派发档案

> 配置写法与行为。第一性需求见 [input-02 PRD](../prd/input-02-cast-dispatch.md)；编辑器需求见 [UXD](../uxd/input-02-cast-dispatch.md)；现状见 [reference](../reference/input-02-cast-dispatch.md)。

## 1. 示例配置

引擎根资产真实档案（`assets/Input/cast_dispatch_profiles.json` 全量）：

```json
{ "profiles": [
  { "id": "dispatch.all_together",
    "selector": { "kind": "all" },
    "router": { "kind": "parallel", "sharedOrderId": true } },
  { "id": "dispatch.one_by_one",
    "selector": { "kind": "cycle", "advanceOn": "orderAccepted" },
    "router": { "kind": "sequential" } },
  { "id": "dispatch.nearest_top_n",
    "selector": { "kind": "topN", "n": 3 },
    "scorer": { "kind": "utility", "considerations": ["distanceToTarget:invert"] },
    "router": { "kind": "parallel", "sharedOrderId": true } } ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 档案标识；控制方案 `defaults.castDispatchProfileId` 按它引用 |
| `selector.kind` | `all`（全组）/ `topN`（按分取前 N，须给 `n`）/ `cycle`（轮转一位） |
| `selector.advanceOn` | 轮转推进时机（如 `orderAccepted`：单被接受才轮到下一位） |
| `scorer.kind` | 评分器；当前 `utility` |
| `scorer.considerations` | 考虑因素列表（如 `distanceToTarget:invert` 距离取反=近者高分） |
| `router.kind` | `parallel`（同帧齐发）/ `sequential`（按序逐个） |
| `router.sharedOrderId` | 并行路由下整组共享一个订单号（与订单流水共享批量对应） |

## 3. 文件结构

`assets/Input/cast_dispatch_profiles.json`（引擎根资产持三档案；mod 同 id 深合并）。

## 4. 运行时加载效果

装配期注册并校验形状；运行期意图路由出组后由输入映射系统按"控制方案默认档案"选人与排序（选人入口见 ord-06 的消费链）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| `topN` 缺 `n` / 未知 kind / 未知评分器 | 启动失败 |
| 控制方案引用不存在的派发档案 | 启动失败（见 input-05） |
| cycle 推进无调用方 | 现状缺陷：退化为永远第一位（O8） |

## 6. 实例

- 根三档案：`assets/Input/cast_dispatch_profiles.json`

**相关文档**：[input-02 PRD](../prd/input-02-cast-dispatch.md) · [input-01 配置说明](input-01-command-intent.md) · [ord-03 配置说明](ord-03-pipeline.md)
