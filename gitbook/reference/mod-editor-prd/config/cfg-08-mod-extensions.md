# cfg-08 配置说明 · mod 代码扩展面

> 配置写法与行为。第一性需求见 [cfg-08 PRD](../prd/cfg-08-mod-extensions.md)；编辑器需求见 [UXD](../uxd/cfg-08-mod-extensions.md)；现状见 [reference](../reference/cfg-08-mod-extensions.md)。

## 1. 示例

代码与数据协作（摘自演示场景）。第一步，入口里注册处理器：

```csharp
public void OnLoad(IModContext context)
{
    _handlerId = context.Extensions.Gas.RegisterBuiltinHandler(
        "ApplyHeatMark", ApplyHeatMark,
        new EffectOperationMetadata(EffectOperationKind.Pure, EffectAtomicDomain.None, "ApplyHeatMark"));
}
```

第二步，配置分片按名引用——预设类型把默认相位处理器指向这个键，效果分片引用该预设。读法：**代码注册键，数据声明引用，运行时执行**。注册图节点同理：

```csharp
int opId = context.Extensions.Gas.RegisterGraphOp(
    "MyMod.SumNearbyHealth", GraphValueType.Float, handler, GraphValueType.Entity);
```

## 2. 注册面与行为

| 注册面 | 签名要点 | 这样注册的效果 |
|---|---|---|
| `Extensions.Gas.RegisterBuiltinHandler` | 键 + 处理函数 + 操作元数据 | 键成为效果预设可引用的内建处理器名；元数据（操作类别/原子域）决定它可出现的相位窗口 |
| `Extensions.Gas.RegisterGraphOp` | 键 + 输出类型 + 执行体 + 输入类型 | 键成为图可创作节点；输入输出类型参与编译期类型检查 |
| `Extensions.Presentation.RegisterPerformerCommand / RegisterPerformerBehavior` | 键 + 描述符 | 表现器配置可按名使用的新命令/行为 |

三条铁律：只在加载窗口；语义键单主（重复即错，与配置 id 同处全局命名空间）；全部 fail-fast。

## 3. 文件结构

注册不落文件——发生在 mod 入口代码；结果进引擎各运行时注册表。你的 mod 拥有哪些扩展键，读入口代码即知。

## 4. 运行时加载效果

三拍：装配期注册（入口窗口）→ 枢纽冻结 → 配置编译解析键（未注册即编译失败）→ 运行期执行。代码注册**先于**配置编译——"新积木不需要新 schema"的实现基础。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 枢纽冻结后注册 | 启动失败 |
| 同一 mod 重复注册同名键 | 启动失败，指明键名 |
| 配置引用未注册的键 | 编译期启动失败，指明引用方与键名 |
| 键与引擎既有键撞名 | 启动失败 |

## 6. 实例

- 处理器 + 数据引用完整链：`mods/showcases/capability_standard/CapabilityStandardEffectPresetTypeCodeShowcaseMod/CapabilityStandardEffectPresetTypeCodeShowcaseModEntry.cs`
- 图节点/表现命令/表现行为 showcase：`mods/showcases/capability_standard/` 下对应目录

**相关文档**：[cfg-08 PRD](../prd/cfg-08-mod-extensions.md) · [cfg-01 配置说明](cfg-01-mod-manifest.md) · [cfg-04 配置说明](cfg-04-config-tables.md)
