# input-05 配置说明 · 过滤与输入方案

> 配置写法与行为。第一性需求见 [input-05 PRD](../prd/input-05-filters-and-schemes.md)；编辑器需求见 [UXD](../uxd/input-05-filters-and-schemes.md)；现状见 [reference](../reference/input-05-filters-and-schemes.md)。

## 1. 示例配置

控制方案真实例（`assets/Input/control_schemes.json` 全量）：

```json
{ "schemes": [
  { "id": "scheme.default", "inputContexts": [],
    "defaults": { "commandIntentId": "intent.command.default",
                  "castDispatchProfileId": "dispatch.all_together" } } ],
  "allowedSchemes": [] }
```

过滤档案真实例（`assets/Input/filter_profiles.json`）：

```json
{ "profiles": [ { "id": "filter.controllable.default",
  "associationQuery": { "anchor": "localPlayerRep", "expand": "controls" },
  "exclude": { "anyTags": [] }, "include": { "anyTags": [] } } ] }
```

动作绑定教学骨架（default_input；全字段显式必填）：

```json
{ "actionAttributeBindings": [ { "id": "bind.zoom", "action": "Zoom", "attribute": "Input.ZoomAxis",
  "valueKind": "Axis1D", "sourceChannel": "Value", "target": "Current", "scale": 1.0,
  "zeroWhenUiCaptured": true, "suppressOnUiWheelCaptured": false, "preserveValueUntilSnapshot": true } ] }
```

## 2. 字段与行为

| 文件与字段 | 这样配会产生什么效果 |
|---|---|
| `default_input.json` `actions[]{id,name,type}` | 动作词汇表；type 为 Button/Axis1D/Axis2D/Axis3D |
| `contexts[]{id,name,priority,bindings}` | 输入上下文分组；priority 定同抢裁决；bindings 为动作→设备路径（含组合键与处理器） |
| `filter_profiles.json` `associationQuery` + 筛选 | 锚点 `localPlayerRep` + 展开 `controls`（受控实体）或 `none`；`exclude/include.anyTags` 展开后先排除再包含 |
| `control_schemes.json` `schemes[].inputContexts[]` | 方案激活的上下文集 |
| `defaults{commandIntentId, castDispatchProfileId}` | 方案默认意图（input-01）与派发（input-02） |
| `axisMove{actionId, orderTypeKey, throttleTicks, stepDistanceCm}` | 轴动作按节流与步长转移动订单；根级 `allowedSchemes` 为方案白名单（空 = 全允许） |
| `action_attribute_bindings.json` 各字段 | 全部显式必填：动作值经 valueKind/通道/缩写给 target 属性，UI 抢占与快照保持行为随三开关 |

## 3. 文件结构

`assets/Input/` 下四文件：`default_input.json`、`filter_profiles.json`、`control_schemes.json`、`action_attribute_bindings.json`（引擎根资产持默认，mod 深合并补充）。

## 4. 运行时加载效果

动作与上下文注册后供映射/提交档案引用；方案安装后其上下文集与默认生效；过滤档案被交互上下文（input-03）与集合写入方消费；属性绑定系统逐帧写属性缓冲。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未注册动作/属性/意图/派发、属性绑定缺任一字段 | 启动失败（绑定全显式） |
| 切换白名单外方案 | 拒绝切换 |
| 默认玩法上下文缺关键绑定 | 现状静默（治理中，O9） |

## 6. 实例

- 根四文件：`assets/Input/default_input.json`（22 动作 2 上下文）、`filter_profiles.json`、`control_schemes.json`、`action_attribute_bindings.json`

**相关文档**：[input-05 PRD](../prd/input-05-filters-and-schemes.md) · [ord-06 配置说明](ord-06-input-mappings.md)
