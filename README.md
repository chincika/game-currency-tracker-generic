# Game Currency Tracker Generic

Generic personal Windows tool for recording in-game currency balances and gains.

## Recommended App

Run:

`tools/currency-tracker-winforms/dist/金条更新记录.exe`

## Features

- Create, rename, delete, and sort groups.
- Create, rename, delete, sort, and regroup accounts.
- Manually update current account balances with update time and notes.
- Calculate per-update gains by total, by group, and by account.
- Preserve historical account and group names after later rename, move, or delete actions.
- Query historical records by date, group, and account.
- Import and export JSON backups.
- Store data in AppData by default, with an in-app option to move the JSON data file.
- High-DPI WinForms UI and custom gold-bar icon.

## Data Storage

Default data file:

`%APPDATA%\GameCurrencyTracker\currency_records.json`

Settings file:

`%APPDATA%\GameCurrencyTracker\settings.json`

The current data schema is `schemaVersion: 2`. The generalized version starts with an empty account list for new data files. Older fixed-account files are treated as incompatible and should be kept only as legacy backups.

## Project Layout

- `tools/currency-tracker-winforms/`: Windows WinForms app, assets, and compiled exe.
- `tools/currency-tracker-winforms/assets/`: gold-bar icon source and generated icon files.
- `tools/currency-tracker-winforms/dist/金条更新记录.exe`: self-contained Windows executable.
- `build.ps1`: rebuilds the WinForms executable from source.

## Rebuild

From the repository root:

```powershell
.\build.ps1
```
