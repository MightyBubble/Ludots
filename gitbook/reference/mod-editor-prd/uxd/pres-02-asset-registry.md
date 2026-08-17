# pres-02 UXD · 表现资产清单的编辑器需求

> pres-02 的编辑器需求（高保真规格）。第一性需求见 [pres-02 PRD](../prd/pres-02-asset-registry.md)；配置写法见 [pres-02 配置说明](../config/pres-02-asset-registry.md)；编辑器实现见 [editor spec](../spec-editor/pres-02-asset-registry.md)；目录计数以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

资产浏览器是表现内容的仓库：作者导入/登记网格与材质、把逻辑资产钉到平台文件、组装实例批次。pres-01 的资产选择器从这里取数。

## 2. 布局线框

```text
┌─ 资产浏览器 ──────────────────────────────────────────────────────┐
├─ 左：分类树 ────────┬─ 中：资产网格（缩略图）──────┬─ 右：详情 ─────┤
│ ▸ 网格 (mesh)       │ [cube] [sphere] [hero.glb]   │ id [cube]      │
│ ▸ 材质 (material)   │ [＋导入文件…]                │ type Primitive │
│ ▸ 平台绑定 (host)   │                              │ kind  [Cube ▾] │
│ ▸ 实例批次 (batch)  │  筛选: [域▾][类型▾][来源▾]   │ 使用处 ×3      │
│   ⚠ 批次: 零样例    │                              │                │
└─────────────────────┴──────────────────────────────┴────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 分类树 | 四张表的注册表投影 | 每类计数徽标 |
| 资产网格 | 各表条目 + host 映射的文件缩略图 | 无 host 绑定的逻辑资产打"未落地"黄标 |
| 导入向导 | VFS 地址空间（cfg-02） | 产出 mesh 行 + host 行成对 |
| type/kind 下拉 | 引擎枚举 | 封闭白名单；Prefab 不出现 |
| 域/旗标表单 | material domain 枚举 + flags | blend mode 等开关 |
| 批次编辑器 | instanced_batches 表 | groups 至少一组；事件键下拉来自 GAS/表现事件枚举 |
| 来源列 | mod 归属（根表/本 mod/依赖 mod） | 覆盖审计入口 |

## 4. 关键交互流：导入一个模型并落地

1. 拖入 `hero.glb` → 导入向导识别后端。
2. 生成逻辑行：mesh `{id, type: Model}`；sourceUris 被向导排除（表禁止）。
3. 生成 host 行：backendId、assetKind、assetId、sourceUris 指向真实路径。
4. 缩略图渲染成功 → 保存；两行成对写入本 mod。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 未落地 | mesh/material 无 host 行 | 黄标"该后端无真实文件" |
| 路径失效 | host sourceUris 在 VFS 外/丢失 | 红标 + 重新定位 |
| 批次零样例 | instanced_batches 无真实数据 | 该分类显示骨架说明（D1） |
| 待重启 | 保存后 | 状态栏"重启生效" |

## 6. 易用性验收口径

- 导入一个模型到可用（表现器可选），全程 ≤ 4 步且不手写 JSON。
- "未落地"资产清单一键过滤可达。
- 非法字段（mesh 的 sourceUris、type Prefab）在编辑期不可产生。

**相关文档**：[pres-02 PRD](../prd/pres-02-asset-registry.md) · [editor spec](../spec-editor/pres-02-asset-registry.md)
