# ab-08 配置说明 · Toggle 技能

> 配置写法与行为。第一性需求见 [ab-08 PRD](../prd/ab-08-toggle.md)；编辑器需求见 [UXD](../uxd/ab-08-toggle.md)；现状见 [reference](../reference/ab-08-toggle.md)。

## 1. 示例配置

真实实例一（champion 沙盒：Garen 姿态开关，带光环与关断时间轴）：

```json
{
  "id": "Ability.Champion.Garen.Courage",
  "exec": { "clockId": "FixedFrame", "items": [
    { "kind": "TagClip", "tick": 0, "duration": 24, "tag": "Cooldown.Champion.Garen.W" },
    { "kind": "End", "tick": 0 } ] },
  "toggleSpec": {
    "toggleTag": "State.Champion.Garen.Courage",
    "activeEffects": [ "Effect.Champion.Garen.CourageAura" ],
    "deactivateExec": { "clockId": "FixedFrame",
      "items": [ { "kind": "End", "tick": 0 } ] }
  },
  "blockTags": { "blockedAny": [ "Cooldown.Champion.Garen.W" ] }
}
```

真实实例二（形态切换，Jayce Transform.*）：toggleSpec 只有 toggleTag 与瞬时 deactivateExec——开关 tag 驱动形态路由（ab-07）。教学骨架见实例一删去 activeEffects 即最小开关。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `toggleSpec.toggleTag` | 必填：开启挂/关闭摘的状态 tag（路由与规则的输入） |
| `toggleSpec.activeEffects[]` | ≤4 个效果模板：开启时以无限时长挂到自身（Target=自身） |
| `toggleSpec.deactivateExec` | 可选关断时间轴（结构同 exec）：关时先摘 tag 再播此轴；缺省瞬时完成 |
| `exec`（顶层） | 激活时间轴：播完即"开"；冷却 TagClip 照常在此轴上 |
| `blockTags` | 再激活冷却照用；但关闭分支不经过它 |

注意：activeEffects 的效果身上应携带 toggleTag 身份（授予 tag），否则关闭时无法随身份过期回收（回收依赖效果生命周期，不是关断逻辑逐个撤销）。

## 3. 文件结构

`toggleSpec` 是 `abilities.json` 单条技能的顶层块（ab-01）；deactivateExec 结构同 exec（ab-02），但不可再嵌 toggleSpec。

## 4. 运行时加载效果

编译期校验 toggleTag 必填、activeEffects ≤4 且逐个解析为已注册模板、deactivateExec 按时间轴同规编译。运行期开关两态各走独立路径。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| toggleSpec 缺 toggleTag / 旧名 `tag` | 启动失败（旧名指路 toggleTag） |
| activeEffects 超 4 / 引用未注册模板 | 启动失败 |
| 开启时效果请求队列不足 | 起播失败（容量错误，含所需/可用数） |
| 已开状态再激活 | 走关闭分支（不判门、不吃冷却） |

## 6. 实例

- 姿态光环：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`（Garen.Courage）
- 形态开关：同文件 Jayce.Transform.Hammer / Transform.Cannon（配 ability_form_sets）

**相关文档**：[ab-08 PRD](../prd/ab-08-toggle.md) · [ab-02 配置说明](ab-02-exec-timeline.md) · [fx-13 配置说明](fx-13-granted-tags.md)
