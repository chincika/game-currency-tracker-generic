# Gold Bar Tracker WinForms App

This is the recommended Windows desktop version of the tracker.

## Run

Open:

`dist/金条更新记录.exe`

## Current Version

The app is now generalized. It no longer has built-in fixed accounts or fixed groups. Use `账号管理` to add groups and accounts before the first update.

Historical records store snapshots of account names, group names, balances, and gains at the time of each update. Later rename, regroup, or delete actions do not rewrite old records.

## Data Storage

Default data file:

`%APPDATA%\GameCurrencyTracker\currency_records.json`

The selected data location is stored in:

`%APPDATA%\GameCurrencyTracker\settings.json`

Click `数据位置` in the app to choose another JSON data file path. The app copies the current data to the new location.

## Source

- `CurrencyTracker.cs`: WinForms source.
- `assets/app-icon.ico`: Windows app icon.
- `scripts/make_icon.py`: icon-generation helper.
- `app.manifest`: high-DPI manifest used when compiling the exe.
