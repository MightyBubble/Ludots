# reconnect_recovery showcase 无头验收报告（#1207）

- 进程：Ludots.App.Web.exe（无头 Web adapter，preset reconnect_recovery_showcase_raylib，--adapter web）
- mods：LudotsCoreMod, ReconnectRecoveryShowcaseMod, AgentBridgeMod；map=reconnect_recovery
- 桥判活：health ok=true，pump 持续增长；UI 按钮 7 个全部 bridge ui.click 驱动

## 断线/重连证据（联机专项之单机模拟层，如实标注）

| 节点 | 结果 |
|------|------|
| Checkpoint | armed @ tick 4984，digest 8E0C615A57D0 |
| Nudge（活线） | Hero moved (live) |
| Disconnect | authority 持续走（5981），client 冻结（5051），差值 930 tick 高亮 |
| Reconnect | 从权威 checkpoint 恢复；两线重新并走（5175/5175）；面板明示「authority advanced 1723 ticks during the gap; client resumed from the authoritative checkpoint, not a local illusion」 |
| 恢复来源 | authoritative checkpoint + digest 常显 |

## 帧故障注入（真实 ReplayArchive.Validate 拒绝）

missing / duplicate / stale 三种注入均被真实校验拒绝、面板可读留痕、无静默修复：
`rejected: Replay firstFrameSequence must be zero for a checkpoint archive.`

已知限制（如实）：三种注入当前均被 header 前置校验（FirstFrameSequence）拦截，未触达缺帧/重复/过期各自的差异化错误文案——引擎校验链工作正常（fail-closed），但差异化语义展示待 showcase 构造参数修正后补录。

## 联机专项边界

本验收为单机模拟层（设计文档/面板/feature 三处一致标注）；真实网络故障注入与跨进程权威/客户端分离未验收。
