# 叙事内容运行时 Wiki

叙事内容是玩家"被按在椅子上作答"和"被牵着走"的那部分：活动要你拍板，任务要你追进度，剧情演出讲给你听，Record 负责把已经发生过的事留下来。这几类内容共用一套纪律——运行时真相只存实体一处、界面只做只读投影、效果只能调已登记的能力键。

每一页两段视角合一：先讲玩家看到什么，再讲 mod 作者怎么配。合同细节在对应 issue，本 wiki 只做导览。

## 家族分组

### 拍板（一次决定，当场结算）

- [Activity v5 设计](activity-v5-design.md) —— 图化决策活动、标签门、投票和哑记录的设计稿。
- [Activity / Task 配置手册](activity-config-cookbook.md) —— 13 个可抄案例，先写配置再接 Graph。


### 追踪（跨周期的持续目标）

- [Task 任务](task.md) —— 挂在目标列表里的条目，有进度、完成条件、失败条件。

### 演出（讲给玩家听）

- [Story · Dialogue · Sequencer 剧情与演出](story-runtime.md) —— 剧情线、多页对话、脚本时间轴。合同在既有的 Story Runtime 文档，本页是入口和分工。

### 历史（曾经发生过）

- [Record 系统设计](record-system-design.md) —— 通用事件日志、审计轨迹、按类别查询。

## 四者的分界线（内容作者先背这个）

| 内容问自己 | 归属 |
|---|---|
| 玩家现在必须选一个，选完就结束？ | Activity |
| 要跨多个周期才能完成、要挂在列表里追？ | Task |
| 要多页对话、演出时间轴？ | Dialogue / Sequencer |
| 选项的后果需要长期追踪？ | Activity 的结算效果去创建 Task（`task.create`） |

引擎事件总线上的 Engine Event 不属于这个家族——那是系统之间的通知，玩家不该直呼其名。把三者混称会写出跑不通的配置，详见 Activity 合同 issue #773 的术语节。

## 总装与验收

- 活动拍板台 showcase：`activity_dispatch`（三条派发路径可玩，内容纯 JSON），启动 `activity_dispatch_cef_raylib`
- 叙事总装 showcase：`narrative` / `narrative_frontend`（Story/Dialogue/Sequencer/Task 串成一场戏）
- 存档：三类内容的运行时状态都在通用存档系统里（domain `activities` / `task` / `dialogue` / `sequencer`），见[通用存档系统](../../architecture/save-system.md)

## 合同索引（SSOT）

| 主题 | 合同在哪 |
|---|---|
| Activity 术语与运行时合同 | issue #773（子卡 A1–A10） |
| Activity v5 设计定稿候选 | [Activity v5 设计](activity-v5-design.md) |
| Activity / Task 配置案例 | [Activity / Task 配置手册](activity-config-cookbook.md) |
| Task 运行时（Quest 退役重做） | issue #774（已关闭，代码已落地） |
| Record 通用事件日志设计 | [Record 系统设计](record-system-design.md) |
| Source/Selector/Condition/Effect 共享合同 | issue #775 |
| 程序总览（Activity/Task 实体化 + 大战略示例） | issue #830 |
