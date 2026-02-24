---
鏂囨。绫诲瀷: 鏋舵瀯璁捐
鍒涘缓鏃ユ湡: 2026-02-05
鏈€鍚庢洿鏂? 2026-02-05
缁存姢浜? X28鎶€鏈洟闃?鏂囨。鐗堟湰: v0.1
閫傜敤鑼冨洿: 浜や簰灞?- UI绯荤粺 - Widget 鏋舵瀯
鐘舵€? 鑽夋
---

# Widget 鏋舵瀯

# 1 鑳屾櫙涓庨棶棰樺畾涔?
UI 绯荤粺闇€瑕佸湪楂橀浜や簰涓嬩繚鎸佸彲棰勬祴鐨勬覆鏌撴垚鏈紝骞朵笌澶氬钩鍙伴€傞厤灞傝В鑰︺€傚綋鍓?UI 浠?Widget 鏍戜负鍩虹褰㈡€侊紝鐢?UIRoot 璐熻矗灞忓箷灏哄涓庤緭鍏ュ垎鍙戙€?
# 2 璁捐鐩爣涓庨潪鐩爣

鐩爣锛?
- 缁撴瀯娓呮櫚锛歎IRoot 鎸佹湁鏍?Widget锛學idget 鍙鐞嗘覆鏌撲笌杈撳叆

- 鍙帶閲嶇粯锛氶€氳繃 dirty 鏍囪瀹炵幇鍙娴嬬殑閲嶇粯绛栫暐

- 骞冲彴瑙ｈ€︼細骞冲彴鍙彁渚涚敾甯冧笌杈撳叆浜嬩欢锛孶I 涓嶄緷璧栧叿浣撳紩鎿?
闈炵洰鏍囷細

- 褰撳墠涓嶅畾涔夊畬鏁村竷灞€绯荤粺涓庢牱寮忕郴缁燂紙鍚庣画鍦?HTML 寮曟搸鏂囨。涓墿灞曪級

# 3 鏍稿績璁捐

## 3.1 妯″潡鍒掑垎涓庤亴璐?
- Widget锛氬熀纭€ UI 鍏冪礌锛屾嫢鏈変綅缃?灏哄涓?Render/HandleInput

- UIRoot锛氭牴瀹瑰櫒锛岀鐞嗗睆骞曞昂瀵稿苟椹卞姩娓叉煋/杈撳叆鍒嗗彂

- 鍏蜂綋鎺т欢锛歀abel/Panel 绛夊湪 Widgets 鐩綍鎵╁睍

## 3.2 鏁版嵁娴佷笌渚濊禆鍏崇郴

```
骞冲彴杈撳叆浜嬩欢
  鈫?UIRoot.HandleInput
  鈫?Widget.HandleInput锛堟爲褰㈠垎鍙戯級

骞冲彴娓叉煋甯?  鈫?UIRoot.Render
  鈫?Widget.Render锛堟爲褰㈢粯鍒讹級
```

## 3.3 鍏抽敭鍐崇瓥涓庡彇鑸?
- Widget 鐨?Render 浠ュ眬閮ㄥ潗鏍囦负涓伙紙Translate 鍚庣粯鍒讹級

- 杈撳叆浜嬩欢浠ュ潗鏍囧彉鎹㈠畬鎴?hit test锛岄伩鍏嶅紩鍏ュ钩鍙板璞″彞鏌刓n
- dirty 鏄覆鏌撶瓥鐣ョ殑鍩虹鐪熸簮锛屽繀椤诲彲瑙傛祴

# 4 浠ｇ爜鍏ュ彛锛堟枃浠惰矾寰勶級

- `src/Libraries/Ludots.UI/UIRoot.cs`

- `src/Libraries/Ludots.UI/Widgets/Widget.cs`

- `src/Libraries/Ludots.UI/Widgets/Panel.cs`

- `src/Libraries/Ludots.UI/Widgets/Label.cs`

# 5 楠屾敹鏉℃

- UI 閫昏緫涓嶅紩鐢ㄥ钩鍙扮被鍨嬶紙闄ゆ覆鏌撶敾甯冩娊璞℃墍闇€锛塡n
- 杈撳叆鍒嗗彂涓庢覆鏌撻亶鍘嗕笉浜х敓 GC锛堝彲娴嬭瘯锛塡n
- dirty 鏍囪鑳藉弽鏄犫€滄槸鍚﹂渶瑕侀噸缁樷€濓紙鍙祴璇曪級

