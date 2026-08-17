# gr-07 · 动作库 ActionLib

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-07-actionlib.md)；编辑器需求见 [UXD](../uxd/gr-07-actionlib.md)；引擎实现见 [runtime spec](../spec-runtime/gr-07-actionlib.md)；editor spec 见 [editor spec](../spec-editor/gr-07-actionlib.md)；现状见 [reference](../reference/gr-07-actionlib.md)。

## 1. 定位

ActionLib 给带宿主的动作起名：把 Script 图按宿主（行为树、状态机、关卡、脚本）收进动作目录，是含挂起图进入挂接点的唯一通道。

## 2. 产品承诺

- **动作必有宿主**：每条动作声明挂在四种宿主之一；宿主决定执行入口与挂起政策。
- **挂起政策装载期裁决**：宿主不允许挂起时，图内可达挂起即拒——不留运行期炸雷。
- **名字双库不撞**：动作名与函数名共用一片命名空间，撞名即拒。
- **先图后库**：引用的图必须先注册且 kind 一致。

## 3. 运行行为

装载位置在 FuncLib 之后（先解析函数调用终检，再装动作）；动作经各自宿主挂接点消费（gr-08）。

## 4. 异常承诺

kind 非 Script、宿主缺省或非法、可达挂起违反宿主政策、与函数库撞名、引用未注册图——装载失败并指明动作名。

**相关文档**：[配置说明](../config/gr-07-actionlib.md) · [gr-05](gr-05-execution.md) · [gr-06](gr-06-funclib.md) · [gr-08](gr-08-mount-points.md)
