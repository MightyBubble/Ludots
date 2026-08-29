## 来源

#1315 的 per-seat 面板 showcase 真机验证（PR #1334）发现的引擎级时序缺口。

## 问题

`PlayerInputHandler.PressedThisFrame` 的边沿只覆盖**一个视觉帧**。真机上 pacemaker 会跳逻辑帧（视觉帧与逻辑 tick 非 1:1），消费方若在逻辑帧里读 `PressedThisFrame`，跳帧瞬间的按下边沿直接丢失——实测约四成注入丢失；headless 1:1 Tick 的测试不暴露此问题。

showcase 的规避（已合并）：改读 per-seat 通道的 `PressedThisTick`（accumulator 把帧边沿 OR 进 tick 冻结快照），全量送达。

## 风险面

任何在逻辑帧节奏里消费按键边沿的通用消费方（不止面板热键：技能触发、UI 确认、debug 快捷键等）在真机 pacemaker 跳帧下都可能丢边。这是「单帧语义」与「tick 冻结快照」两套边沿语义并存导致的坑。

## What to build

- 裁决边沿语义合同：按键边沿的权威口径 = tick 冻结快照（`PressedThisTick` 家族），还是视觉帧（`PressedThisFrame`）？
- 若 tick 为权威：审计 `PressedThisFrame` 的全部消费方（src/Core + mods），逐个迁移或在 handler 层把帧边沿 OR 进 tick 快照；`PressedThisFrame` 保留给真正的视觉帧消费方（如 UI 层）并文档化两者边界
- 回归测试：模拟 pacemaker 跳帧（视觉帧 ≠ 逻辑 tick）下边沿不丢

## Acceptance criteria

- [ ] 边沿语义合同写入输入相关合同页（含两 API 的适用层）
- [ ] 逻辑帧消费方零丢失（跳帧模拟测试）
- [ ] showcase 的规避读法与最终合同一致（不一致则迁移 showcase）
