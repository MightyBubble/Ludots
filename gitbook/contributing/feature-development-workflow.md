# Feature 开发工作流

本页定义 Ludots 中 feature 级开发的正式工作流。目标是避免重复造轮子、幻觉 API 和游离实现。

## 1 发现阶段

写代码前必须先确认：

- 已有 System 是否已覆盖需求
- 已有 Registry、Pipeline、Sink、Trigger 是否可复用
- 是否已有 Mod 可以扩展，而不是新建平行体系
- 是否已有文档说明目标挂靠点

发现结论至少要写清三件事：

- 可复用：已有类、Registry、System、Mod
- 需新增：仓库中确实不存在的部分
- 需扩展：已有能力的增量缺口

## 2 设计阶段

非 trivial 改动在编码前应明确：

- 目标
- 挂靠点
- 复用清单
- 新增清单
- 数据流
- 测试策略

## 3 实现阶段

- 每引用一个非 BCL API，先搜索确认存在和签名
- 不把基建改造偷偷混进 feature 代码
- 发现基建缺口时，优先补已有基建或先停下来提基建方案

## 4 验证阶段

提交前至少确认：

- 编译通过
- 相关测试通过
- 无重复实现
- API 引用正确
- ECS 约束满足
- 文档同步完成

## 5 深度材料

- 仓库深度版：`docs/conventions/01_feature_development_workflow.md`
- 环境命令：`docs/conventions/03_environment_setup.md`
