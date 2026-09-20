# ab-03 editor spec · CallerParams 参数池

> 编辑器实现任务书。编辑器需求见 [ab-03 UXD](../uxd/ab-03-caller-params.md)；引擎侧见 [runtime spec](../spec-runtime/ab-03-caller-params.md)。

## 1. 概述

参数池侧栏实现：池格编辑、双向引用索引、键名补全、冲突提示。

## 2. 设计

- **池格视图模型**：callerParams 数组的表格投影；删组重排索引时同步改写全部引用并提示。
- **引用索引**：items.callerParamsIdx 反向索引，与轨道视图（ab-02）共享选中态。
- **键名补全**：数据源为参数键注册表（与效果 configParams 共用命名空间）；同键冲突对照效果模板参数做静态提示。
- **越界拦截**：下拉只列已声明组，编辑器侧先于运行期拦截悬空引用。

## 3. 精确语义与不变量

- 编辑器键注册视图与引擎参数键注册表一致。
- 覆盖提示的判定（同键调用方胜）与效果侧合并规则同源。

## 4. 依赖接口与验收
- 消费：参数键注册表枚举、效果模板 configParams 读取、加载器编译入口。
- 验收：悬空引用在编辑器被拦截；删组后引用自动改写且保存零错误。

**相关文档**：[ab-03 UXD](../uxd/ab-03-caller-params.md) · [ab-03 runtime spec](../spec-runtime/ab-03-caller-params.md)
