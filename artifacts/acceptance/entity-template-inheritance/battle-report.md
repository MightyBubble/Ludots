== Entity Template Inheritance 验收 ==
[装载] templates.json 同 id 合并 → extends 展开 → 校验：grunt(父) + hero(extends grunt) 就位
[展开] hero.AttributeBuffer = 父 base.Health=100/current.Health=80 ∘ 子 base.Health=250 → base.Health=250, current.Health=80（未提及字段继承）
[展开] 标量钩子：子代静默继承父 onSpawnEffect=Effect.GruntIncome；子代非空才覆盖
[展开] children/TriggerGraphs 追加：chassis→+flag_bearer；graph.shared 精确去重（同图双挂不是合法组合）
[展开] 三级链 base_unit→veteran→hero：Health=150（最近父代胜），Team.Id=1（祖父代组件继承）
[整替通道] 子代组件顶层 "__replace": true → RegionVolumeCm 圆形整替为矩形，radiusCm 不残留嵌合体，标记装载期剥离；实例 Overrides 同通道（MapTriggerRegionTests.Override_ReplacesShape_WholeComponent 迁移后保持原断言）
[失败分支] extends 未知父 'missing_parent' → 启动失败指明双方 id；继承环(self/a↔b) → 启动失败
[spawn] 地图布阵 hero：Team.Id=9（子代覆盖）, Name=Template:InheritanceGrunt（父代继承）, 父代组件齐全；grunt 不受展开影响 Team.Id=1
[实例覆盖] partial.unit 布阵 Overrides 只写 WorldPositionCm.Value.X=900 → 落位 (900,20)：Y 继承模板值，不再整组件重述/吃默认 0
[幂等] 展开后 extends 字段清空，二次展开无变化
== 验收通过：装载期增量继承 + 实例覆盖字段级深合并 + __replace 整替通道合同成立（GasTests EntityTemplateInheritanceTests 12/12 + MapTriggerRegionTests 39/39）==
