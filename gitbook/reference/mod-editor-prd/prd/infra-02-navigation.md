# infra-02 · 导航配置

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/infra-02-navigation.md)；编辑器需求见 [UXD](../uxd/infra-02-navigation.md)；引擎实现见 [runtime spec](../spec-runtime/infra-02-navigation.md)；editor spec 见 [editor spec](../spec-editor/infra-02-navigation.md)；现状见 [reference](../reference/infra-02-navigation.md)。

## 1. 定位

导航域三张表回答"谁能走、怎么选路、路网怎么烘"：体型档案声明agent的物理尺寸与质量，寻路配置把体型组合成具名 agent 类型并声明选路偏好，导航网格配置声明离线烘焙算法与运行期增量预算。

## 2. 产品承诺

- **体型先于类型**：agent_profiles 用厘米尺寸（半径/身高/净空/吃水/船宽）+ 质量刻画体型；至少一个档案存在，否则启动失败。
- **一个类型一种选路人格**：pathing 的 agentTypes 绑定体型档案 + 选路模式与权重（网格/图偏好）+ 面积代价 + 图投影规则；同一体型可以有不同选路人格。
- **烘焙可预算**：navmesh 声明离线模式与 recast 算法、逐档案的爬坡/坡度上限、层级与面积代价、运行期逐瓦片预算。
- **尺寸即语义**：体型尺寸直接参与导航网格生成与避让，写错厘米数 = 单位卡墙，字段不带单位后缀的写法一律拒绝。

## 3. 运行行为

导航系统按 agent 类型解析选路（网格或图，按 mode 与权重）；navmesh 离线烘焙按 profiles 参数生成，运行期增量按 tileBudgetPerFixedTick 逐固定步分摊。

## 4. 异常承诺

档案表为空、agentTypes 为空或字段缺失、profileId 未注册、面积/层级引用非法、烘焙参数越界——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/infra-02-navigation.md) · [infra-01](infra-01-engine-physics.md) · [ent-01](ent-01-templates.md)
