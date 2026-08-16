# cfg-08 · mod 代码扩展面

> 产品承诺 · 已冻结。理想实现见 [cfg-08 spec](../spec-runtime/cfg-08-mod-extensions.md)；现状见 [cfg-08 reference](../reference/cfg-08-mod-extensions.md)。

## 1. 定位

mod 不只能写数据和图——还能用 C# 代码给引擎**注册新积木**：新效果处理器、新图节点、新表现命令与行为。这是"组合优先于改 Core"的正规通道：不新增 Core 枚举、不动引擎源码，注册一个语义键，配置按名字引用它。

## 2. 示例配置

代码与数据协作的真实示例（摘自仓库 showcase）。第一步，mod 代码在加载窗口注册处理器：

```csharp
public void OnLoad(IModContext context)
{
    _handlerId = context.Extensions.Gas.RegisterBuiltinHandler(
        "ApplyHeatMark",
        ApplyHeatMark,
        new EffectOperationMetadata(EffectOperationKind.Pure, EffectAtomicDomain.None, "ApplyHeatMark"));
}
```

第二步，配置分片按名字引用它——预设类型分片把默认相位处理器指向这个键，效果分片引用该预设：

```json
[ { "id": "HeatMark", "components": [ "ModifierParams" ],
    "activePhases": [ "OnApply" ],
    "defaultPhaseHandlers": { "OnApply": { "type": "builtin", "id": "ApplyHeatMark" } } } ]
```

读法：**代码注册键，数据声明引用，运行时执行**——"Heat Mark 由数据声明、由 Mod 代码完成"。注册图节点同理：

```csharp
int opId = context.Extensions.Gas.RegisterGraphOp(
    "MyMod.Sum nearby health",
    GraphValueType.Float,      // 输出类型
    handler,                   // 执行体
    GraphValueType.Entity);    // 输入类型
```

## 3. 注册面与效果

| 注册面 | 签名要点 | 注册后的效果 |
|---|---|---|
| `Extensions.Gas.RegisterBuiltinHandler` | 键 + 处理函数 + 操作元数据；返回处理器 id | 键成为效果预设可引用的内建处理器名；元数据声明操作类别（纯/事务等）与原子域，编译期据此校验它出现在哪些相位合法 |
| `Extensions.Gas.RegisterGraphOp` | 键 + 输出类型 + 执行体 + 输入类型列表（可固定寄存器）；返回 op id | 键成为图程序可创作的节点；输入输出类型参与图编译期类型检查，类型不符即编译失败 |
| `Extensions.Presentation.RegisterPerformerCommand` | 键 + 命令描述符 | 表现器配置可按名使用的新命令种类 |
| `Extensions.Presentation.RegisterPerformerBehavior` | 键 + 行为描述符 | 表现器配置可按名挂接的新行为种类 |

三条铁律约束全部注册面：

- **只在加载窗口**：注册仅发生在 mod 入口的 `OnLoad` 期间；扩展枢纽冻结后再注册，启动失败。
- **语义键单主声明**：一个键一个主人，同一 mod 重复注册同名键即启动失败；键与配置 id 同处全局扁平命名空间。
- **全部 fail-fast**：重复键、配置引用未注册的键、缺目录登记、缺分片、缺处理器表——任何一环缺失启动失败并指明环节。

## 4. 文件结构

不落在文件——注册发生在 mod 的代码入口（`main` DLL 的 `OnLoad`，见 cfg-01 第 5 节）；注册结果进入引擎侧各运行时注册表。你的 mod 拥有哪些扩展键，读你的入口代码即知。

## 5. 运行时加载效果

扩展键的生命周期贯穿启动三拍：

1. **装配期**：mod 逐个加载，入口代码注册扩展键；装配完毕，扩展枢纽**冻结**。
2. **编译期**：配置在其后编译（cfg-04 第 5 节）——预设类型、效果、图程序里的扩展键引用此时解析，未注册的键在编译期报错。
3. **运行期**：注册表只读；效果执行到该处理器、图执行到该节点时调用你的代码。

代码注册因此**先于**配置编译——这是"新积木不需要新 schema"的实现基础。

## 6. 预期反馈

- **注册即得 id**：四个注册面都返回整数 id，供代码侧记录。
- **配置立即可引用**：同一次启动里，后编译的配置分片可以放心引用刚注册的键。
- **面板可验证**：仓库的四个扩展 showcase 各自带面板，逐项展示"键已注册、分片已加载、点击触发"。

## 7. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 枢纽冻结后调用任何注册 | 启动失败 |
| 同一 mod 重复注册同名键 | 启动失败，指明键名 |
| 配置引用未注册的键 | 编译期启动失败，指明引用方与键名 |
| 键与既有引擎键撞名 | 启动失败（全局命名空间单主） |

## 8. 编辑器要点

- **扩展键浏览**：列出本 mod 与依赖 mod 已注册的全部扩展键（按四个注册面分组），是"我能引用什么代码积木"的清单。
- **图节点面板动态化**：节点库按已注册的图 op 动态生成，而不是写死的内置清单。
- **预设编辑器的处理器选择器**：defaultPhaseHandlers 的 builtin id 从已注册键下拉选择，杜绝拼错。
- **引用检查前移**：配置里的扩展键引用在编辑期即校验存在性，不等启动。
- 热应用级别：注册随代码加载，为重启级。

## 9. 实例

- 效果处理器 + 数据引用完整链：`mods/showcases/capability_standard/CapabilityStandardEffectPresetTypeCodeShowcaseMod/CapabilityStandardEffectPresetTypeCodeShowcaseModEntry.cs`（配同目录分片文件）
- 图节点扩展、表现命令、表现行为三个 showcase：`mods/showcases/capability_standard/` 下 `CapabilityStandardGraphOpExtensionShowcaseMod` 与两个 `Performer…Extension` 目录
- 合同正本：架构章 mod-extensible-runtime（四扩展面与铁律的 SSOT）

**相关文档**：[cfg-08 spec](../spec-runtime/cfg-08-mod-extensions.md) · [cfg-08 reference](../reference/cfg-08-mod-extensions.md) · [cfg-01](cfg-01-mod-manifest.md)（加载窗口从哪来）· [cfg-04](cfg-04-config-tables.md)（配置编译时序）· [cfg-07](cfg-07-merge-rules.md)（分片写法）
