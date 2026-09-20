# map-02 配置说明 · 地图触发器

> 配置写法与行为。第一性需求见 [map-02 PRD](../prd/map-02-triggers.md)；编辑器需求见 [UXD](../uxd/map-02-triggers.md)；现状见 [reference](../reference/map-02-triggers.md)。

## 1. 示例

地图侧只要一行清单（真实示例，`Maps/audit_outer.json`）：

```json
{ "TriggerTypes": [ "AuditPlaygroundMod.Triggers.AuditScopedMapLoadedTrigger" ] }
```

触发器本体是 mod 代码里的一个类（教学骨架）：

```csharp
public sealed class MyVictoryTrigger : Trigger
{
    public override async Task ExecuteAsync(ScriptContext ctx)
    {
        // 读地图数据 → 判定条件 → 施放效果 / 下订单 / 结束对局
    }
}
```

读法：**地图声明启用，代码承载逻辑**；战役第一关的"摧毁敌方基地获胜"就是一个读布阵、监听事件、判胜负的触发器。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `TriggerTypes[]` | 每项是一个触发器类的全限定类型名；进地图时反射解析并实例化注册。合并取并集去重 |

## 3. 文件结构

清单长在地图 JSON 里（map-01 的字段）；触发器类在你的 mod 代码工程中（cfg-01 第 5 节的入口或代码文件）。

## 4. 运行时加载效果

进地图：合并各片段的启用清单（并集）→ 逐类型名反射解析 → 实例化并注册触发器管理器 → 事件到达时执行。代码先行的地图定义与 JSON 地图共用同一装载路径。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 类型名解析失败 | 加载失败，指明类型名与来源地图片段 |
| 类型不继承触发器基类 | 跳过该类型（与代码先行路径一致） |

## 6. 实例

- 真实启用：`mods/AuditPlaygroundMod/assets/Maps/audit_outer.json`
- 触发器类样例：`mods/AuditPlaygroundMod` 的 Triggers 目录

**相关文档**：[map-02 PRD](../prd/map-02-triggers.md) · [map-01 配置说明](map-01-definition.md) · [cfg-08 配置说明](../config/cfg-08-mod-extensions.md)
