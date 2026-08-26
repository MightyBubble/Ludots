Feature: 作者用懂 schema 的工作台定义数据并绑定面板
  Background:
    Given 我打开数据结构作者工作台
    And 工作台连接到隔离的预览会话，而不是直接改正式运行中的不可变注册表

  Scenario: Schema Designer 能定义嵌套结构
    Given 我还没有 unit schema
    When 我新建 point struct、rarity enum 和 unit struct
    And 我为 unit 添加 name、position、tags、rarity 字段并标记必填
    Then 预检通过
    And 我能在 schema 列表里选中 unit

  Scenario: Record Editor 按 schema 生成表单
    Given 当前 schema 是 unit
    When 我创建 unit.scout 并在表单中填写名字、坐标、标签和稀有度
    Then 我通过下拉选择 rarity 名称，而不是手填整数
    And 右侧预览显示结构化结果
    And 解释层显示数组长度与 enum 名称及数值

  Scenario: 面板绑定使用路径树而不是手写错误路径
    Given 我已有 unit.scout
    When 我在绑定编辑器选择 Data source 与 unit.scout
    And 我从路径树选择 position.x
    Then 绑定成功
    And 预览能读到该路径
    And 我不必把错误路径字符串当作默认绑定方式

  Scenario: Graph 与 Data 引脚可以混用且换肤正交
    Given 同一面板模板已有一个 Graph pin 和一个 Data pin
    When 我切换 Native 与 Web 预览皮肤
    Then 两类来源的投影数据保持不变
    And 只有外观改变

  Scenario: 从零一键搭好 Scout 套件
    Given 作者草稿目录是空的
    When 我使用从零定义 Scout 套件
    Then 我能看到 point、rarity、unit 三个 schema
    And 我能看到 unit.scout 记录与绑定到 position.x 的面板引脚
    And 保存可用

  Scenario: EntityRef 从表单里选实体名
    Given 当前 record 的 schema 含有 focusTarget 字段
    When 我在表单里为 focusTarget 选择一个带名字的实体
    Then 该字段写入实体名
    And 校验不因 EntityRef 路径失败

  Scenario: 校验失败时保存必须可见地失败
    Given 我正在编辑一条原本合法的 record
    When 我删除必填字段、选择未知 enum，或绑到未知路径
    Then 诊断区显示 schema、record、field 与 path
    And 保存按钮不可用
    And 目标 Mod 的数据资产文件内容不变

  Scenario: 校验通过后写回目标 Mod
    Given 当前 schema、record 与面板绑定全部校验通过
    When 我确认保存到目标 Mod
    Then Data/data_schemas.json、Data/data_records.json 与面板模板被更新
    And 下次经正式配置管线启动仍能读到相同路径
