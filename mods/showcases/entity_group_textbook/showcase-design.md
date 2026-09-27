# Entity Group 教科书 showcase 设计（issue #1540 / #1554）

> 状态：**设计完成，尚未实现实机可玩**（八步设计齐备；实现清单与阻塞见文末。按
> `ludots-showcase-design` 状态词纪律，在实机验收通过前不宣称"可玩交付完成"。）

## 一句话与目标用户

同一份营地模板，摆两处、各自魔改、关系保留——给"想往大世界里摆一坨 POI"的地图作者看。

## 主循环

- **谁改变世界**：玩家按快捷键（1/2/3）给海滨营/雪山营的哨兵改关系忠诚度、建边、删边
  （trigger 图 `RelationshipSetMetric / EnsureLink / RemoveLink`，全部现有节点）。
- **用户看到什么变**：哨兵头顶 WorldText 与关系 HUD 数值实时变（图监听**新预设事件**
  `RelationMetricChanged / RelationLinkAdded / RelationLinkRemoved` → `SetParam`）——
  本 showcase 同时是 #1554 事件链的端到端验收场。
- **惊喜时刻**：按下删边键，"哨兵—营火 WorksFor"边消失、HUD 边数 -1、头顶文字变灰
  ——玩家亲眼看到"关系是活数据，不是装载期快照"。
- **消融对照**：两营地并排——alpine 不带 override、harbor 带 override（宝箱改名、锚点挪位）
  ——"同一模板、两份差异"一眼可见；按键切到"模板原样"视图即关闭实例差异（消融实例轴）。

## 解释层

- HUD：`边数（按类型）/ 哨兵忠诚度 / 两营地实体树计数（26 地址路径）`；
- 颜色编码：WorksFor 边绿色随 loyalty 0–100 走热力阶梯；树层级按深度缩进 WorldText；
- 图例固定左下角：绿线=WorksAt、灰=已删、括号=localId。

## 旋钮清单（≥4，全部运行时）

| 旋钮 | 范围 | 回答什么 |
|---|---|---|
| 忠诚度滑条 | 0–100 | 关系 metric 是活的吗 |
| 建边/删边 | WorksFor × 哨兵 | 关系可在运行时增删吗（Relation* 事件闭环） |
| 实例差异消融 | on/off | override 到底改了什么 |
| 视图切换 | 世界视图 / 树视图 | 组的层级结构长什么样 |

## 场景结构

主演示 = eg_camp_site（双营地）；子场景 = 纯树视图、纯关系面；首屏引导 = 一行按键说明。

## 门户资产

封面取"删边瞬间"帧；预览页从 `templates.json + eg_camp_site.json` 生成（同源自检：改营地
配置预览随变）；文档 = 本设计 + `gitbook/architecture/entity-group-and-override.md`。

## 反向 API 审计（showcase 需要而现可能缺的接口）

| 接口 | 归属 |
|---|---|
| 图 op `RelationshipSetMetric/EnsureLink/RemoveLink` | ✅ 已有 |
| 预设事件 `Relation*` ×4 + payload schema | ✅ 本次 PR #1566 |
| WorldText 的**关系 metric 文本模式**（现只有 attribute/param 模式） | 本次交付（Core presenter 小扩展）或用 `SetParam`+param 模式绕（零代码路径） |
| 关系边**世界空间连线渲染**（现 presenter 无 Line/Edge behavior） | 后续 issue（HUD 数值先行，连线可视化二期） |
| 运行时实体树查询（按 localId 路径枚举） | MapLoadEntityIndex 已有，图侧查询节点后续 |

## 交付边界与完成判据

- 入口：launcher `entity_group_textbook`（raylib preset）+ registry T2→可玩（binding/preset/
  acceptanceTest/screenshot 齐备后升 T1）。
- 闸门：`ludots-showcase-design` 可玩交付闸门 9 项 + agent-bridge 实机验收（/health pumpCount、
  交互证据、实机截图）+ BDD UAT。
- 当前阻塞：①presenter 资产与图资产未写；②launcher preset 未配；③实机验收未跑。
