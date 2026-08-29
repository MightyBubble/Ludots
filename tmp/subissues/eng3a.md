Part of #1321（W3 资产生命周期之一）

## 目标

AssetHandle 状态机、句柄租约 + 后端引用计数 + GPU 帧延迟销毁、同 URI 全局去重、负缓存失效。分层纪律：Core 管描述符/VFS 合同（对齐 `MeshAssetRegistry` 模式），GPU 资源状态与句柄放 `Ludots.Raylib.Render`，Adapter 注入；GPU 类型不进 Core。

## 背景（证据见 epic）

- 当前按 asset id 各自缓存（`RaylibPrimitiveRenderer.cs:81-83`、`RaylibMaterialLibrary._bindingsByMaterialId`、`RaylibVfxRenderer._textureCache`），同 URI 多份 GPU 拷贝；负缓存（`:1621-1627`）导致文件补齐后须重启。

## 验收标准

- Given 两个 asset id 或多个 lane 使用同一 URI；When 同时请求；Then 物理 GPU 资源只创建一份（契约测试断言）。
- Given 最后一个租约释放且 GPU 使用完成；When 延迟销毁点到达；Then 资源才销毁；Dispose 顺序不触发 double-free。
- Given 某 URI 首次加载失败；When 文件补上或内容版本变化；Then 句柄可重入 Preparing，不须重启；失败原因、重试次数、当前状态可诊断。
- Given 全部现有验收/截图路径；When 迁移到句柄模型；Then 渲染输出不变。

## 依赖

前置：#1326（错误策略合同）。
