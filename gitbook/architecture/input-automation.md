# Input Automation

Input Automation 是 Ludots 的通用键盘鼠标操作能力。它服务 UAT、录屏、AI 驱动编辑器、可玩 showcase 和回归测试，不属于 Raylib、Web 或某个 Mod。

## 边界

- Core 定义脚本语义、时间线、键鼠状态和 frame events。
- Host adapter 只负责把 frame events 投递到自己的窗口或 UI surface。
- 业务 Mod 只写 scenario，不直接操作 host 私有 API。
- 录屏 harness 只消费 Input Automation，不拥有输入语义。

## 正式入口

- `InputAutomationCommand`：一条操作命令，例如 pointer move、click、drag、scroll、key stroke、text。
- `InputAutomationPlayer`：按 frame 推进脚本，产生稳定的 pointer/key 状态和 frame events。
- `InputAutomationBackend`：包裹任意 `IInputBackend`，把自动化输入叠加到真实设备输入上。
- `CoreServiceKeys.InputAutomationPlayer`：host 和 scenario 共享当前 automation player 的正式服务键。
- `LUDOTS_INPUT_AUTOMATION_SCRIPT`：通用脚本入口；host 不应新增自己的平行键鼠脚本变量。

## Script Shape

```json
{
  "commands": [
    { "kind": "PointerMove", "frame": 0, "durationFrames": 8, "x": 100, "y": 120, "endX": 220, "endY": 140 },
    { "kind": "PointerClick", "frame": 12, "durationFrames": 2, "x": 244, "y": 132, "button": "Left" },
    { "kind": "KeyStroke", "frame": 20, "durationFrames": 2, "key": "W", "modifiers": 1 },
    { "kind": "Text", "frame": 24, "text": "map_001" },
    { "kind": "PointerScroll", "frame": 30, "x": 480, "y": 300, "deltaY": -120 }
  ]
}
```

## Host Adapter Contract

Every visual host should implement the same two steps:

1. Wrap the physical backend:
   `effectiveInputBackend = new InputAutomationBackend(physicalInputBackend, player)`.
2. On each visual frame, call `player.SetFrame(frameIndex)` before UI input routing, then convert `player.FrameEvents` into the host's native UI/browser events.

Raylib and Web already follow this pattern. Future hosts such as UE should keep the same shape: engine/game input reads through `IInputBackend`, and surface/browser input receives the same frame events converted by the adapter.

## Non Goals

- Input Automation does not define screenshot or video capture.
- It does not own Live Map Editor authoring APIs.
- It does not bypass `InputConfigPipeline`, `PlayerInputHandler`, `UIRoot`, or host browser bridges.
