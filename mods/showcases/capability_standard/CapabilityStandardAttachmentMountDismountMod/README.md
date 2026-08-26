# 乘员上下车（挂接）

青色载具与黄色乘员：上车挂座、跟车前移、再周边下车落位。主画面为正式 Presenter 网格，字幕讲阶段。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_attachment_mount_dismount' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_attachment_mount_dismount_raylib'
```

## 你会看到

1. 大青块 = 载具，黄立块 = 乘员  
2. 字幕「上车」后乘员贴到座位  
3. 载具前移时乘员保持相对偏移  
4. 「下车」后乘员落在车旁环上  

本场只演示 **Effect 触发的 Attach/Detach**，不含炮塔多层瞄准。
