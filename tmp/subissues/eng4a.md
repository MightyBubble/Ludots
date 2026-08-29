Part of #1321（W4 spike，先行探假设）

## 目标

单条静态实例 lane 用 `UploadMesh(dynamic)` + `UpdateMeshBuffer`（`Raylib.cs:574,577`）走 mesh-buffer 通道做持久实例 buffer 验证：不碰裸 rlgl 句柄、不在绑定层造新耦合。

## 验收标准

- Given 一条静态实例 lane；When 用持久 GPU buffer 上传矩阵；Then 画面与 CPU 指针路径一致（截图对比）。
- Given 软件 GL 路径（llvmpipe 等）；When 尝试持久 buffer；Then 明确报告不支持，不静默退回 CPU 路径、无隐式 fallback。
- buffer、VAO、材质与 renderer 的 Dispose 顺序全部通过测试。
- spike 结论（可行性、限制、性能数字）写进本 issue，供 #<ENG-4b> 决策。

## 依赖

无硬前置；建议在 #1323 后做（新帧结构下验证）。
