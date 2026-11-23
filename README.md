# InventoryCleaner

Frees up your Armoury Chest by moving out gear your gearsets don't use.

> **Warning:** this moves items for you, which counts as automation under the FFXIV User
> Agreement. Use it at your own risk.

## What it keeps

- Anything you have equipped
- Anything in a saved gearset
- Upgrades for jobs you still play

Everything else moves to your bags, starting with the fullest armoury slot.

## Nothing moves without your OK

Every clean shows a preview first: each item, and why it stays or goes. Nothing moves until you
confirm.

## More tools

Reverse puts gearset pieces from your bags back into the Armoury Chest.

The discard picker shows leftover gear in a grid so you can choose what to throw away. Equipped
gear, gearset pieces and anything on your allowlist never show up there.

## Install

In `/xlsettings`, under Experimental, add this to Custom Plugin Repositories:

```
https://plugins.xivhub.net/pluginmaster.json
```

Then install InventoryCleaner from the plugin installer.

## Commands

| Command | What it does |
|---|---|
| `/inventorycleaner` | Open the main window |
| `/inventorycleaner clean` | Preview a clean |
| `/inventorycleaner reverse` | Preview putting gearset pieces back |
| `/inventorycleaner confirm` | Run the preview |
| `/inventorycleaner cancel` | Throw the preview away |
| `/inventorycleaner discard` | Open the discard picker |
| `/inventorycleaner stop` | Stop a run |

Building from source: see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## License

AGPL-3.0-or-later. See `LICENSE`.
