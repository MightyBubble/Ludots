# 家族方案 ③ 名单筛选与汇总（14 op）

> 像素级复核修正：①TagNone 实际亮 11（多出的 1 是 caster 恒亮血条）；②14 张 poster 的血条簇逐像素一致——**"亮/暗"通道当前完全不渲染**（DiscloseHealth 无条件覆盖门控），本家族把语义主通道放在 DebugDraw 圈/徽/线/台；③presenter 按 template 配色：友军11 是红方块，FilterTeam 的"红蓝对照"目前不成立。

### 家族统一拍式（两拍框架，14 op 全套用）
DrawOverlay 按 Wave 奇偶切换：**偶=满员拍**（全体候选黄圈+徽章浮现）、**奇=结果拍**（命中黄圈+被滤者细灰圈残影）。聚合数值 op 结果拍圈退场、算式台数字接管。指挥脚下**席位双圈**（≠点名单圈）替代 caster 恒亮。

### QueryFilterTemplate｜B(P8+P7)｜S
- 零字幕画面：满员拍 12 单位全圈；结果拍只剩两个 teal 矮个子（侦察兵）保持黄圈，10 个高个红方块退灰影。
- 文案：title「只挑侦察兵」beat「全场先亮一圈，再只剩两个矮个子亮着。」detail「矮个侦察兵留圈{count}个，高个士兵全退成灰影。」

### QueryFilterTeam｜B(P8+P7)｜S
- 现状：友军11 按 template 渲染成红方块，阵营对照假的；指挥恒亮推翻"友军被滤掉"。
- 零字幕画面：满员拍全圈；结果拍 10 个红方块留黄圈，蓝方块（友军11 改 GraphOps.Ally 模板）+teal 侦察兵退灰影；指挥只有席位双圈。
- 改动：`_fields/query.json` unit10 template→`GraphOps.Ally`（已核对不影响任何 op 命中集合与聚合值）；caster 不再写 ActorHudLit。文案：title「圈出对面十个」beat「红的一排留圈，蓝的退成灰影。」detail「红方{count}个个个有圈，蓝方两个全退成灰影。」

### QueryFilterAttributeRange｜A（对齐拍式）｜S
- 零字幕画面：满员拍全圈；结果拍短血条 5 人留黄圈、长血条 7 人退灰影——圈住那组血条清一色见底，证据链完整。
- 文案：title「只圈残血的」beat「全场先亮一圈，再只剩短血条的留着。」detail「血量不超过{threshold}的{count}个短血条留圈，长血条退成灰影。」

### QueryFromCollection｜B(P3+P1+P8)｜M
- 零字幕画面：指挥席右侧立**白色名册板**（2×3 六格对应 6 成员）；结果拍板→6 名成员各拉一条黄色点名线，在册 6 人留圈，册外 6 人退灰影。
- 改动：DrawOverlay 按 `Collections[0].Members` 排格连线；caption 补 rest 值。文案：title「照着名册点名」beat「名册板六格点亮，点名线拉向场上六人。」detail「名册上{count}人被点名线牵住，不在册的{rest}个退成灰影。」

### AggAverageAttribute｜B(P4+P11)｜M
- 现状：带两条与求平均无关的红线（最强/最弱）；"字幕报平均"元语言。
- 零字幕画面：场边三格**算式台**——左格人数「13」、中格合计「800」、右格结果「62」依次点亮；结果拍全员圈退场。
- 链路修复：QueryNodeDriver.ResolveExtremes 对 IsAggregateValueOp 删 ScanInRangeExtremes 与双红线；FillCaptions 补真实 sum。文案：title「全场平均血量」beat「十三个人的血条凑上台面，台面亮出平均数。」detail「{count}人生命合计{sum}，除完平均{avg}。」

### QueryAllMapEntities｜B(P1 扫描弧+P11)｜M
- 零字幕画面：以指挥席为圆心的**黄色扫描弧**逆时针扫过全场，扫过的单位逐个亮圈+头顶 HP 浮标；席旁**计数牌**从 0 翻到 13——动作（扫）与结果（全亮+13）都在画面里。
- 改动：按单位方位角排序驱动弧进度。文案：title「把场上的人全点名」beat「扫描弧从指挥席扫过全场，点到谁谁亮。」detail「扫完一遍，场上{count}人个个有圈，计数牌停在{count}。」

### AggSumAttribute｜B(P4)｜S
- 零字幕画面：算式台两格——人数「13」→合计「800」；800 是 13 根血条一根根收进台子的和，数字与满场血条同框。
- 链路修复：同 AggAverage 删双红线。文案：title「全场生命合计」beat「十三根血条一根根收进台面，台面亮出总数。」detail「{count}条血一起上台，合计{sum}。」

### QuerySortByAttribute｜B(P6+P1)｜M
- 零字幕画面：结果拍按血量逐个亮圈定格"检阅态"：敌军8 头顶**三道杠角标**、敌军1 两道杠、第三名一杠；一条黄色箭头链从第一名指向第二名再第三名，沿途血条肉眼可见变短——角标高度+箭头方向+血条长度三重冗余。
- 改动：HitTargets 本身是排序后列表，按索引画角标即可。文案：title「按血量从厚到薄排队」beat「最厚的顶着三道杠，箭头顺着血条一路排下去。」detail「头名{label}血{hp}顶着三道杠，箭头一路指向更薄的血条。」

### AggMinAttribute｜B(P4 一格台)｜S
- 现状：detail"对应{label}"越权（节点只输出 float，label 是驱动旁路扫的）；海报与 EntityBy 版不可辨。
- 零字幕画面：算式台单格「最低」亮出「0」；画面证据=场上唯一空血条（敌军7）与台面数字同框。**无圈、无线、不点名——数值版的视觉签名就是"只有台面、没有人被指名"**，与实体版彻底分家。
- 链路修复：删 WeakestIndex 红线与 label 回退。文案：title「全场最低血量」beat「台面翻出最低一格，亮出的数短得像那条空血条。」detail「全场最低生命{min}，没有一条血条比它更短。」

### AggMaxAttribute｜B(P4 一格台)｜S
- 同上镜像：单格「最高」亮「150」，唯一顶格血条同框。
- 链路修复：删 StrongestIndex 红线与命名。文案：title「全场最高血量」beat「台面翻出最高一格，亮出的数顶着满格血条。」detail「全场最高生命{max}，没有一条血条比它更长。」

### QueryFilterTagNone｜B(P2+P8)｜S
- 现状：反直觉（0 血的亮、40 血的暗）且"阵亡标记"无载体。
- 零字幕画面：敌军9 头顶**灰白菱形阵亡徽**（全场唯一）；满员拍全亮+徽章浮现；结果拍戴徽者圈灭退灰影，其余 11 留圈——徽章直接回答"为什么 0 血的还亮着：筛的是徽，不是血"。
- 改动：驱动按 Actors[i].Tags 现画，零 schema 变更。文案：title「摘掉阵亡徽的留下」beat「戴阵亡徽的退成灰影，没戴徽的留着圈。」detail「唯一戴阵亡徽的退成灰影，其余{count}个都留着圈。」

### AggMinEntityByAttribute｜B(P2 点名徽+P7+点名线)｜S
- 零字幕画面：结果拍其余 11 人退灰影，敌军7 独留黄圈+头顶**红色菱形点名徽**+黄色点名线从指挥席拉到其头顶；血条空、HP 浮标「0/150」。点名徽=实体版统一签名（数值版永远没有徽）。
- 文案：title「点名最残的那个」beat「全场退成灰影，空血条那个被点名徽钉住。」detail「被点名的是{label}，血{hp}，点名线从指挥席拉到他头顶。」

### AggMaxEntityByAttribute｜B(P2+P7+点名线)｜S
- 镜像：敌军8 独留+点名徽+顶格血条「150/150」。
- 文案：title「点名最能扛的」beat「全场退成灰影，满血条那个被点名徽钉住。」detail「被点名的是{label}，血{hp}，血条顶格还顶着点名徽。」

### QueryFilterTagAny｜B(P2+P8)｜S
- 零字幕画面：敌军1..9 头顶各一枚**红色菱形敌徽**，teal 的敌军10（同队无徽）头顶空空；结果拍九个戴徽者留圈，敌军10、蓝方两个退灰影——"同队没徽也灭"写在画面上。
- 文案：title「戴敌徽的全圈出来」beat「头顶红徽的九个留圈，没徽的退成灰影。」detail「头顶带红徽的{count}个全被圈住，同队没戴徽的也照灭。」

## 家族小结
- 家族基建（一次性 L）：两拍框架+灰影+席位圈+菱形徽章绘制器+数值浮标/算式台（道具实体走 Stage.Spawn，不进地图查询计数；数字用 WorldHudTextMode.AttributeCurrent 现成枚举）+点名线取代 DrawAggroLine。
- 场景：必改 1 处（unit10→GraphOps.Ally）；可选加固（默认不做）：Dead 标签挪到敌军7 让"戴徽=躺平"三重一致（代价：AttributeRange 5→4 需同步断言）。
- 链路修复：LightCasterAndHits 去恒亮；ResolveExtremes 删红线/label 越权；average 补真实 sum；另开独立 issue 排查——静态帧 knowledge 血条门控无差异（retained presenter 是否绕过属性遮罩）。
- 录制注意：poster 取末帧需落在结果拍（Wave 奇偶对齐或锁定末帧）。
- 统计：基建 L×1；per-op S×10、M×4。无 C 档。

