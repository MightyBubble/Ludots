# 验收套件（Acceptance Suite）

本目录是 Ludots showcase 验收的统一入口。验收清单不在这里手工维护，而是由
`scripts/build-acceptance-index.py` 从仓库根部的 `showcase.registry.json` 自动生成为
`acceptance.index.json`：

- **选取范围**：注册表中 `tier = T1` 且 `status = active` 的条目。
- **runnable**（`preset` 非空）：可通过 launcher 实跑录制，CI 逐条执行
  `scripts/run-mod-launcher.cmd cli launch <binding> --adapter raylib --record <artifactDir>`。
- **test-only**（`preset` 为空但有 `acceptanceTest`）：无 launcher 预设，仅通过
  `dotnet test --filter FullyQualifiedName~<testFilter>` 覆盖（见各测试项目与 solution-verify 流水线）。

当前索引统计：**runnable 9 条 / test-only 14 条 / 共 23 条**（以 `acceptance.index.json` 的
`counts` 字段为准）。

## 索引条目结构

`acceptance.index.json` 中每条形如：

```json
{
  "id": "camera_acceptance",
  "preset": "camera_acceptance_raylib",
  "binding": "camera_acceptance",
  "testFilter": "CameraAcceptanceModTests",
  "artifactDir": "artifacts/acceptance/launcher-camera-acceptance-raylib",
  "hasScreenshotEvidence": true
}
```

- `testFilter`：即注册表 `acceptanceTest`，对应 `dotnet test --filter FullyQualifiedName~<值>`。
- `artifactDir`：录制产物目录；为空时 CI 落盘到 `artifacts/acceptance/ci/<id>/`。
- `hasScreenshotEvidence`：注册表 `screenshot` 字段非空即为 `true`。

## 录制产物"六件套"

runnable 条目经 `--record` 录制后，产物目录下标准产出六件证据：

| 产物 | 说明 |
|---|---|
| `summary.json` | 运行摘要与成功判定（`success` 字段，CI 据此二次校验退出码） |
| `trace.jsonl` | 逐 tick 事件轨迹 |
| `screens/` | 截图序列（如 `000_start.png`、操作后截图） |
| `battle-report.md` | 战报 / 结果报告 |
| `visible-checklist.md` | 可见性核对清单 |
| `path.mmd` | Mermaid 路径 / 流程图 |

## 目录内脚本清单（11 个）

| 脚本 | 用途 |
|---|---|
| `capture-item-system-showcase-rooms.ps1` | 批量截取物品系统 showcase 各房间截图到 `artifacts/acceptance/item-system-showcase/room-screenshots` |
| `run-forge-socket-showcase-acceptance.ps1` | 锻造插槽 showcase 验收：Raylib 截图 + 诊断日志 |
| `run-item-loadout-showcase-acceptance.ps1` | 物品配装 showcase 验收：Raylib 截图 + 诊断日志 |
| `run-item-system-showcase-acceptance.ps1` | 物品系统 showcase 验收：Raylib 截图 + 诊断日志 |
| `run-item-system-showcase-raylib.ps1` | 物品系统 showcase 通用截图脚本（支持启动地图覆盖 mod） |
| `run-mass-navigation-large-world-uat.ps1` | 大世界万人导航 UAT：launcher `--record` 循环录制并汇总成功率 |
| `run-raid-loop-showcase-acceptance.ps1` | 突袭循环 showcase 验收：Raylib 截图 + 诊断日志 |
| `run-relationship-showcase-raylib.ps1` | 关系系统 showcase Raylib 截图验收 |
| `run-save-system-uat.ps1` | 存档系统 UAT（Debug 配置，纯测试驱动） |
| `run-uxprototype-raylib.ps1` | UX 原型 showcase Raylib 截图验收 |
| `run-weapon-bench-showcase-acceptance.ps1` | 武器工作台 showcase 验收：Raylib 截图 + 诊断日志 |

## 如何新增一条验收

只需改注册表，索引自动生成：

1. 在 `showcase.registry.json` 中把目标条目标为 `tier: "T1"`、`status: "active"`：
   - 填好 `preset` / `binding`（以及可选的 `artifactDir`、`screenshot`）→ 进入 **runnable**；
   - 只填 `acceptanceTest` → 进入 **test-only**。
2. 运行 `python scripts/build-acceptance-index.py` 重新生成 `acceptance.index.json` 并一并提交。
3. CI（`.github/workflows/ci-acceptance.yml`）会先跑 `build-acceptance-index.py --check`，
   索引与注册表漂移即失败，因此两者始终同步。

## 本地运行

```bash
# 生成 / 更新索引
python scripts/build-acceptance-index.py

# 校验索引与注册表同步（CI 同款检查）
python scripts/build-acceptance-index.py --check

# 实跑某条 runnable 验收（以 camera_acceptance 为例）
scripts/run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib --record artifacts/acceptance/launcher-camera-acceptance-raylib

# 跑某条 test-only 验收（以 fog_of_war 为例）
dotnet test src/Tests/GasTests/GasTests.csproj -c Debug --filter FullyQualifiedName~FogOfWarShowcaseAcceptanceTests -v minimal
```

## CI 流水线（ci-acceptance）

`.github/workflows/ci-acceptance.yml`，windows-latest，触发方式：

- **schedule**：每天 UTC 17:23（`23 17 * * *`，非整点非半点）；
- **workflow_dispatch**：手动触发；
- **push 到 main** 且路径命中 `scripts/acceptance/**`、`scripts/build-acceptance-index.py`
  或 `showcase.registry.json`。

步骤：checkout → 安装 .NET 8 + 9 SDK（globaljson pin 9.0.100）→ `--check` 校验索引 →
编译 `GasTests` / `PresentationTests` / `UiShowcaseTests` → 逐条实跑 runnable 验收
（单条 600s 超时，失败/超时不阻断后续条目）→ 上传 `artifacts/acceptance/` 产物 →
输出 job summary（成功/失败条目表）→ Gate 步骤统一判定（任一失败则工作流失败）。
