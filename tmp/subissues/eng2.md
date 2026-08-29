Part of #1321（W2 能力接线修复包；允许分多个 PR，一个 issue 跟踪）

## 目标

纠正"能力存在但未接线/接线错误"：方向光 + 阴影主宿主接线、水面/后处理互斥修正、错误策略统一合同定稿、schema 补 Sound、两处文档不实声称修正。本包为 W3 资产重构提供一条能正确出图的帧。

## 任务清单

1. 方向光 + 阴影接入主宿主：`RaylibDirectionalShadowMap.BeginFrame/EndFrame` 与 shadow draw 进入唯一帧执行路径（依赖 #1323 的新帧结构）；主宿主截图可观察到阴影；`raylib-render-code-shape.md` 改为与代码一致。
2. 后处理/水面互斥修正：`RaylibHostLoop.cs:624` `postProcessWorldFrame = !waterFboEnabled` 导致水面开启时调色 pass 熄灭——按新帧结构重排判定。
3. 错误策略统一合同定稿 + 负缓存失效语义：材质/skinned fail-loud 与 mesh/billboard Warn+跳过 二选一定稿成文；实现随 W3 落地。
4. `host_assets.schema.json` assetKind enum 补 `Sound`；schema 与运行时校验对齐，未知 kind 两边都明确失败；顺带核对 `presenters.schema.json` 的 assetBinding 合同是否需同步。
5. `gpu-skinned-instancing-and-offline-retarget.md` 的"FBX 仅离线转换"声称改为与 `RaylibModelFileConverter.cs:22` 运行时转换一致。

## 验收标准

- Given 主宿主场景存在可投影模型；When 渲染一帧；Then 阴影 pass 被调用、主宿主截图可见阴影、文档与代码一致。
- Given 水面 FBO 开启；When 渲染一帧；Then 后处理调色仍生效，无其他行为回归。
- Given `AssetKind.Sound` 合法配置；When schema 校验与运行时加载同时执行；Then 两者都接受；未知 kind 两边都明确失败。

## 依赖

前置：#1323。阻塞 #<ENG-3a>（错误策略合同）。
