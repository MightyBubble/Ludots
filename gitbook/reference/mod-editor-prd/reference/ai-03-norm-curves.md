# ai-05 reference · 归一化与响应曲线

> 现状参考。第一性需求见 [ai-04 PRD](../prd/ai-03-norm-curves.md)；配置说明见 [ai-04 配置说明](../config/ai-03-norm-curves.md)。

## 1. 现状快照

- normalizations：Kind=Identity/Range/RangeInverse；Min 默认 0、Max 默认 1；非 Identity 时 Max≤Min 报错；运行 Range=clamp((raw-Min)/(Max-Min))、RangeInverse=1-Range。
- curves：Kind=Linear/Power/Inverse；Exponent 默认 1 且必须>0；Power=pow(v,e)、Inverse=1-v。
- 两表编译进 Normalizations/Curves 数组，Ordinal 字典登记，被考量按名引用。
- 真实资产：utility_autocast 归一化 3 条（全部 0 起 Max 1600/120/180）、曲线 1 条 Linear。
- 两表均无 schema（I10）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 归一化编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:650-688 |
| 曲线编译 | AiConfigLoader.cs:690-727 |
| Normalize/Curve 求值 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:495-497,539-540 |
| 定义结构 | src/Core/Gameplay/AI/Utility/UtilityAiCompiledRuntime.cs |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/normalizations.json、curves.json |

**相关文档**：[ai-04 PRD](../prd/ai-03-norm-curves.md) · [ai-05 reference](ai-04-decisions.md)
