---
文档类型: 工具设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 工具链 - Ludots.Tool - CLI
状态: 草案
---

# Ludots.Tool 工具设计

# 1 背景与目标

Ludots.Tool 是面向 Mod 开发与离线资源处理的 CLI 工具：提供 Mod 初始化与构建、GAS Graph 编译、地图工具（导入 Web 编辑器产物、生成测试地图）。

# 2 工具边界与非目标

边界：

- 工具只处理资源与开发辅助，不负责运行时启动与 preset 管理（由 ModLauncher 承担）。

非目标：

- 不作为通用资产管线；仅覆盖项目约定产物的转换与生成。

# 3 输入输出与产物契约

## 3.1 Mod 初始化

输入：

- `--id <modId>`

输出：

- 在 `src/Mods/{modId}/` 创建基础目录结构；如果无法定位 `src/Mods`，则在当前目录创建并给出警告。

## 3.2 Graph 编译

输入：

- `--mod <modId>`
- `--assetsRoot <path>`（可选，repo 根目录，需包含 `assets/`）

输出：

- 生成 Graph 二进制 blob（落盘位置以 Graph 编译器实现为准）。

## 3.3 地图导入与生成

输入：

- `map import-react --in <map_data.bin>`：Web 编辑器导出二进制
- `map gen-vtxm --out <file.vtxm>`：生成 VertexMap v2 测试地图

输出：

- 默认输出目录为 `assets/Data/Maps/`（可通过 `--outDir` 覆盖）。

# 4 命令与参数

```bash
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- mod init --id ExampleMod
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- mod build --id ExampleMod

dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- graph compile --mod ExampleMod

dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- map import-react --in <path-to-map_data.bin> --name <mapId> --force
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- map gen-vtxm --out assets/Data/Maps/test.vtxm --widthChunks 16 --heightChunks 16 --chunkSize 64 --preset bench --overwrite
```

# 5 版本化与兼容性

- `import-react` 的输入格式与输出 `VertexMap` 版本必须在工具内部强校验；一旦升级，必须更新文档版本并给出迁移说明。

# 6 失败策略与诊断信息

- 缺失输入文件、输出目录不可写、未知 preset 等情况必须直接失败并输出可定位信息。

# 7 安全与破坏性操作

- `map import-react --force` 会覆盖既有输出文件。
- `map gen-vtxm --overwrite` 会覆盖既有输出文件。

# 8 代码入口

- CLI：`src/Tools/Ludots.Tool/Program.cs`

