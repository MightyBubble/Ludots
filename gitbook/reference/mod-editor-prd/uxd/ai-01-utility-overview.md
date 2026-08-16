# ai-00 UXD · AI 行为层总论的编辑器需求

> ai-00 的编辑器需求（高保真规格）。第一性需求见 [ai-00 PRD](../prd/ai-01-utility-overview.md)；配置写法见 [ai-00 配置说明](../config/ai-01-utility-overview.md)；编辑器实现见 [editor spec](../spec-editor/ai-01-utility-overview.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

AI 总览是行为层的地图与体检台：一张图看清 18 张表谁有货、编译产物长什么样、接缝是否健康。

## 2. 布局线框

```text
┌─ AI 总览面板 ─────────────────────────────────────────────────────────┐
├─ 左：表清单（按组）────────┬─ 右：编译产物视图 ──────────────────────┤
│ 效用感知                   │ AiCompiledRuntime                       │
│  inputs        2 条 ▸ai-01 │  Atoms 4 · Projection 1 · Goals 1       │
│  normalizations 3 ▸ai-02   │  UtilityRuntime ✔(1 profile)            │
│ 效用决断                   │  Behavior: BT 1 · HFSM 2                │
│  decisions     3 ▸ai-03    │ 接缝体检                                │
│  profiles      1 ▸ai-04    │  GraphScore→只读 ✔                      │
│ 世界状态 ▸ai-10 图行为 ▸…  │  SubmitOrder→OrderQueue ✔               │
│ ＋新建条目（跳对应表）      │  AbilityKey→技能注册表 ✔                │
├─ 底部：[来源 utility_autocast · 主仓 4 文件 · mod 11 文件] ───────────┤
└────────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 表清单 | AiConfigCatalog 18 条目 + 各表合并后条目数 | 条目数 0 灰显；点击跳专篇面板 |
| 编译产物视图 | AiCompiledRuntime 字段投影 | 只读；Empty 时整体显示"效用 AI 未启用" |
| 接缝体检 | GraphScore/SubmitOrder/AbilityKey 引用对账 | 断链红标并跳专篇 |
| 来源条 | VFS 合并来源扫描 | 主仓/mod 分列计数 |

## 4. 关键交互流：摸清一个 mod 的 AI 配了什么

1. 打开 AI 总览，读表清单条目数，定位主要组。
2. 点 `decisions 3` 跳决策面板（ai-03），看决策者归属。
3. 查接缝体检：GraphScore 引用的图是否只读、AbilityKey 是否已注册。
4. 点来源条展开 VFS 视图，确认各表来自哪个 mod。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 效用未启用 | 效用十表全空 | 编译产物显示 Empty，不报错 |
| 半配置 | 十表非空但 profiles 空 | 红条"must declare at least one profile"预检 |
| 断链接缝 | 引用名未定义 | 表清单行红点 + 体检区明细 |

## 6. 易用性验收口径

- 18 张表从总览 ≤ 1 跳可达对应专篇面板。
- 编译期错误（表:id.字段）在保存时预演呈现，不等启动。
- 主仓与 mod 的表来源一眼可分。

**相关文档**：[ai-00 PRD](../prd/ai-01-utility-overview.md) · [editor spec](../spec-editor/ai-01-utility-overview.md)
