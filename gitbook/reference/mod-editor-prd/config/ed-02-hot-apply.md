# ed-02 配置说明 · 热应用白名单与边界

> 配置写法与行为。第一性需求见 [ed-02 PRD](../prd/ed-02-hot-apply.md)；编辑器需求见 [UXD](../uxd/ed-02-hot-apply.md)；现状见 [reference](../reference/ed-02-hot-apply.md)。

## 1. 示例配置

热应用不引入新配置——被改的就是常规表字段。作者视角的"热改一个数值"（效果表骨架，字段含义见 fx 卷）：

```json
[ { "id": "Effect.Fireball.Damage",
    "duration": { "durationTicks": 120 },
    "modifiers": [ { "attribute": "Health", "op": "Add", "value": -50 } ] } ]
```

durationTicks、periodTicks、modifiers.0.value 三处即热字段——经工作台改这三处走下次施放生效，改其他字段走重进地图/重启。

## 2. 作者可配什么与在哪配

| 通道 | 热字段 | 在哪改 | 生效级别 |
|---|---|---|---|
| 效果数值 | duration.durationTicks / duration.periodTicks / modifiers.0.value（`modifiers[0].value` 等价写法） | effects 表对应字段，经工作台 | 下次施放 |
| 弹道引用 | projectile.impactEffect / hitEffect / presentationEffect（仅 LaunchProjectile 预设） | effects 表 projectile 块 | 下次施放 |
| 授予 tag | grantedTags 槽 0（Fixed 公式；无槽则追加） | effects 表 grantedTags | 下次施放 |
| 图程序 | 整图替换（同 id 同 kind） | graphs 表/图文档 | 下次施放（安全帧） |
| tag 规则 | 已注册 tag 的规则集整体替换 | tag_rules 表 | 下次施放（安全帧） |
| 属性约束 | 既有约束的数值替换（三边界） | attribute_constraints 表 | 下次施放（安全帧） |

规则：**身份扩张永不热**——新增效果模板、新增 tag、新增属性名、改 preset 身份、改 id——重启级；效果结构大改——重进地图级。判定不靠作者记忆，预检自动给结论。

## 3. 文件结构

无新增文件；白名单本身是代码合同（见 reference）。各通道字段所在表：effects.json、graphs.json、GAS/tag_rules.json、GAS/attribute_constraints.json。

## 4. 运行时加载效果

热替换在安全帧内逐通道执行（图→效果数值→tag 规则→属性约束→效果引用→授予 tag），每步带快照；失败逆序回滚。进行中的效果实例引用模板新值继续周期；已实例化的旧参数不追溯。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 字段不在白名单 | 拒绝并注明"需重进地图/重启"，不降级生效 |
| 模板/图/tag 未注册 | 拒绝，指明 id |
| 图 kind 与注册不符 | 拒绝并恢复 |
| 约束三边界不满足 | 拒绝，指明缺哪条 |
| 批量提交中途失败 | 已提交项逆序回滚 |

## 6. 实例

- 热字段实现：效果模板注册表（见 reference 锚点）
- 演示场景：强化兵种 mod 的数值热调（改伤害/时长→下次施放生效）

**相关文档**：[ed-02 PRD](../prd/ed-02-hot-apply.md) · [ed-01 配置说明](ed-01-workbench-base.md) · [fx-02](fx-02-template.md)
