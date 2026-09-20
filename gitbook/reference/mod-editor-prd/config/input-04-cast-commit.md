# input-04 配置说明 · 施法提交档案

> 配置写法与行为。第一性需求见 [input-04 PRD](../prd/input-04-cast-commit.md)；编辑器需求见 [UXD](../uxd/input-04-cast-commit.md)；现状见 [reference](../reference/input-04-cast-commit.md)。

## 1. 示例配置

引擎根资产两文件现状均空（全量）：

```json
{ "profiles": [] }        ← assets/Input/cast_commit_profiles.json
{ "locks": [] }           ← assets/Input/cast_commit_locks.json
```

教学骨架（激活压瞄准帧，确认键提交）：

```json
{ "profiles": [
  { "id": "commit.aim",
    "onActivate": [
      { "op": "pushFrame", "contextProfileId": "ctx.guided" } ],
    "frameActions": {
      "Confirm": [
        { "op": "submitOrder", "payload": { "spatialTarget": "cursorWorld" } },
        { "op": "popFrame" } ] } } ] }
```

锁教学骨架：

```json
{ "locks": [ { "scope": "template", "key": "Ability.Champion.Q", "castCommitId": "commit.aim" } ] }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 档案标识；技能槽位声明引用 |
| `onActivate[]` | 槽激活即执行的操作序列 |
| `frameActions` | 动作标识 → 操作序列；仅压帧在顶期间生效 |
| `op: pushFrame / popFrame` | 压/弹交互帧；压帧可带 `contextProfileId`（input-03） |
| `op: submitOrder` + 值源 | 提交订单；`payload` 为参数槽→值源（`cursorWorld` 光标世界坐标 / `framePointer` 帧指针）映射 |
| `locks[].scope` + `key` + `castCommitId` | 锁作用域（`global`/`template`/`formSet`/`slot`）内提交偏好固定为指定档案 |

## 3. 文件结构

`assets/Input/cast_commit_profiles.json` 与 `assets/Input/cast_commit_locks.json`（根资产均空表，档案与锁由 mod 贡献）。

## 4. 运行时加载效果

加载器拒绝三种字段以外的任何键；运行期槽激活执行序列，帧内动作经注册表拦截，玩家偏好按"锁 > 单槽 > 形态集 > 模板 > 全局"五级序解析。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 op / 未知值源 / 多余字段 | 启动失败 |
| 被锁作用域写偏好 / 帧内动作但无压帧在顶 | 写入返回失败（明确拒绝） / 不触发 |

## 6. 实例

- 根空表：`assets/Input/cast_commit_profiles.json`、`assets/Input/cast_commit_locks.json`

**相关文档**：[input-04 PRD](../prd/input-04-cast-commit.md) · [input-03 配置说明](input-03-interaction-context.md)
