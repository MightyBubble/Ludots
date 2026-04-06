# 发布与访问

本页说明如何从 GitHub 仓库接入 Ludots 的 GitBook 文档。

## 1 仓库内正式源

Ludots 的 GitBook 源位于：

- `gitbook/README.md`
- `gitbook/SUMMARY.md`

仓库根目录的 `.gitbook.yaml` 已声明：

- 文档根目录为 `gitbook/`
- 首页为 `README.md`
- 目录结构文件为 `SUMMARY.md`

## 2 在 GitBook 中接入

把仓库推到 GitHub 后，在 GitBook 后台执行：

1. 新建或打开目标 space
2. 选择 Git Sync / Import from GitHub
3. 选择 Ludots 对应仓库
4. 分支优先选择 `gitbook`
5. 保持 GitBook 使用仓库中的 `.gitbook.yaml`
6. 完成首次同步并发布

当前仓库额外维护了一个 `gitbook` 分支，用于给 GitBook Git Sync 和 GitHub 网页访问提供稳定入口；它应与经过校验的主线文档保持一致。

同步完成后，GitBook 会直接以 `gitbook/` 作为文档根，而不是使用仓库根目录。

## 3 从 GitHub 网页怎么访问

在 GitHub 网页里，当前有两种访问方式：

### 3.1 访问仓库内文档源

直接打开这些文件：

- `https://github.com/MightyBubble/Ludots/tree/gitbook/gitbook`
- `gitbook/README.md`
- `gitbook/SUMMARY.md`

推荐优先从 `gitbook` 分支访问，因为该分支就是提供给 GitBook Sync 的稳定文档入口。

### 3.2 访问已发布的 GitBook 站点

完成 GitBook Git Sync 并发布后：

- 在 GitHub 仓库 `README.md` 中添加 GitBook 站点链接
- 或在仓库 About 区域填写站点 URL

如果 GitBook 站点 URL 尚未确定，GitHub 首页至少应保留到 `gitbook` 分支文档源的显式入口；站点 URL 确认后再替换为正式发布地址。

## 4 维护规则

- 修改正式文档时，更新 `gitbook/`
- 调整导航时，同步更新 `gitbook/SUMMARY.md`
- 不要在 GitBook UI 中直接改 `README.md` 造成仓库冲突
- 若更改文档根目录，必须同步更新 `.gitbook.yaml`

## 5 相关资料

- GitBook 内容配置官方文档
- GitBook Git Sync 官方文档
