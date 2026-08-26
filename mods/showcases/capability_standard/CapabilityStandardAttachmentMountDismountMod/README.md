# 乘员上下车（挂接）

载具边上的乘员上车跟车，再周边下车落到车旁稳定位置。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_attachment_mount_dismount' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_attachment_mount_dismount_raylib'
```

## 你会看到

1. 青色大圈 = 载具，黄/绿小圈 = 乘员  
2. 字幕「上车」后乘员贴到座位  
3. 载具前移时乘员保持相对偏移  
4. 「下车」后乘员落在车旁环上，颜色变回绿色  

本场只演示 **Effect 触发的 Attach/Detach**，不含炮塔多层瞄准。
