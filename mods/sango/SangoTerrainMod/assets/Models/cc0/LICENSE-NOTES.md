# CC0 模型归属说明(SangoTerrainMod assets/Models/cc0/)

本目录下的 GLB 模型与 `Textures/colormap.png` 均为 CC0 1.0 Universal(公有领域贡献)资产,可合法入库、修改与再分发;许可全文见同目录 `CC0-1.0.txt`。只入库当前用到的模型文件,完整原始 ZIP 不入库(仓库 `.gitignore` 已含 `*.zip`,全量归档在仓库外 Ludots-Assets 克隆)。

来源与选取(镜像 Ludots-Assets 归档的做法:每个 pack 一节,逐文件列来源):

| 子目录 | 原始资产包 | 上游来源 | 归档 ZIP SHA-256 |
|---|---|---|---|
| `castle-kit/` | Kenney "Castle Kit" | https://kenney.nl/assets/castle-kit | `921f3f73927bb23106cae34bc21d5ab4b033a9fc120475e96f714a406e3169df` |
| `pirate-kit/` | Kenney "Pirate Kit" | https://kenney.nl/assets/pirate-kit | `667ed2caf92954ddb98f7b7cede831fe99ab75063c26b25e23d32715bee9c943` |

作者:Kenney(kenney.nl),以 CC0 1.0 发布;归档清单与校验值来自 https://github.com/MightyBubble/Ludots-Assets (`manifest.kenney.json`)。GLB 从原始 ZIP 的 `Models/GLB format/` 原样提取,未做再导出;castle-kit 的 GLB 以相对 URI 引用包内共享贴图,故每个包必须保持 `[*.glb | Textures/colormap.png]` 同目录结构。pirate-kit 的 GLB 无外置贴图。

各包入库文件清单:

- `castle-kit/`:tower-square、wall-corner、wall、flag-banner-long、flag、tree-large、tree-small、rocks-large、rocks-small(+ `Textures/colormap.png`)
- `pirate-kit/`:castle-gate、structure-platform-dock、ship-small、tower-watch、crate、flag-high-pennant
