# reconnect_recovery showcase 验收（Cucumber 描述；真机证据落 artifacts/acceptance/reconnect-recovery/）
# 注意：联机专项未验收——以下为单机模拟场景；真实网络注入通道落地后补跨进程场景。

Feature: 断线重连权威恢复
  作为玩家，断线后重连时世界应从权威 checkpoint 继续，而不是本地重置。

  Scenario: 断线期间权威继续
    Given 启动 preset reconnect_recovery_showcase_raylib 且已 [Checkpoint]
    When 我按 [Disconnect]
    Then authority tick 持续增长而 client tick 冻结，差值可见

  Scenario: 重连从权威恢复
    Given 处于断线状态
    When 我按 [Reconnect]
    Then 世界从权威 checkpoint 恢复，面板显示恢复来源与 digest，两线重新并走

  Scenario: 帧故障注入被拒
    When 我依次按 [Inject missing/duplicate/stale frame]
    Then 每次注入都显示真实校验拒绝消息（序列错误原文），无静默修复
