# 打包分发

把做好的场景发到一台**没装开发环境**的电脑上跑。思路一句话：**播放器是二进制，工程是数据**——打包就是把这两样并排放进一个文件夹。

## 发布播放器

```text
dotnet publish src/Apps/Raylib/Ludots.App.RaylibPlayer -c Release -o dist
```

`dist/` 里是自包含运行所需的一切（含 raylib 原生库）。

## 带上工程

把工程目录整个拷进发布产物旁边：

```text
dist/                  ← 播放器二进制（dotnet publish 的输出）
  Ludots.App.RaylibPlayer.exe
  …
  projects/
    engine_gallery/   ← 工程整个目录原样拷贝
```

## 在目标机器上跑

**直接双击 `Ludots.App.RaylibPlayer.exe`**：它发现身边唯一的 `projects/engine_gallery` 就自动进入场景菜单——目标用户零输入。机器上有多个工程时双击会弹选择列表。

需要固定进某一关（比如展会自动循环），放一个 `play.cmd` 在 dist/ 里：

```text
@echo off
cd /d "%~dp0"
Ludots.App.RaylibPlayer.exe --project projects/engine_gallery --scene composition
```

目标机器要求：64 位 Windows/Linux + 一块能跑 OpenGL 的显卡，无需安装 .NET 之外的东西（`dotnet publish` 默认框架依赖发布时目标机需要 .NET 9 运行时；加 `-r win-x64 --self-contained` 可连运行时一起带上，体积换省心）。

## 三条红线

1. **工程不进播放器的构建输出**——这套引擎从设计上就把两者分开，别把工程文件塞进 csproj 的复制清单；
2. **改内容只动工程目录**——发出去之后调场景、换贴图，直接改 `projects/` 里的 JSON/PNG，不用重新 publish；
3. **别把打包产物提交回仓库**——`dist/` 是一次性分发物，内容的真身永远在仓库的 `projects/` 里。

工程、播放器、打包三者的完整分层合同见[引擎工程分层与关卡容器格式](../../architecture/raylib-engine-project-scene-format.md)第 10 节。
