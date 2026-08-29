# Epic #990 家族批次公共工单（pi 执行者必读）

你在 C:\001_AI\LudotsProd\.worktrees\epic-990（Ludots 仓库，Arch ECS，分支 epic/990-zero-caption-gallery）。任务：按家族规格文件实现一批 graph 节点画廊 op 的零字幕重设计。

## 已有基建（直接复用，不要重造）

1. **视觉原语**（GraphShowcaseStagePresenter.cs，静态类直接调）：`DrawDirectedLine(buffer,ax,ay,bx,by,thickness,color,arrowStart,arrowEnd)`、`DrawDashedDirectedLine(...)`、`DrawBadge(buffer,x,y,BadgeKind.{Flag,Eye,Bell,Diamond,Flame,Check,Cross,Ring},color,scale)`、`DrawGhostCircle(buffer,x,y,radius,color)`、`DrawGhostSegment(...)`、`DrawDigit/DrawNumber(buffer,x,y,value,heightMeters,color)`、`DrawPanelBox(buffer,x,y,w,h,slots,color)`、`DrawRankPips(buffer,x,y,rank,color)`、`DrawArcArrow(...)`、`DrawThickOutlineCircle(buffer,x,y,radius,outer,inner)`。
2. **真结算**：driver 入队 EffectRequests 后 runtime 自动结算（headless 走引擎 tick），目标 ActiveEffectContainer 有真实活跃效果——画徽章/光环前**必须真实读状态**（ActiveEffectContainer/Relationships.HasFlag/HasLink/TagOps.HasTag），禁止无条件画。
3. **试点样板**（commit 2b609d5bcb，用 `git show 2b609d5bcb --stat` 和 `git show 2b609d5bcb -- <file>` 学模式）：
   - RelNodeDriver：QueryIncoming（灰底全貌+亮黄指向箭头）、SetFlag（真实读旗画 Flag 徽章）
   - QueryNodeDriver：SortByAttribute（RankPips+指挥箭头）
   - BlackboardNodeDriver：WriteBlackboardFloat（PanelBox+DrawNumber+回读 Check 勾）
   - LinearNodeDriver：MulFloat（算式台+graphSettled 真结算模式——图尾巴 featured→NegFloat→ModifyAttributeAdd，driver applyTo:"graphSettled" 不直写血条）
   - ScriptNodeDriver：Yield（茶杯水位直读 `_ints[0]`，头顶 HUD 隐藏）
   - EventNodeDriver：SnapToNearestGraphEdge（GhostCircle+DirectedLine 下落箭头）
   - AttrNodeDriver：ApplyEffectTemplate（真实读 ActiveEffectContainer 画 Diamond 徽章+光环）

## 硬规则（违反任何一条=返工）

1. 只改规格里列出的文件：本家族 driver、本家族 vignette JSON（assets/Vignettes/）、需要的图分片（assets/GAS/graphs/*.json，**单元素数组格式**）、相关验收测试 pin。**不改**：GraphShowcaseStagePresenter.cs、其他家族 driver、生成器脚本、Maps、showcase.registry.json、launcher 配置。
2. **禁止**：fallback/向后兼容开关/新 enum/preset 开关/平行管线；行为变化用既有 op 在图 JSON 里组合（图尾巴模式照抄 MulFloat.json）。
3. **禁止**：git commit、git push、跑生成器（generate-*.py）、录屏（record-*.py）——协调者统一做。
4. 文案三件套（title/beat/detailTemplate）按规格文件给的新文案逐字写入 vignette；assertDetailContains 与新 detail 一致；**beat 禁止描述画面里没发生的事**；禁"字幕报X"元语言；"示意条非结算"免责仅在该 op 仍走示意路径时保留（改真结算的必须删）。
5. 改了 vignette 文案后，同步 grep 验收测试（src/Tests/GasTests/Production/GraphOpsNodeGallery*.cs）里该 op 的 title/detail 断言 pin 并更新；**不得**削弱断言强度（数值/行为断言保留或加强）。
6. 每完成一个 op 后增量跑：`dotnet test src/Tests/GasTests --filter "FullyQualifiedName~GraphOpsNodeGallery" --nologo`，最终必须全绿（当前基线 110/110；你的家族可以新增测试）。遇 MSB3027/testhost 文件锁：等 30 秒重试（最多 5 次）。
7. 规格里标注 ⚠️ 的语义级修复（文案改真/接线修复）优先做；标 C(演后果) 的按规格做；规格与现实冲突时**停下来在最终报告里说明**，不要自由发挥。
8. 图分片改动的语法照抄同目录现有图（controlEdges/valueEdges 端口模式）；改完必须过 ExistingVignettes_CompileWithFeaturedOp（featured opcode 仍在）。
9. 代码注释纪律：命名到位不写注释；只写非显然约束。不写"本次修改"类痕迹。

## 产出报告（最后输出）

逐 op 一行：`op名 | 改了什么(画面/链路/文案) | 测试结果`；家族总结：新增测试数、遗留问题、与规格的偏差及原因。
