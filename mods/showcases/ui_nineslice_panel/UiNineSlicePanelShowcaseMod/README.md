# 打开雕花木匣

玩家向 Showcase：用九宫格切图做博德之门风格拟物面板。

## 玩法

1. 默认看「窄匣」里的旅人卷宗。
2. 点「宽案」「高柜」，观察金角纹样是否保持清晰。
3. 木边与羊皮纸应跟着盒子变大变小；按钮框同理。

## 技术挂靠

- 场景 / HTML / CSS / 贴图：`UiShowcaseCoreMod`
- CSS：`image-slice` 挂在 `<img>` 上（不是 `background-image`）
- 验收：`UiNineSlicePanelTests`
