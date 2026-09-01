# 播放器与命令行

播放器（`Ludots.App.RaylibPlayer`）是引擎的唯一入口：开窗口、跑场景、出证据都靠它。这页是全部参数和常见报错。

## 参数一览

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibPlayer -- \
  --project projects/engine_gallery \
  --scene composition \
  --frames 120 \
  --screenshot shot.png \
  --json stats.json
```

| 参数 | 干什么 | 备注 |
|---|---|---|
| `--project <路径>` | 打开哪个工程 | **必填**。给相对路径时先按当前目录找，找不到再向上层找 |
| `--scene <id>` | 直接进哪个场景 | 省略则开菜单 |
| `--frames <N>` | 跑多少帧后退出 | 省略默认 300；配 `--screenshot` 时截的是最后一帧 |
| `--screenshot <路径>` | 存截图（PNG） | 给了这个参数就不弹窗口，后台静默跑 |
| `--json <路径>` | 存帧统计 | 平均/95 分位/最大帧耗 + 墙钟时间 |
| `--menu-auto <id>` | 开菜单后自动进入某场景 | 录像脚本用 |
| `--interactive-shot <路径>` | 交互模式跑 120 帧截一张 | 录像脚本用 |

多帧取样走两个环境变量（录像管线在用）：`LUDOTS_TAKE_SCREENSHOT_PATH` 给基名、`LUDOTS_TAKE_SCREENSHOT_FRAMES` 给帧号表（如 `30,60,90`），产物命名 `基名_001_f0030.png`。

## 菜单操作

数字/字母选场景，回车进入；场景内 ESC 回菜单；R 复位相机。

## 常见报错

| 报错 | 意思 | 怎么办 |
|---|---|---|
| `--project <path> is required` | 忘给工程了 | 加上 `--project projects/engine_gallery` |
| `Failed to open engine project …` | 工程目录不对（没找到 project.json） | 检查路径；在仓库根目录跑最稳 |
| `Unknown scene 'xxx'. Available: …` | 场景 id 打错 | 报错里列出全部可用 id，照抄 |
| `…references unknown component kind` | 场景里组件名拼错 | 对照[组件手册](components.md) |
| `…asset 'x' source 'y' was not found` | assets 申报的文件不存在 | 路径是工程根相对路径，检查文件在不在 |
| `…declares asset 'x' that no component references` | assets 里申报了没人用的文件 | 删掉那条申报，或让组件引用它 |
| `…contains a parent cycle` | 材质父链绕圈了 | 检查 materials/ 里 parent 指向 |

引擎的纠错原则是**报错说人话并指名道姓**：每条都带出错的文件与字段。看到不认识的报错，先读全句再动手。
