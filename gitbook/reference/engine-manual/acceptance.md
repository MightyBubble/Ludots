# 验收与证据

"验收"在这套引擎里 = 用固定命令跑一关、留下截图和帧统计，作为"它长这样、跑得动"的证据。CI 也用同一套命令做门禁——本地怎么验收，云端就怎么复查。

## 标准跑法

一条命令完成一次取证：

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibPlayer -- \
  --project projects/engine_gallery --scene composition \
  --frames 120 \
  --screenshot artifacts/acceptance/engine_raylib_composition/screen.png \
  --json artifacts/acceptance/engine_raylib_composition/stats.json
```

跑完得到两样东西：

- **screen.png** — 第 120 帧的画面。初始相机来自场景文件的 `camera`，所以每批截图取景一致；
- **stats.json** — 帧耗统计：`avgFrameMs` 平均、`p95FrameMs` 95 分位、`maxFrameMs` 峰值、`wallMs` 总耗时。首帧含冷启动装载（模型/贴图第一次进显存），峰值高一截是正常的。

## 用 preset 跑（推荐）

仓库把每条验收固化成了 launcher preset，名字就是 `engine_raylib_<场景 id>`：

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_composition --adapter raylib
```

preset 跑的就是上面那条命令，产物落在约定目录，注册表与验收索引自动对账。

## 录像

给场景录一段页内可播的实拍（play.mp4 + poster.png）：

```text
python scripts/record-engine-galleries.py --scene composition
```

产物在 `artifacts/evidence/engine_raylib_composition/`，画廊 Wiki 的场景页正文嵌的就是它。新场景上架后重录一次是硬要求。

## 给新场景接上验收

四件事，缺一不可：`showcase.registry.json` 加条目、`launcher.presets.json` 加 preset、跑一次 preset 落截图与 stats、`gitbook/reference/engine-gallery-wiki/` 加一页。然后跑 `python scripts/build-acceptance-index.py` 同步索引（CI 会校验同步，忘跑即红）。完整登记清单见[引擎画廊开发指南](../../architecture/raylib-engine-gallery-dev-guide.md)。
