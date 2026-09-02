继续 #1398 收尾：把 D7/D8（冻结中的 PR #1431）救活合入，给 Case E 补上屏幕空间框。上一刀（#1441 桥接器退役直绑 action）已合 main，你是基于它继续。

## 背景（为什么 #1431 冻着）
#1431（分支 codex/1398-debt-d7-d8）做了两件：D7 Tap/Drag 阈值参数面、D8 presenter ScreenRect 屏幕空间锚定 kind + case_e 真屏幕框。当时因用户报「二次框选集合不变」被冻结——该 bug 根因已查明并修复（桥接器不转发松手，PR #1437），#1431 本身无辜。但它基于旧 main（#1428 时代），之后 main 发生了三连变更：#1437（删 tap_commit 图、删 Drag/Tap 绑定、box_commit 改听 BoxSelectEnd）→ #1441（桥接器退役、InputActionFired 事件删除、图 entry 改直绑 action、payload 捕获机制重做）。#1431 的 showcase 资产和引擎接线两头来撞，**必须逐文件以新 main 为准重构**。

## 任务
1. **拉 #1431 的两个提交内容**（git fetch origin codex/1398-debt-d7-d8 后 cherry-pick 或手动搬）：
   - D7：TapMaxTravelPixels/DragThresholdPixels 从编译期常量改为读 binding 的 Interactions 参数（缺省 6/8）+ Drag 折叠臂空隙合同 + 镜像守卫
   - D8：presenter BehaviorKind.ScreenRect=15 四处齐（定义/加载 fail-fast/解析/渲染 PresenterScreenRectSystem）+ 语义槽位 screenRect=18 + case_e 框视觉换真屏幕框
2. **D7 适配新 main**：注意 #1437 已删了 case_e 的 Drag/Tap 绑定（回归原案两裸 action），case_e 不再依赖阈值消空隙——D7 的参数面保留为引擎能力，showcase 侧不再引用；镜像守卫测试适配新绑定形态
3. **D8 适配新 main（重点）**：#1431 的 case_e 屏幕框数据链（CaseE.Pointer 动作属性 → press 角写入）依赖旧桥接器的 payload/动作目录——现在输入走直绑（entry.action + InputActionDef.firesOn），press 角数据源要接到新绑定层的指针捕获上（#1441 已把逐 action 指针窗口像素捕获改为扫已编译 action 绑定——确认 CaseE.BoxSelectBegin 的按下像素仍可被 presenter 参数链读到，读不到就把 press 角写入挪进 box_begin 图体经 ModifyAttributeSet，走图内 LoadEntryPayloadFloat 的 MapTrigger.PointerScreenX——该键在 #1441 后是否保留先查清，若已更名查新键名）
4. **渲染内核复用**：ScreenRect 的渲染实现必须吃现成 ScreenOverlayBuffer（引擎服务，GameEngine 注册；技能栏/旧拖拽框同源）——禁止新造渲染路径
5. **验收**：Case E 七步全绿 + 框视觉断言（屏幕空间矩形跟随拖拽、松手随 scope 消失）+ 真机截图（拖拽中蓝色矩形跟随指针、扩大到四单位、松手消失+选择环亮）+ D5/D7 交互回归 + PresenterBehaviorKind 测试族
6. **完成动作**：合入后在 #1398 勾 D7/D8；#1431 若被重构取代就 close 并注明由新 PR 替代

## 红线
- 开新分支新 worktree（codex/1398-d78-unfreeze），基于最新 main
- 引擎改动收敛：D7 只动 PlayerInputHandler 参数面；D8 只动 presenter kind 族
- PR #1431 的旧提交不许直接 rebase push（污染已冻结分支历史），新分支干净重构
- 全量测试污染 artifacts/（留底恢复只提交源文件）；CI 已知 flake：ThinkWave_10k 与 Benchmark_BuildTexturePlan（性能基准，负载下红）——失败先重跑一次，连续红且 diff 零交集才判 flake 并 PR 注明
- PR 中文正文按 shuorenhua 标准，Refs #1398（D7/D8），**只建不合**（等 owner）

## 完成定义
main 上 Case E：拖拽时屏幕上有真正的蓝色矩形框跟随（不是头顶指示牌），松手消失，七步全绿，#1398 债务清单 D7/D8 勾选归零。
