# fx-22 · 出生下单

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-22-submit-order.md)；编辑器需求见 [UXD](../uxd/fx-22-submit-order.md)；引擎实现见 [runtime spec](../spec-runtime/fx-22-submit-order.md)；editor spec 见 [editor spec](../spec-editor/fx-22-submit-order.md)；现状见 [reference](../reference/fx-22-submit-order.md)。

## 1. 定位

SubmitOrderFromBlackboard 效果替单位向正式订单队列提交一条命令：目标从黑板存储键读出——"工厂集结点""出生即走向指定位置"的那一环。

## 2. 产品承诺

- **专属组合**：必须 Instant 生命周期加 submitOrderFromBlackboard 块。
- **槽位合同**：source 与 target 槽缺省 Source/Target，禁 None——source 是黑板宿主，target 是下单执行者。
- **黑板合同**：storedTarget 五个键全部必填且经黑板键注册表解析。
- **订单类型合同**：pointMoveOrderTypeKey 与 entityOrderTypeKey 必填且须在订单类型表注册；entityOrderIntArg0 必填；submitMode 二选一 Immediate 立即 / Queued 排队。
- 订单属外部订单原子域：独占效果计划；黑板无目标静默跳过；提交被拒即抛错。

## 3. 运行行为

点与六角目标生成点移动订单，实体目标生成实体订单（携带整型参数）；经正式订单队列入口提交，享受与玩家命令相同的准入与终态链路。

## 4. 异常承诺

槽位为 None、黑板键或订单类型未注册、submitMode 未知——启动失败并指明字段；运行期槽位实体失效、执行者无订单缓冲、提交被拒——抛错带细节。

**相关文档**：[配置说明](../config/fx-22-submit-order.md) · 见 ord-04（黑板）、fx-16（造单位）
