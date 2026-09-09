# Activity Dispatch Showcase

> 系统文档见 [叙事内容运行时 Wiki · Activity](../../../../gitbook/reference/narrative-runtime-wiki/activity.md)；本 README 讲本 showcase 的玩法与改法。

三条 Activity 派发路径的可玩演示 + 通用「事件面板」（PanelKit panelType `activity`）。

## 玩家视角

启动后右侧是事件面板，顶部三条触发轨道按钮：

- **forced**：弹出「补给超限」拍板层。选项四种形态一次看全——基础选项（永远可选）、普通可执行、
  可见但锁定（写明原因：`execute_condition_failed:world.subject_attribute`）、Gate 未通过所以
  整个不出现的「向盟友求援」。确认后弹层关闭，历史区出现该实例与所选选项 id。
- **pooled**：从候选池 `activity.showcase_pool`（商队 60 / 天象 40）按命名流确定性抽取。同一流状态
  两次触发抽中同一候选；抽中自动候选（天象）时不弹层，直接进历史并标记自动结算。
- **automatic**：不弹层，直接归档为「城·河口归属切换」通报，历史区带 automatic 标记。
- **消融旋钮**：「切换议事会余力」把 scope host 的 Health 在 60/20 间切换，forced 弹层里
  「展开前进补给营地」当场在锁定↔可执行之间翻转——条件三分是活的，不是截图。

面板下方「审计侧」实时显示当帧 presentation cue（Presented / OptionBlocked / Resolved /
AutomaticSettled / AdmissionRejected），拒绝原因可见。

## 配置作者视角（0 编码）

新增一条演示活动只需要改 JSON，不需要写 C#：

- `Assets/Activities/activities.json` — 活动定义（派发/重复策略、选项、条件、效果引用）。
  效果当前只有已登记的 `task.create`（结算即创建追踪任务）；条件有 `world.subject_attribute`。
  未登记的 `effect_key` / `condition_key` 加载期整包拒装，错误信息带键名。
- `Assets/GAS/graphs.json` — 触发轨道：自定义地图事件 → `OfferActivity` op → 活动定义 id。
  换事件、换活动、加过滤都只改这段连线。
- `Assets/Events/custom_events.json` — 轨道事件声明。
- `Assets/Rng/distributions.json` — 候选池权重与命名流种子（确定性抽取）。
- `Assets/PanelKit/panel_manifest.json` + `profile.*.json` — 面板绑定与文案。

事实源说明：示例定义的 `source_key` 目前声明为 `task.state_changed`（生产环境唯一已登记的
fact source）；本 showcase 的触发走 graph 派发轨，不走信号订阅轨。信号轨（`IntakeSignal`）
等待 #775 的 Source 合同落地后再启用。

## 入口

launcher preset `activity_dispatch_cef_raylib`（CEF 浏览器运行时，依赖 `LudotsCoreMod`）。
