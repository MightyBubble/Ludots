# ab-03 配置说明 · CallerParams 参数池

> 配置写法与行为。第一性需求见 [ab-03 PRD](../prd/ab-03-caller-params.md)；编辑器需求见 [UXD](../uxd/ab-03-caller-params.md)；现状见 [reference](../reference/ab-03-caller-params.md)。

## 1. 示例配置

仓库现有 abilities.json 尚无 callerParams 实例；下为教学骨架（同一效果模板按条目取不同数值）：

```json
{
  "id": "Ability.Ex.Waves",
  "exec": {
    "clockId": "FixedFrame",
    "callerParams": [
      { "entries": [ { "key": "damage", "value": 30 }, { "key": "radiusCm", "value": 120 } ] },
      { "entries": [ { "key": "damage", "value": 90 }, { "key": "radiusCm", "value": 260 } ] }
    ],
    "items": [
      { "kind": "EffectSignal", "tick": 0,  "template": "Effect.Ex.Nova", "callerParamsIdx": 0 },
      { "kind": "EffectSignal", "tick": 30, "template": "Effect.Ex.Nova", "callerParamsIdx": 1 },
      { "kind": "End", "tick": 30 }
    ]
  }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `exec.callerParams` | 参数池数组 ≤4 组，按数组下标即条目引用的索引 |
| `entries[].key` | 参数键名，首现注册（全局键命名空间） |
| `entries[].value` | float 数值（单组条目数上限见 [facts](../facts.md) EFFECT_CONFIG_PARAMS_MAX） |
| item `callerParamsIdx` | 引用池中第几组（0 起）；缺省 = 不引用 |

合并与注入语义：

- **同键覆盖**：效果模板自带 configParams 与调用方参数同键时，调用方值胜（fx-14 合并律在效果侧生效）。
- **空间参数自动注入**：时间轴实例带目标位置时自动追加 `TargetPosX/TargetPosY`；带原点时追加 `TargetOriginX/TargetOriginY`（效果侧读这四个键免声明）。
- 注入的前提是池里有余位：余位不足整技能失败（不是只丢坐标）。

## 3. 文件结构

位于 `abilities.json` 单条的 `exec.callerParams`；无独立文件。参数键是全局键命名空间的一部分（与效果 configParams 共用）。

## 4. 运行时加载效果

编译期池编入技能定义（内联定长存储）；键注册进参数键注册表。触发效果条目时：取所引组 → 追加空间参数 → 随效果请求下发 CallerParams → 效果侧按"模板参数 + 调用方覆盖"合并读取。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 单组 entries 超上限 | 启动失败，指明组下标 |
| 池组数超 4 | 启动失败 |
| 运行期空间参数追加失败 | 技能失败（现状错误码不指明根因，见 reference） |
| callerParamsIdx 指到未声明组 | 现状读默认空组（无独立校验，见 reference） |

## 6. 实例

- 教学骨架见上；真实使用待演示场景首个参数化技能落地后回填。

**相关文档**：[ab-03 PRD](../prd/ab-03-caller-params.md) · [ab-02 配置说明](ab-02-exec-timeline.md) · [fx-14 配置说明](fx-14-config-params.md)
