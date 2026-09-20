== Entity Template Uses 验收 ==
[装载] templates.json 同 id 合并 → extends/uses 折叠 → 校验：block.mortal(块) + block.selectable(块) + archer(uses 二者) 就位
[折叠] 优先级一条规则：声明越靠后优先级越高，自身 components 永远最高——block.a 写乙=100, block.b 写乙=220 → 220（后声明者胜，逐块 merge 进 template 的错误实现会让 block.a 恒胜，已用例钉死）
[折叠] 自身最高：块写 Health=999 + 自身 Health=220 → 220；块独有组件 Team 照常继承
[合并] 字段级：block.a 写 base.Health=100/current.Health=80，block.b 只写 base.Health=220 → base=220, current=80（未提及字段留底继承）
[叠层] extends 父打底 → uses 覆盖 → 自身最后：unit H=50 → block.elite H=150 → hero H=300；champion 自身静默 → 150（块胜父），Mana=40/Team=1（父打底继承）
[钩子] onSpawnEffect/initialInteractionContext 后应用且非空才覆盖：recruit 取后块 Effect.B；hero 自身 Effect.Hero 最高
[块链] 块自身带 extends：block.tough 先展开自己的继承链（H=100→200, Team 随链）再参与折叠
[追加] children/TriggerGraphs 跨块追加：chassis→flag_bearer→rider；graph.shared 精确去重（同图双挂不是合法组合）
[整替] 块组合下 "__replace": true：block.zone 圆形 + block.yard 矩形整替 → radiusCm 不残留嵌合体，标记装载期剥离
[失败分支] uses 未知块 'missing_block' → 启动失败指明双方 id；uses+extends 混合环(a extends b, b uses a) / 块互 uses(x↔y) / 自 uses → 启动失败 cycle detected
[幂等] 展开后 uses 清空，二次展开无变化；被引用块内容不被折叠污染（mortal 直接布阵无 Team，selectable 的值不回流进块）
[冲突报告] 同组件多源写入留痕：AttributeBuffer 覆盖链 "block.a -> block.b -> self" 记入 ConfigConflictReport；单一写入者不记录
[spawn] 地图布阵 archer(Team=9 自身胜块值 2) / recruit(Team=2 自身静默块值生效) / block.mortal(无 Team, 块不受使用者影响)；Name/WorldPosition/Facing 自块继承
== 验收通过：组件组 uses 折叠 + 后声明胜优先级 + 冲突覆盖链可见成立（GasTests EntityTemplateUsesTests 15/15 + EntityTemplateInheritanceTests 12/12 + MapTriggerRegionTests 39/39）==
