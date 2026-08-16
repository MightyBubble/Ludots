# cfg-07 · 合并规则案例集

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/cfg-07-merge-rules.md)；编辑器需求见 [UXD](../uxd/cfg-07-merge-rules.md)；引擎实现见 [runtime spec](../spec-runtime/cfg-07-merge-rules.md)；编辑器实现见 [editor spec](../spec-editor/cfg-07-merge-rules.md)；现状见 [reference](../reference/cfg-07-merge-rules.md)。

## 1. 定位

十种合并意图的速查合同：想做什么 → 怎么写 → 结果与坑。规则本体见 cfg-05。

## 2. 产品承诺

- **裁决确定**：十种情形（新增/改标量/扩对象/改数组元素做不到/追加不存在/屏蔽/物理删/整文件深合并/双位置并存/大小写）每种结果确定可查。
- **危险可预警**：数组整组替换是默认行为，覆盖即换整组——必须让作者预先知道。
- **删除不延续**：屏蔽只作用于此前加载的片段。

## 3. 运行行为

全部裁决发生在启动合并期，一次定形；两个无依赖 mod 的胜负由计划顺序决定。

## 4. 异常承诺

无独立异常；违反书写的后果即各情形注明的"坑"。

**相关文档**：[配置说明](../config/cfg-07-merge-rules.md) · [UXD](../uxd/cfg-07-merge-rules.md) · [cfg-05](cfg-05-config-pipeline.md)
