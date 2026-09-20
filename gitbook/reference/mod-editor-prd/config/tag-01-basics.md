# tag-01 配置说明 · Tag 表示与状态

> 配置写法与行为。第一性需求见 [tag-01 PRD](../prd/tag-01-basics.md)；编辑器需求见 [UXD](../uxd/tag-01-basics.md)；现状见 [reference](../reference/tag-01-basics.md)。

## 1. 示例

效果授予 tag（卷 5 效果配置的片段，教学骨架）：

```json
[ { "id": "Effect.MyMod.Burn", "grantedTags": [
    { "tag": "State.Burning", "formula": "Linear", "amount": 1 } ] } ]
```

能力时间轴的限时 TagClip（控制器=能力，骨架）：

```json
{ "kind": "TagClip", "tick": 0, "duration": 300, "tag": "Cooldown.MyMod.Q" }
```

## 2. 用法与行为

| 用法 | 写法 | 效果 |
|---|---|---|
| 声明/引用 | 任意配置里写 tag 名 | 首次出现即注册（全局命名空间，上限见 [事实页](../facts.md)） |
| 授予层数 | 效果 `grantedTags`（公式 Fixed/Linear/LinearPlusBase） | 效果存续期持续贡献层数，移除即回收 |
| 限时状态 | 能力时间轴 `TagClip`（带时长） | 控制器是能力：项到期由能力执行系统退层（内部用定时缓冲实现，非独立配置面） |
| 判定 | 图节点 `HasTag`（Effective 默认） | 在场/有效两视角可选 |
| 屏蔽 | 删除标记/规则（见 tag-02） | 按合同退层 |

## 3. 文件结构

无独立 tag 表：名字散布在效果/技能/规则/AI 配置里，首次出现即注册。规则集中表见 tag-02。

## 4. 运行时加载效果

配置加载期完成全部名字注册与 id 分配；运行期 tag 操作走统一入口：在场集位图 + 层数容器 + 定时缓冲；变化帧末入脏队列，下一拍生成事件（tag-03）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 注册超上限 | 启动失败（上限见事实页） |
| 层数/定时条目超容量 | 操作失败并报错 |
| 引擎保留 `0` 值 tag id | 不可作引用（监听通配专用） |

## 6. 实例

- 授予真实样例：效果卷 grantedTags 系列（`mods/showcases/` 各效果表）
- 规则表：`mods/showcases/arpg_demo/ArpgDemoMod/assets/GAS/tag_rules.json`

**相关文档**：[tag-01 PRD](../prd/tag-01-basics.md) · [tag-02 配置说明](tag-02-rules.md) · [tag-03 配置说明](tag-03-changed-events.md)
