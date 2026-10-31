# Settings

Chooses which features the menu shows, and in what order.

| Item      | Value       |
|-----------|-------------|
| Menu path | Settings    |
| Status    | Implemented |

## Purpose

Keep the menu down to what someone uses, in the order they reach for it. Settings that belong to one
feature stay on that feature's page.

## Page layout

1. Page header: title "Settings", subtitle "Settings".
2. A **Menu** card listing every menu entry in menu order, indented below its group. Each row has the
   entry's title, **Up** and **Down**, and a switch.

## Showing and hiding

| Rule | Behaviour |
|------|-----------|
| Switch | A toggle switch, not a checkbox. Takes effect in the menu at once |
| Group switched off | The group and everything in it leave the menu. The rows below it are disabled and keep their state |
| Every entry of a group switched off | The group leaves the menu as well, even with its own switch on |
| Settings | Its switch is on and disabled, so the page can always be reached |
| Hidden feature | Stays loaded. What it has running keeps running, its data is kept, and its help can still be opened |

## Ordering

**Up** and **Down** swap an entry with its neighbour among the entries that share its parent, hidden
ones included. An entry cannot move to another group. **Up** is disabled on the first entry and
**Down** on the last.

## Start-up

Joktan opens on the first leaf in the menu that is shown, with the groups above it expanded. With the
menu as it comes, this is Dashboard / Overview.

## Saved state

Each change is written straight away to `menu.json` in the [data folder](../../../src/Joktan.Help/Topics/settings-and-data.md):
the entries in order, as a tree, each with its title and whether it is shown.

| Case | Behaviour |
|------|-----------|
| No file, or one that cannot be read | The menu as it comes |
| An entry in the app but not in the file, such as a new feature | Shown, directly after the entry it follows in the app |
| An entry in the file but not in the app | Ignored, and dropped at the next save |
| A renamed entry | Entries are matched by title among their siblings, so it counts as a new one |
| Settings marked as hidden in the file | Shown |
