# WinForms 桌面版

这是通用游戏货币记录工具的 Windows 桌面版本。

## 运行

打开：

`dist/金条更新记录.exe`

## 当前版本说明

当前版本已经通用化，不再内置固定账号或固定分组。

首次使用时，请点击软件右上角的 `账号管理`，先添加分组和账号，再进行余额更新。

历史记录会保存每次更新时的账号名、分组名、余额和收益快照。后续改名、换组或删除账号，不会改写旧历史记录。

## 数据存储

默认数据文件：

`%APPDATA%\GameCurrencyTracker\currency_records.json`

数据位置设置文件：

`%APPDATA%\GameCurrencyTracker\settings.json`

点击软件中的 `数据位置` 可以选择新的 JSON 数据文件路径。软件会把当前数据复制到新位置。

## 源码文件

- `CurrencyTracker.cs`：WinForms 主源码。
- `assets/app-icon.ico`：Windows 程序图标。
- `scripts/make_icon.py`：图标生成辅助脚本。
- `app.manifest`：高 DPI manifest。
