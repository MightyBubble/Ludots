# 哨所静物（挂接）

一座大厅带着附楼和塔楼：父实体不动时，子建筑相对位置多拍保持不变。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_attachment_settlement' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_attachment_settlement_raylib'
```

## 你会看到

1. 白圈 = 大厅，青圈 = 附楼，黄圈 = 塔楼  
2. 附楼在大厅东侧，塔楼在西北侧  
3. 字幕确认多拍重派生后位置不抖  

本场只演示 **静态父预置挂接**，不含行军或上下车。
