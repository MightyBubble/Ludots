# GPU 剔除 + Indirect Draw + LOD：100K 红蓝蒙皮军团 showcase

## 交付
- **GPU compute 剔除**：视锥 4 平面球测试 + 距离 3 级 LOD 分拣 → 原子紧凑化 → indirect 命令缓冲
- **glMultiDrawElementsIndirect**：每帧 3 次绘制调用（high/medium/low LOD 各一次）
- **零 CPU 逐实例工作**：实例数据加载时一次上传驻 GPU；姿势 compute 16 相位桶；相机 uniform + 2 次 compute dispatch + 3 次 indirect draw = 全部 CPU 工作
- **三级 LOD**：9,148 / 3,524 / ~730 三角形（GPT 备好的资产链）

## 数字
| 指标 | 值 |
|------|-----|
| 实例 | **100,000** GPU 蒙皮动画实例 |
| CPU Draw() 耗时 | **3.78ms**（264fps 能力） |
| 绘制调用 | **3 次** indirect draw |
| GPU 剔除 | 1 次 compute dispatch（100K 线程） |
| 画面 | 红蓝军团清晰可见，人形结构成形 |

## 关键发现
1. `MapBufferRange` 读回计数 = GPU 管线同步停顿 → **帧率从 15fps 坍塌到 <1fps**（禁用后恢复 264fps CPU 侧）
2. `glDrawElementsInstancedIndirect` 在 NVIDIA GL 3.3 上下文不可用；`glMultiDrawElementsIndirect` 可用（用 drawCount=1 等效替代）
3. 红蓝军团：内半区红军（90 环）、外半区蓝军（90 环），~31K 彩色像素可见

## 运行
```bash
cd src/Apps/Raylib/Ludots.App.RaylibPlayer
dotnet bin/Debug/net9.0/Ludots.App.RaylibPlayer.dll \
  --project C:/001_AI/Ludots-gpu-commercial/projects/engine_gallery \
  --scene gpu_crowd --frames 1000000
```
