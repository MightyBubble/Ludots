# Ludots Showcase Design

Use this skill whenever a showcase/demo/演示场景 needs to be designed, reviewed, or upgraded from a verification fixture into a user-facing demo.

## Rules

- Follow the eight-step derivation in order; each step lands in the matching section of the design document skeleton.
- A showcase answers "why is this capability worth it" in 60 seconds for someone who never read the code; a fixture only answers "does it run".
- Never copy concrete content from the built-in fog-of-war example: surprise moments, HUD metrics, knob lists must be re-derived from the target capability's own dynamic axis.
- Before delivery, self-audit against the seven anti-patterns (static fixture, numbers-only-in-assertions, preview drift, broken assets, no ablation, no user-orientation section, unexplained jargon).

## Outputs

A markdown design document with the agreed skeleton: 一句话与目标用户 / 主循环 / 消融对照 / 解释层 / 旋钮清单（≥4）/ 场景结构 / 门户资产与同源 / 反向 API 审计（含接口归属）.
