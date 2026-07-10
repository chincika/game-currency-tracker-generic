# 通用游戏货币记录工具

这是一个 Windows 桌面工具，用来记录多个游戏账号的货币余额，并按每次更新时间计算收益。

## 下载使用

推荐从 GitHub Releases 下载最新版：

https://github.com/chincika/game-currency-tracker-generic/releases

也可以直接运行仓库中的 exe：

`tools/currency-tracker-winforms/dist/金条更新记录.exe`

## 主要功能

- 自由新增、改名、删除、排序分组。
- 自由新增、改名、删除、排序账号。
- 账号可以自由切换所属分组。
- 手动更新每个账号余额，并记录更新时间和备注。
- 自动计算每次更新的总收益、分组收益、账号明细收益。
- 新增账号首次参与更新时，当前余额完整计入当期收益。
- 账号或分组改名、换组、删除后，不会影响旧历史记录。
- 支持按日期、分组、账号查询历史收益。
- 支持按周六结算的周收益折线图，可弹出大图查看，并在折线点显示收益数字。
- 支持 JSON 备份导出和导入。
- 支持旧版固定账号 JSON 自动迁移到新版数据结构。
- 默认数据保存在 AppData，也可以在软件里手动更改数据文件位置。
- 支持高 DPI 显示，内置金条图标。
- 主界面支持拖拽调整模块大小，并会记住用户调整后的布局。

## 数据位置

默认数据文件：

`%APPDATA%\GameCurrencyTracker\currency_records.json`

设置文件：

`%APPDATA%\GameCurrencyTracker\settings.json`

当前数据结构版本为 `schemaVersion: 2`。通用版不会预置固定账号，新数据会从空账号列表开始。

旧的固定账号版数据会自动迁移为新版结构。迁移后会生成分组、账号、账号快照和分组快照，保留历史时间、备注、余额和收益。

## 项目结构

- `tools/currency-tracker-winforms/`：WinForms 桌面版源码、资源和 exe。
- `tools/currency-tracker-winforms/CurrencyTracker.cs`：主程序源码。
- `tools/currency-tracker-winforms/assets/`：图标资源。
- `tools/currency-tracker-winforms/dist/金条更新记录.exe`：当前构建好的 Windows 程序。
- `build.ps1`：重新编译 exe 的脚本。

## 重新编译

在仓库根目录执行：

```powershell
.\build.ps1
```

编译产物会输出到：

`tools/currency-tracker-winforms/dist/金条更新记录.exe`

## 后续更新

后续正式版本会通过 GitHub Releases 发布，并上传对应的 Windows exe 附件。
