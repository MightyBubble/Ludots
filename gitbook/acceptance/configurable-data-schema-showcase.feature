Feature: 作者能在数据结构工作台里看见配置如何驱动面板
  Background:
    Given 底层可配置数据结构能力已经可用
    And 我从 showcase 入口启动数据结构工作台
    And 我看到非空的 unit.scout 示例，而不是空的数据资产

  Scenario: 进入工作台立刻看见 Scout 结构
    Given 工作台已经打开
    When 我查看左侧示例
    Then 我看到 struct、嵌套 position、tags 数组和 rarity enum
    And 解释层显示当前 schema 名称与 record 名称

  Scenario: 修改坐标后右侧面板立刻变化
    Given 右侧面板已绑定 position.x
    When 我把 position.x 改成另一个合法值
    Then 右侧面板显示同一个新坐标
    And 解释层显示绑定路径、当前值与类型

  Scenario: 切换皮肤不改变数据和绑定
    Given 面板处于 Data 或 Mixed 模式
    When 我在 Native 与 Web skin 之间切换
    Then 绑定路径和数值保持不变
    And 只有渲染外观改变

  Scenario: Graph 与 Data 消融对照可读
    Given 我可以看到 Source mode 旋钮
    When 我切换到 Graph only
    Then 面板只显示图输出
    When 我切换到 Data only
    Then 面板显示嵌套 struct、数组和 enum
    When 我切换到 Mixed
    Then 同一面板同时保留两类来源且换肤不改来源

  Scenario: 非法数据停住保存并指出路径
    Given 我正在编辑 unit.scout
    When 我填入未知 rarity 或删除必填字段
    Then 界面显示错误数量与第一处错误路径
    And 保存按钮不可用
    And 系统不会静默写回旧资产

  Scenario: 修复后可以导出作者资产
    Given 刚才的非法编辑已经修复且校验通过
    When 我导出到目标 Mod
    Then 目标 Mod 的 Data/data_schemas.json 与 Data/data_records.json 含有非空示例
    And 面板模板仍能读到导出后的绑定路径
