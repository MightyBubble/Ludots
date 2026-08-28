# 装甲阅兵（挂接跟随）

一辆坦克开过演练场：青色底盘带着黄炮塔与红炮管；底盘开动时整车粘在一起，炮塔再自己转向。

主画面走正式 Presenter（Core `cube` 网格），字幕讲阶段。不是 DebugDraw 色圈。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_attachment_vehicle_parade' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_attachment_vehicle_parade_raylib'
```

## 你会看到

1. 青色长方体 = 底盘，黄块 = 炮塔，红细长条 = 炮管，底盘上方有血条 HUD  
2. 字幕先说「底盘开动」——整车平移  
3. 再说「炮塔独立转向」——炮管相对炮塔前伸方向改变  
4. 结束字幕确认跟随与瞄准成立  

本场只演示**多层跟随 + 独立瞄准**，不含上下车。
