Part of #1321（W3 资产生命周期之二）

## 目标

两阶段异步加载：worker 只做 VFS 解析/读取/格式转换（含 Assimp→GLB），GL 资源创建只在渲染线程；启动期同步预加载定义为显式 bootstrap 闸门状态；运行态禁止 draw-time 同步加载。不做同步 fallback 开关。

## 验收标准

- Given 首次请求大型模型；When worker 完成 CPU 准备；Then GL 资源只在渲染线程创建；首帧不执行完整同步导入（benchmark 前后对比入报告）；缺失文件、转换失败、上传失败均 fail-loud。
- Given 引擎启动；When 处于 Loading 闸门状态；Then bootstrap 资产集就绪前同步完成是该状态被定义的行为，一次性、可观察；进入运行态后不得再出现任意 draw-time 同步加载。
- Given 任意路径；When 资源未就绪；Then 状态与失败原因可观察，不以"跳过绘制"作隐式降级。

## 依赖

前置：#1327（句柄与租约模型）。
