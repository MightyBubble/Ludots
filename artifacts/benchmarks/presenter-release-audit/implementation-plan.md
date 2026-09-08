# Presenter 集合表现驻留与清理

## 1. 概述

万人场景中，抬起框选将数千个预览环销毁并创建选中环。已测得 10000 所有者、5000 环的真实 Presenter 子链销毁中位数约 902.69 ms；视觉身份表按实例反复扫描全表是已复现瓶颈。

本次按用户建议采用显式隐藏配置：集合退出将可见性参数置零，再进入复用原目标、原作用域的实例并恢复显示。真正的 Destroy 仍然释放实例，单位死亡和地图卸载走该路径。已有原语足以表达，不新增分配策略 schema 或 RTS 专用 runtime。

## 2. 结构

复用 PresenterDefinitionConfigLoader、全局 PresenterRuleSystem、Scoped CreatePresenter/SetParam、PresenterEntityRuntime、visibilityParamKey、静态脏输出、StableDrawCache 和正式适配器同步。

修改现有 PresenterVisualStableIdTable 的所属实例索引，使真正销毁仅清理该实例拥有的视觉身份。索引保留完整视觉键、固定容量、显式耗尽和稳定 ID 不复用合同。

Core 修复从最新 origin/main 开发。Case E 全局集合规则当前位于尚未合入 main 的 #1462 提交链；其配置在现有 showcase 工作区集成验证，不把前置查询改动重复捆入 Core PR。

## 3. 详情

- 环定义声明 Int 可见性默认值和 AssetBinding.visibilityParamKey。
- 全局成员进入规则使用 CreatePresenter，并携带可见性 1；首次创建，后续复用匹配的 def/owner/scope/parent 实例。
- 全局成员退出规则使用带 definitionId、EventPayloadA 作用域的 SetParam，可见性置 0。
- 隐藏撤掉画面输出，保留实例与视觉身份；恢复不更换身份。纯静态环没有计时器、声音和动画，其未变化帧使用既有静态保留路径。
- 隐藏不是通用暂停：复杂行为的计时器、动画、绑定仍遵循各自合同。文档不能宣称隐藏会自动终止整个生命周期。
- 按实例索引优化同时覆盖 Runtime 与 Emit 的真正销毁入口。
- 验证先覆盖真实配置到规则、运行时、缓存与输出，再测 10000 所有者下的 5000/10000 环切换、首次成本和重复成本。

## 4. 场景

玩家拖动框选看到黄色预览，松手后黄色消失、选中者显示蓝环；再次框选不重复堆叠。切换玩家时旧玩家的集合输出消失，新玩家独立选择。单位消失后任何隐藏或显示的关联环一并释放。

## 5. 边界

- 不将全局规则复制到单位模板，不创建语义标记组件，不绕开 Presenter 写缓存。
- 不将 Destroy 悄悄改成隐藏。配置明确选择 SetParam 或 Destroy。
- 保留实例按曾访问的目标与集合所有者计占用；换新目标仍有首次创建成本，容量按世界规模和集合数核算。
- 不承诺首次创建零分配，也不将环的性能推广为全部 HUD/动画性能。
- 真正销毁仍须正确清理多槽、动态身份、碰撞与实体世代；容量不足显式失败。
- GAS composition gate 范围判定：本次不改 GAS handler、effect preset、graph op、游戏实体 spawn/morph 或 profile schema；使用现有 Presenter 配置和清理索引，完整 GAS gate 不适用。

## 6. UAT

```gherkin
Feature: 大量单位的框选标记及时切换
  Scenario: 松开框选
    Given 战场上有一万个单位
    When 我框住当前玩家的五千个单位并松开鼠标
    Then 黄色预览环消失
    And 五千个被选中的单位显示蓝色选中环

  Scenario: 反复改变框选范围
    Given 我已框选过这批单位
    When 我反复缩小和扩大框选范围
    Then 亮环只显示在当前集合的单位上
    And 同一单位不会叠加重复的同类亮环

  Scenario: 切换玩家
    Given 玩家一已经选中自己的单位
    When 我切换到玩家二并框选玩家二的单位
    Then 当前视角不残留玩家一的框选提示
    And 玩家二能独立选择自己的单位

  Scenario: 单位离开战场
    Given 一个单位曾经被框选过
    When 该单位被销毁
    Then 它的预览环和选中环都不再出现
    And 后续生成的单位不会继承它的标记
```
