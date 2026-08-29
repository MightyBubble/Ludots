Part of #1321 · 实现 #1326（W2 能力接线修复包）

**stacked on #1352（含 W0/W1a 提交，前序 PR 合并后本 PR diff 即为纯 W2）**

## 改了什么

1. **方向光阴影主宿主接线**（能力存在但从未接线的核心修复）：`ShadowDepth` pass 进入唯一帧执行者——`RaylibDirectionalShadowMap` 惰性构造，`BeginFrame(太阳方向, 相机目标, 48m)` → 即时/蒙皮/车道三类投射物灌深度 → `EndFrame`（try/finally 成对收尾）；`ApplyFrameLighting` 三个接收端（primitive/terrain/heightmap）带入 frameShadow，夜间自动跟随月光方向。门控 `RenderDebugState.DrawShadows`（默认开），宿主环境钮 `LUDOTS_RAYLIB_SHADOW=0` 可关（同时用于本 PR 的 A/B 取证）。
2. **水面/后处理互斥修正**（功能 bug）：旧代码 `postProcessWorldFrame = !waterFboEnabled`，水面帧直接丢调色。根因是水面 `EndTextureMode` 切回默认帧缓冲会丢后处理 RT 绑定；修法是 `BeginWorldTexture` 挪到水面与阴影 pass 之后开启。水面帧从此保留曝光/对比/饱和/暗角。
3. **schema 对齐**：`host_assets.schema.json` assetKind 补 `Sound`（loader 已支持且未知 kind 抛错——两边一致）。
4. **错误策略合同定稿**（实现随 #1327）：`raylib-render-code-shape.md` 新增章节——宿主资产装载失败一律 fail-loud，负缓存必须可失效；mesh/billboard 的 warn+skip 登记为待收敛偏差。
5. **文档修正**：帧内数据流改为唯一帧执行者现实（W1a 后的真话）；FBX 声称改为运行时转换现实。

## 验证证据

- adapter 全量 203/203；顺序矩阵扩到 >500 组合（新增水面+后处理**共存**断言、阴影顺序不变量）。
- 真实宿主 E2E + **开/关 A/B 像素差分**（阴影落地的决定性证据）：atmosphere mod 6.11% 变化像素全部集中在地形/实体带、天空与开阔水面为零（结构化投影，非全画面漂移）；blacksmith mod 0.13% 变化精确落在实体脚下区域。
- **双视觉审核均判符合预期**：codex（实体投影清晰可见、调色变化出现、无阴影痤疮/元素缺失）+ pi（调色两处确认、方向性明暗符合、无意外回归）。

## 复核状态（重要）

视觉复核双完成。**代码级双复核因供应商余额耗尽未完成**：codex 代码复核中途 402（aicodemirror 余额不足）、pi 代码复核同样 402（anthropic 余额不足）。提交前已由实现方按 14 项风险清单自查（GL 状态次序/RT 生命周期/门控一致性/异常收尾）。**建议充值后由任一评审补跑代码复核再合并**；复核简报存于 `tmp/w2-review-brief.md`。

Closes #1326
