# Tech Debt Report: 2608-maploaded-before-host-compose

Date: 2026-08-23
Reporter: NavGate 真人试玩（物理按键全失效排查，c433a4676）
Owner: 引擎启动/宿主组装线
Severity: P1
Scope: Cross-layer（Core 事件时序 → 宿主 Composer → 所有 mod 的 MapLoaded 合同）

## Trigger

- Scenario: 任意 mod 在 `GameEvents.MapLoaded` 里注册/推送输入上下文（`PlayerInputHandler.PushContext`）或消费宿主服务。
- Entry point: `GameEngine.MapLoadLifecycle` 触发 `MapLoaded` → mod `context.OnEvent` 处理器。
- Repro: NavGate showcase —— MapLoaded 中 `GlobalContext.TryGetValue(InputHandler)` 静默落空，输入上下文从未推送，物理按键全失效；同一动作经桥接 action 级注入（直写 `_injections`，绕过上下文）却"正常"，掩盖缺陷至真人试玩才暴露。

## Evidence

- 宿主侧注册时序：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs:95` —— `SetService(InputHandler/SyntheticInput/HostFrameCapture)` 发生在 Composer 组装期（启动序列中）；
- 事件侧：`src/Core/Engine/GameEngine.MapLoadLifecycle.cs:431` —— `MapLoaded` 可在上述注册完成前触发（实测两轮启动，入口推送日志均未打出，而同一处理器后半段的系统激活日志打出了——输入块被 `TryGetValue` 静默跳过）；
- 相机为何幸存：composer 创建 handler 时按 `config.StartupInputContexts` 推默认上下文（RaylibHostComposer.cs:85-93），不依赖 MapLoaded；
- 掩蔽链：`PlayerInputHandler.PushContext` 对未注册上下文静默 no-op（PlayerInputHandler.cs:53-56）+ mod 输入块 TryGetValue 失败静默跳过 + 桥接注入绕过上下文层 → 三层静默叠加；
- 当前缓解（showcase 内，非根治）：NavGateTimelineSystem 每帧 `ReferenceEquals` 检测 + 幂等重推（c433a4676）。

## Impact

- User-visible impact: 所有依赖 MapLoaded 消费 InputHandler/宿主服务的 mod 在首个地图加载时拿不到服务，且无任何报错。
- Correctness/stability risk: mod 与引擎启动顺序隐式耦合；换宿主（Web/未来 Unity）时序不同则行为漂移。
- Blast radius: 每个 mod 的 MapLoaded 合同。

## Fuse Decision

- Mode: explicit-degrade（showcase 层幂等重推，已落地）
- Reason: 引擎时序调整涉及启动序列重排，需独立验证，不在 showcase 分支抢做。
- Observability: 入口推送成功日志 `[NavGate] input context pushed`（修复后可观测到重推补位）；本报告。

## Containment and Follow-up

- Immediate containment: NavGateTimelineSystem 每帧幂等重推（已推送，c433a4676）。
- Permanent fix direction:
  1. 明确 MapLoaded 合同：事件必须在宿主 Composer 完成核心服务注册（InputHandler/SyntheticInput/HostFrameCapture）之后触发——要么启动序列重排，要么把已有的 deferred-MapLoaded 机制（GameEngine.cs:2235 一带）扩展到"宿主未就绪"条件；
  2. `PlayerInputHandler.PushContext` 对未注册 contextId 改为抛错（对齐仓库 fail-loud 原则，杜绝同类静默）；
  3. 同类静默审计：GlobalContext.TryGetValue 失败即跳过的 mod 模板改为显式日志或重试。
- Target milestone: 引擎启动线下一迭代；与 #1081（桥接 P2 快照总线）的"引擎线程/时序合同"项同批评审。
