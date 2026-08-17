# misc-03 · 叙事与任务

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/misc-03-narrative.md)；编辑器需求见 [UXD](../uxd/misc-03-narrative.md)；引擎实现见 [runtime spec](../spec-runtime/misc-03-narrative.md)；editor spec 见 [editor spec](../spec-editor/misc-03-narrative.md)；现状见 [reference](../reference/misc-03-narrative.md)。

## 1. 定位

叙事域三张表加任务一张表：变量表存剧情状态（信任度、结局名），对话表是有向节点图（说话人/文本/相机/选项/动作），过场表是相机-台词步骤序列；任务表把阶段、台词、过场与信号串成长线目标。战役地图包的剧情全靠这四张。

## 2. 产品承诺

- **变量是强类型剧情状态**：Int/Float/Bool/String 四类带默认值与显示名；对话文本可内插变量值。
- **对话可分支可驱动**：节点带相机位、自动推进、进入动作；选项带条件与动作（改变量、开任务、发信号、切相机……动作枚举封闭）。
- **过场是步骤带**：每步相机+台词+时长（缺省 0.75s）+是否需玩家推进；完成可自动清相机。
- **任务是阶段的容器**：属性块挂 GAS 属性、阶段带目标文案与提示、进入台词/过场、requiredSignals 判定推进——叙事驱动、运行时服务代管。
- **动作与条件封闭**：条件五种、动作十一种，全部封闭枚举；写未知值即失败。

## 3. 运行行为

NarrativeDirector/RuntimeSystem 驱动对话推进与过场播放；QuestRuntimeService 维护任务与阶段状态，接收信号推进；变量存储持久于世界。

## 4. 异常承诺

变量 kind 非法或默认值类型不符、对话节点/选项引用悬空（nextNodeId 无着落）、动作/条件未知、任务属性引用未注册属性、台词/过场 id 引用未注册——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/misc-03-narrative.md) · [infra-03](infra-03-vision-camera.md) · [map-02](map-02-triggers.md)
