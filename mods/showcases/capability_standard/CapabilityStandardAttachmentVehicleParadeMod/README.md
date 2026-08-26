# 装甲阅兵（挂接跟随）

一辆坦克开过演练场：底盘动，炮塔和炮管跟着走；炮塔再自己转向，炮管跟着朝前伸。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_attachment_vehicle_parade' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_attachment_vehicle_parade_raylib'
```

## 你会看到

1. 青色大圈 = 底盘，黄圈 = 炮塔，红点 = 炮管  
2. 字幕先说「底盘开动」——整车平移  
3. 再说「炮塔独立转向」——炮管相对炮塔前伸方向改变  
4. 结束字幕确认跟随与瞄准成立  

本场只演示**多层跟随 + 独立瞄准**，不含上下车。
