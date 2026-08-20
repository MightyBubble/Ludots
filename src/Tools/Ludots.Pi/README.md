# Ludots Pi

Ludots 的编码助手入口。浏览器界面是 agegr/pi-web 的源码分叉，智能体内核继续用官方 Pi，不另造一套。

## 启动

```powershell
.\scripts\run-ludots-pi.cmd
```

```bash
./scripts/run-ludots-pi.sh
```

打开脚本打印的地址。第一次使用要在界面里登录模型，并信任当前 Ludots 仓库，扩展才会加载。

## 目录

| 路径 | 职责 |
| --- | --- |
| `web/` | 改过源码的 Pi Web 前端 |
| `package/` | Ludots 扩展和提示词 |
| `scripts/launch.mjs` | 启动入口 |
| `UPSTREAM.md` | 上游提交钉扎 |

技能正文仍在仓库 `skills/`，由 `scripts/sync-skills.ps1 -Target pi` 同步到本机 Pi 技能目录。
