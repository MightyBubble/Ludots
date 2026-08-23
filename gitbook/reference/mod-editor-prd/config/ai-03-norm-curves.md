# ai-03 配置说明 · 归一化与响应曲线

> 配置写法与行为。第一性需求见 [ai-03 PRD](../prd/ai-03-norm-curves.md)；编辑器需求见 [UXD](../uxd/ai-03-norm-curves.md)；现状见 [reference](../reference/ai-03-norm-curves.md)。

## 1. 示例配置

真实例（utility_autocast 目录条目 `AI/normalizations.json`（根数据为空，由 mod 贡献） + `curves.json` 全量）：

```json
[
  { "id": "Norm.UtilityAutocast.CloseHostile", "Kind": "RangeInverse", "Min": 0, "Max": 1600 },
  { "id": "Norm.UtilityAutocast.LowHealth",    "Kind": "RangeInverse", "Min": 0, "Max": 120 },
  { "id": "Norm.UtilityAutocast.HighHealth",   "Kind": "Range",        "Min": 0, "Max": 180 }
]
```

```json
[ { "id": "Curve.UtilityAutocast.Linear", "Kind": "Linear" } ]
```

教学骨架（补齐其余 Kind）：

```json
[
  { "id": "Norm.Example.Raw",    "Kind": "Identity" },
  { "id": "Curve.Example.Near",  "Kind": "Power", "Exponent": 2 },
  { "id": "Curve.Example.Far",   "Kind": "Inverse" }
]
```

## 2. 字段与行为

normalizations：

| 字段 | 这样配会产生什么效果 |
|---|---|
| Kind=Identity | 原样通过（Min/Max 忽略，可不写） |
| Kind=Range | clamp((raw-Min)/(Max-Min))，低值 0 高值 1 |
| Kind=RangeInverse | 1-Range，低值 1 高值 0 |
| Min / Max | 窗口边界，默认 0 / 1；非 Identity 时 Max 必须大于 Min |

curves：

| 字段 | 这样配会产生什么效果 |
|---|---|
| Kind=Linear | v 直通 |
| Kind=Power | pow(v, Exponent)，Exponent>1 越接近 1 越敏感 |
| Kind=Inverse | 1-v 翻转 |
| Exponent | 默认 1，必须为正 |

## 3. 文件结构

目录条目 `AI/normalizations.json`（根数据为空，由 mod 贡献）、目录条目 `AI/curves.json`（根数据为空，由 mod 贡献）（各自 ArrayById 合并）。无 schema（I10）。

## 4. 运行时加载效果

两张表各自编译进 Normalizations/Curves 数组并登记 Ordinal 字典，供考量按名引用；数组槽位即引用 id。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 Kind（两表同规） | 启动失败：Unsupported normalization/curve kind |
| 非 Identity 且 Max≤Min | 启动失败：Max must be greater than Min |
| Exponent ≤ 0 | 启动失败：Exponent must be positive |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/normalizations.json`（3 条）与 `curves.json`（1 条）

**相关文档**：[ai-03 PRD](../prd/ai-03-norm-curves.md) · [ai-02 配置说明](ai-02-inputs.md) · [ai-04 配置说明](ai-04-decisions.md)
