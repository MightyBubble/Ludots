# Mod 自定义组件

Mod 给 Ludots ECS 新增组件的正式通道。组件分两级：只给自己代码用的**纯代码组件**，和要进 JSON 配置表的 **authoring 组件**。

## 1 组件是什么

组件就是 Arch ECS 的 POCO struct，无基类、无特性标注：

```csharp
public struct CombatStanceState
{
    public int Stance;
}
```

定义在 mod 程序集里即可被自己的 Arch System 泛型查询使用。宿主程序集（`Ludots.*`、`Arch`）由 `ModLoadContext` 统一解析回宿主实例，mod 的类型与引擎 Core 是同一个 Type 宇宙。

## 2 两级注册

### 纯代码组件（零注册）

在自己的 System 里直接 `entity.Add<T>(...)`。Arch 的静态组件注册在首次使用时自动完成，不需要任何显式登记。限制：这样的组件**不能出现在 JSON 配置里**——模板表/地图引用未注册的组件名会启动失败。

### authoring 组件（进配置表）

要让组件名能写进 `Entities/templates.json` 的 components、地图 `Overrides`、模板 extends/uses 组合，在 `IMod.OnLoad` 里登记：

```csharp
public void OnLoad(IModContext context)
{
    // 默认 setter：严格 JSON 反序列化（未知字段即错）后 entity.Add<T>
    ComponentRegistry.Register<CombatStanceState>("CombatStanceState", context.ModId);
}
```

需要自定义校验时传 setter 委托重载（实例见 `mods/CombatStanceBehaviorMod/Runtime/CombatStanceComponentAuthoring.cs`）。注册 API：`src/Core/Config/ComponentRegistry.cs`。

## 3 接入清单

1. csproj `ProjectReference` 指向 `src/Core/Ludots.Core.csproj`，目标 net9.0（`mods/Directory.Build.props` 统一输出到 `bin/net9.0/`）。
2. `mod.json` 的 `main` 指向编译出的 DLL。
3. 入口类实现 `IMod`（`src/Core/Modding/IMod.cs`），注册写在 `OnLoad`。

内置组件与外部 mod 走同一机制：`LudotsCoreMod` 本身就是一个 priority -1000 的普通 mod，其组件注册与任何第三方 mod 无差别。

## 4 生命周期与冲突

- 注册只发生在 `OnLoad` 阶段。引擎在**全部 mod 加载完之后**才跑 ConfigPipeline 与地图装载，所以表里引用的组件名一定先于表解析完成注册。
- 组件名全局唯一：与 core 或其他 mod 撞名且 setter 不同，启动失败并记入配置冲突报告。
- mod 卸载或加载失败时，其注册按 modId 回滚清除；core 的注册不可卸载。
- 不存在纯 JSON 定义组件类型的通道——表只能给**已注册**的组件名配初值（见 [ent-01 配置说明](../reference/mod-editor-prd/config/ent-01-templates.md)）。

## 5 只想挂数据，不想写代码

不需要新组件类型时，用引擎的开放容器，纯资源 mod（无 `main`）即可：

| 容器 | 用途 |
|---|---|
| `AttributeBuffer` | 任意属性名 → 数值，base/current 双轨 |
| `GameplayTagContainer` / `TagCountContainer` | 字符串标签 / 计数标签 |
| Blackboard 系（Int/Float/Spatial/Entity） | 键值数据袋 |

分界线：加"值"可以零代码；组件类型同时定义 System 的查询签名，必须编译期强类型。

## 6 规范与衔接

- 新组件的 payload 字段一律 camelCase（存量例外与迁移清单见 [cfg-04 配置说明](../reference/mod-editor-prd/config/cfg-04-config-tables.md)）。
- 注册后的组件名即可参与模板组合：components 初值、extends/uses 折叠、实例覆盖字段级深合并（[ent-01](../reference/mod-editor-prd/config/ent-01-templates.md)）。
- 实例参考：`mods/CombatStanceBehaviorMod/`（自定义 setter）、`mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod/Runtime/FrontlineComponents.cs`（默认 setter 批量注册）。

**相关文档**：[Mod 架构](mod-architecture.md) · [Mod Extensible Runtime](mod-extensible-runtime.md) · [ent-01 实体模板](../reference/mod-editor-prd/config/ent-01-templates.md)
