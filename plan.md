# Plan: Ludots 代码反推文档与异味扫描

## 目标
1. 全面扫描 Ludots 最新 main 代码，反推功能框架
2. 生成专业 PRD + TDD 标准文档（结构化 wiki）
3. 原子化罗列文档异味、showcase 不合理问题

## 阶段

### Stage 1 — 并行代码扫描（Explore Workers）
按模块分派 `explore` 子代理，每个负责一个主要代码区域，提取：
- 核心类、接口、System、Registry、Pipeline
- 数据流与挂靠点
- 与文档的对应关系（文档说有但代码没有，或代码有但文档没提）

模块划分：
1. **Engine_Core**: Engine, Systems, Hosting, TimeFlow, GameContext, WorldRuntime
2. **Gameplay_GAS**: GAS, Items, Narrative, Relationships, Progression, AI, Cue, Teams, Spawning, Exchange
3. **Input_Nav**: Input, Navigation, MovePlanning, MassNavigation, Orders, Selection
4. **Presentation_UI**: Presentation, UI, Camera, Surfaces, Instancing, Performers, Minimap, Hud
5. **Map_Spatial**: Map, Spatial, Physics, Physics2D, Fields, Vision, TransportNetwork
6. **Config_Script**: Config, Scripting, Modding, Persistence, GraphRuntime, NodeLibraries
7. **Adapters_Apps**: Adapters (Raylib, UE5, Web), Apps, Client
8. **Mods**: mods/ 目录下所有内置 Mod
9. **Docs_Governance**: gitbook/ + docs/ 结构、SUMMARY、交叉引用、文档异味

### Stage 2 — 综合与反推（Orchestrator）
- 整合所有扫描结果
- 按 PRD + TDD 结构组织
- 生成 wiki 目录与内容

### Stage 3 — 文档生成（Coder Workers）
- 按 wiki 结构分派 writer 子代理
- 每篇文档独立生成

### Stage 4 — 异味 Issue 文档（Orchestrator + Coder）
- 整合所有模块发现的异味
- 生成原子化 Issue 清单

## 输出
- `artifacts/wiki/` — 结构化 wiki（Markdown）
- `artifacts/issues/` — 文档异味 Issue 文档（Markdown）
