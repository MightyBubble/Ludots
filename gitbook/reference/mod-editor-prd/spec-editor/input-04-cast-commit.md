# input-04 editor spec · 施法提交档案

> 编辑器实现任务书。编辑器需求见 [input-04 UXD](../uxd/input-04-cast-commit.md)；引擎侧见 [runtime spec](../spec-runtime/input-04-cast-commit.md)。

## 1. 概述
提交编辑器实现：序列时间线、帧内动作表、锁面板与偏好预览。

## 2. 设计
- **序列时间线**：写 `Input/cast_commit_profiles.json`；op 三值与值源枚举同源加载器。
- **帧内动作表**：动作 id 补全自动作注册表；孤儿动作（无对应压帧）静态提示。
- **锁面板**：写 `Input/cast_commit_locks.json`；生效层级预览复刻五级解析序（同源算法）。
- **序列预演**：编辑器内干跑 op 序列（不含真实提交），演示压帧/弹帧与取值。

## 3. 精确语义与不变量
- 时间线可产生的形状 = 加载器接受的形状（多余字段在编辑器即拒）。
- 锁层级预览与引擎偏好解析逐字一致。

## 4. 依赖接口与验收
- 消费：档案/锁表加载器、动作注册表、上下文档案表、偏好解析接口。
- 验收：编排保存后启动即生效；锁作用域在预览与运行一致；孤儿帧动作保存前提示。

**相关文档**：[input-04 UXD](../uxd/input-04-cast-commit.md) · [input-04 runtime spec](../spec-runtime/input-04-cast-commit.md)
