# ab-04 配置说明 · 冷却三件套

> 配置写法与行为。第一性需求见 [ab-04 PRD](../prd/ab-04-cooldown.md)；编辑器需求见 [UXD](../uxd/ab-04-cooldown.md)；现状见 [reference](../reference/ab-04-cooldown.md)。

## 1. 示例配置

实战冷却的写法（演示场景真实实例，Ezreal E，champion 技能沙盒）：

```json
{
  "id": "Ability.Champion.Ezreal.ArcaneShift",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      { "kind": "TagClip", "tick": 0, "duration": 72, "tag": "Cooldown.Champion.Ezreal.E" },
      { "kind": "End", "tick": 5 }
    ]
  },
  "blockTags": { "blockedAny": [ "Cooldown.Champion.Ezreal.E" ] }
}
```

全局冷却同构（utility_autocast 沙盒 GCD，duration 30）：多技能共用同一 tag 即共享冷却。

cooldown 数据契约块（教学骨架；现状仓库零使用，作用见 §2）：

```json
"cooldown": { "valueAttribute": "CooldownSeconds", "tag": "Cooldown.Example.Q" }
```

## 2. 字段与行为

| 写法 | 产生什么效果 |
|---|---|
| exec 内 `TagClip`（tag=冷却 tag，duration=冷却时长 tick） | 起播给自己挂冷却 tag 并预约到期移除 |
| `blockTags.blockedAny` 含同 tag | 冷却 tag 在场期间激活被拒（ab-05 门） |
| `cooldown.valueAttribute` | 数据契约：一个已注册属性名，>0 视为冷却中（AI 就绪与界面读取） |
| `cooldown.tag` | 数据契约：冷却 tag（供 AI 共享冷却判定）；与 valueAttribute 至少其一 |
| 多技能同冷却 tag | 共享冷却：任一技能施放后全部进入冷却 |

注意：cooldown 块**不挡施放也不自动挂 tag**——挡施放的是 blockTags，挂 tag 的是 TagClip；契约只供查询。仓库现有技能全部走 TagClip+blockTags 闭环，cooldown 块尚无使用者。

## 3. 文件结构

TagClip 在 `abilities.json` 的 exec.items（ab-02）；blockTags 是技能顶层块（ab-05）；cooldown 是技能顶层块。冷却时长以 tick 计（时钟域换算见 rt-01）。

## 4. 运行时加载效果

三块分别编译：TagClip→时间轴条目+定时预约路径；blockTags→激活门掩码；cooldown→两 int 契约入技能定义。热通道：改 duration/数值走表单热替换，下次施放生效；换 tag 或增删块是重启级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| cooldown 引用未注册属性 / 两项皆空 / 旧字段名 | 启动失败（旧名 cooldownValueAttribute/cooldownTag 指路新名） |
| 冷却期间再次施放 | 激活拒绝（blockTags 命中），非错误 |

## 6. 实例

- 单技能冷却：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`（每技能 TagClip duration 18-72 + 同 tag blockTags）
- 共享 GCD：`mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/GAS/abilities.json`（三技能共用 `Cooldown.UtilityAutocast.GCD`）

**相关文档**：[ab-04 PRD](../prd/ab-04-cooldown.md) · [ab-02 配置说明](ab-02-exec-timeline.md) · [tag-01 配置说明](tag-01-basics.md)
