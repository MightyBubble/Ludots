# ai-03 · 归一化与响应曲线

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-03-norm-curves.md)；编辑器需求见 [UXD](../uxd/ai-03-norm-curves.md)；引擎实现见 [runtime spec](../spec-runtime/ai-03-norm-curves.md)；editor spec 见 [editor spec](../spec-editor/ai-03-norm-curves.md)；现状见 [reference](../reference/ai-03-norm-curves.md)。

## 1. 定位

归一化与曲线是考量的两段整形器：input 采出原始值（raw），先经 normalization 压到 0..1，再经 curve 弯出响应形状，才进入聚合。二者与 input 同为可复用小定义，被考量逐条引用。

## 2. 产品承诺

- **三种归一化**：Identity 原样、Range 线性压窗、RangeInverse 反向压窗；窗口边界钳制，越界饱和到 0 或 1。
- **三种曲线**：Linear 直通、Power 幂次、Inverse 倒置；Exponent 必须为正。
- **小对象全局复用**：一条 normalization/curve 可被任意多个考量引用；参数可热调（数值替换）。
- **先归一后弯曲**：顺序固定，作者只管各段正确。

## 3. 运行行为

采样链固定为 raw→Normalize→Curve：Range=clamp((raw-Min)/(Max-Min))，RangeInverse=1-Range；Power=pow(v,Exponent)，Inverse=1-v，Linear=v。Min 默认 0、Max 默认 1；非 Identity 时 Max 必须大于 Min。

## 4. 异常承诺

未知 Kind、非 Identity 的 Max≤Min、Exponent≤0——启动失败并带条目路径。

**相关文档**：[配置说明](../config/ai-03-norm-curves.md) · [ai-02](ai-02-inputs.md) · [ai-04](ai-04-decisions.md)
