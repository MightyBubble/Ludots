# cfg-03 · 启动计划

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/cfg-03-launch-graph.md)；编辑器需求见 [UXD](../uxd/cfg-03-launch-graph.md)；引擎实现见 [runtime spec](../spec-runtime/cfg-03-launch-graph.md)；editor spec 见 [editor spec](../spec-editor/cfg-03-launch-graph.md)；现状见 [reference](../reference/cfg-03-launch-graph.md)。

## 1. 定位

一次游戏启动的完整安排：哪些 mod、什么顺序、哪个平台壳。它由启动器从依赖关系生成，作者不手改。

## 2. 产品承诺

- **顺序唯一事实来源**：加载顺序在生成期由依赖闭包烘焙（确定性：依赖按键名字母序），运行期原样执行、无平局决胜。
- **所见即所跑**：计划、锚文件、实际加载三者指纹一致，不一致拒绝启动——改了配置不可能跑着旧计划。
- **作者只动输入**：影响计划的唯一方式是改选择器/预设与依赖声明；顺序推导完全交给启动器。
- **计划不进 mod**：mod 里没有参与计划的字段，计划是生成期视图。

## 3. 运行行为

生成侧：扫描发现 → 展开选择器 → 闭包解析 → 算指纹 → 写计划与锚。启动侧：读锚 → 三重校验 → 按序逐 mod 装配与合并配置。顺序在启动一刻消费完毕。

## 4. 异常承诺

锚缺失、指纹/顺序/适配器不一致、计划内目录缺失、选择器指向不存在——一律拒绝启动或生成期报错。

**相关文档**：[配置说明](../config/cfg-03-launch-graph.md) · [UXD](../uxd/cfg-03-launch-graph.md) · [cfg-01](cfg-01-mod-manifest.md)
