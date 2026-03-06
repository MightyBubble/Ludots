# 开发规范

本文件夹是 Ludots 仓库所有开发规范的唯一入口。所有 AI Agent 配置文件（`CLAUDE.md`、`AGENTS.md`）均重定向到此处。

## 目录

0.  [编码标准](00_coding_standards.md)
    *   **核心架构铁律**：六边形架构与无头测试、一切皆 Mod、四个禁止（fallback/向后兼容/重复造轮子/跨越职责）
    *   ECS 硬性约束、组件/系统命名、Commit 格式、测试规范
1.  [Feature 开发工作流](01_feature_development_workflow.md)
    *   发现阶段（已有能力检索清单）
    *   设计阶段（挂靠点、API 引用验证）
    *   实现阶段（架构挂靠原则）
    *   验证阶段（提交前检查清单）
2.  [AI 辅助开发规范](02_ai_assisted_development.md)
    *   防幻觉条款（搜索→阅读→编码→自检）
    *   禁止创建平行体系
    *   能力清单速查表（20+ Registry、核心管线、SystemGroup Phase）
3.  [开发环境与构建](03_environment_setup.md)
    *   SDK 要求与安装
    *   构建、测试、运行命令
    *   平台特定说明（Linux / Cloud VM）

## 任务 Recipes

按业务场景找到对应的 Recipe 组合：[recipes/README.md](recipes/README.md)

| 我要做什么 | 对应 Recipe 组合 |
|-----------|-----------------|
| 做一个新英雄/角色 | component → ability → input → order → presenter |
| 做一张新地图 | map → trigger → config |
| 加一种新操作方式 | input → order |
| 加一个 gameplay 机制 | system → component → config |
| 做一个独立功能模块 | mod → 按需组合 |

共 10 个原子 Recipe，完整索引见 [recipes/README.md](recipes/README.md)。

## 规范自身的迭代规则

本文件夹的文档也是代码的一部分，增删改必须遵循以下规则。

### 新增

| 类型 | 操作 | 审批 |
|------|------|------|
| 规范文档（`NN_*.md`） | 取当前最大编号 +1，不重排已有编号 | **需用户批准**后才能合入 |
| Recipe（`recipes/*.md`） | 文件名 `new_<名词>.md`，遵循统一模板（目标→文件清单→代码→挂靠点→检查清单） | 可随基建一起提交，但需 Review |

新增后必须同步更新：
*   本文件（`README.md`）的目录表
*   `recipes/README.md`（如果是 Recipe）

### 修改

*   修改规范条文时，commit message 必须说明**改了什么规则、为什么改**
*   修改后必须检查所有交叉引用（`grep` 搜索被改文件名和章节编号）
*   如果修改了铁律（`00_coding_standards.md` §1），**必须用户明确批准**

### 删除

*   编号确定后**不重排**——删除文档时保留编号空缺
*   删除前必须清理所有引用该文档的交叉链接
*   同步更新 README 目录表

### Recipe 迭代

*   Recipe 中的代码示例必须与实际代码库一致——代码变更后 Recipe 必须同步更新
*   如果一个 Recipe 对应的管线/Registry 发生了 breaking change，Recipe **必须在同一 PR 中更新**
*   废弃的 Recipe 直接删除，不标注"已废弃"

### 硬性约束

*   规范文档（`NN_*.md`）**不超过 300 行**——超出则拆分
*   Recipe **不超过 100 行**——只写最小完整示例
*   不得在本文件夹之外创建规范文档（`CLAUDE.md`/`AGENTS.md` 只做重定向）

## 与其他文档的关系

*   **架构文档**在 `docs/developer-guide/`——描述引擎各子系统的设计与实现
*   **本文件夹**——规定开发流程和编码标准（必须遵守的规则）
*   两者互补，不重叠
*   架构文档的编写规范见 `docs/developer-guide/00_documentation_standards.md`
