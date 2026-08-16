# ab-07 配置说明 · 形态路由

> 配置写法与行为。第一性需求见 [ab-07 PRD](../prd/ab-07-form-sets.md)；编辑器需求见 [UXD](../uxd/ab-07-form-sets.md)；现状见 [reference](../reference/ab-07-form-sets.md)。

## 1. 示例配置

真实实例（champion 技能沙盒：Jayce 锤形态整组换技能，炮形态为底座）：

```json
[ {
  "id": "champion_skill_sandbox_jayce_forms",
  "routes": [
    { "requiredAll": [ "State.Champion.Jayce.Hammer" ],
      "priority": 100,
      "slotOverrides": [
        { "slotIndex": 0, "abilityId": "Ability.Champion.Jayce.Hammer.ToTheSkies" },
        { "slotIndex": 1, "abilityId": "Ability.Champion.Jayce.Hammer.LightningField" },
        { "slotIndex": 2, "abilityId": "Ability.Champion.Jayce.Hammer.ThunderingBlow" },
        { "slotIndex": 3, "abilityId": "Ability.Champion.Jayce.Transform.Cannon" } ] }
  ]
} ]
```

形态 tag 由形态切换技能的 toggleSpec 挂/摘（ab-08）：Transform.Hammer 开→State.Hammer 在场→锤路由匹配。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 表项身份（同 id 深合并） |
| `routes[]` | 路由数组，须非空 |
| `route.requiredAll` | 匹配条件：施法者有效 tag 须全部在场 |
| `route.blockedAny` | 匹配条件：任一在场即不匹配 |
| `route.priority` | 必填整数；多路由同时匹配时严格更大者胜（平分先出现者保持） |
| `route.slotOverrides[]` | 须非空；匹配期间覆盖形态槽层 |
| `slotOverrides[].slotIndex` | 0-7；同路由内重复即启动失败 |
| `slotOverrides[].abilityId` | 须已注册（隐含 abilities.json 先于本表加载） |

## 3. 文件结构

目录条目 `GAS/ability_form_sets.json`（根数据为空，由 mod 贡献）（目录登记的表，数组按 id 深合并）。引用许可序：abilities → ability_form_sets。

## 4. 运行时加载效果

编译为掩码快查（requiredAll/blockedAny）+ 优先级 + 覆盖表；加载后冻结。运行期每帧重算形态槽层（不持久）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| routes 为空 / slotOverrides 为空 / priority 缺失 | 启动失败 |
| slotIndex 越界（<0 或 >7） | 启动失败 |
| 同路由重复 slotIndex | 启动失败 |
| abilityId 未注册 | 启动失败 |
| 单位无形态槽缓冲组件 | 现状静默无路由（见 reference） |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/ability_form_sets.json`（唯一真实表项）

**相关文档**：[ab-07 PRD](../prd/ab-07-form-sets.md) · [ab-06 配置说明](ab-06-slots.md) · [ab-08 配置说明](ab-08-toggle.md)
