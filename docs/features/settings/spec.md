# Settings

Chooses which features the menu shows and in what order, and the colours, fonts and text sizes of the
app, kept in themes.

| Item      | Value       |
|-----------|-------------|
| Menu path | Settings / Menu, Themes, Colours, Fonts, Text Sizes |
| Status    | Implemented |

## Purpose

Keep the menu down to what someone uses, in the order they reach for it, and let the app look the way
they want it to. Settings that belong to one feature stay on that feature's page.

## Pages

Settings is a group in the menu, with one page for each kind of setting.

| Page | Holds |
|------|-------|
| Menu | Which entries the menu shows, and their order |
| Themes | The theme in use, and removing saved themes |
| Colours | Every colour of the theme, and whether its controls are dark |
| Fonts | Every font of the theme |
| Text Sizes | Every text size of the theme |

## Menu page layout

1. Page header: title "Menu", subtitle "Settings / Menu".
2. A **Menu** card listing every menu entry in menu order, indented below its group. Each row has the
   entry's title, **Up** and **Down**, and a switch.

## Showing and hiding

| Rule | Behaviour |
|------|-----------|
| Switch | A toggle switch, not a checkbox. Takes effect in the menu at once |
| Group switched off | The group and everything in it leave the menu. The rows below it are disabled and keep their state |
| Every entry of a group switched off | The group leaves the menu as well, even with its own switch on |
| Settings and Settings / Menu | Their switches are on and disabled, so the page can always be reached. The other pages of Settings can be hidden |
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
| Settings, or Settings / Menu, marked as hidden in the file | Shown |

## Themes

A theme has a name and holds every colour, font and text size in the app, and whether controls take
their dark look. The tokens are listed in [`ThemeTokens`](../../../src/Joktan.Settings/Themes/ThemeTokens.cs),
and their use in the [design](../../design.md).

| Choice | Behaviour |
|--------|-----------|
| Follow system | Light or Dark according to the operating system, changing when it does. The default |
| Light, Dark | Built in: the values in `Tokens.axaml` |
| Green Dark | Built in: Dark with green surfaces, menu and accent |
| A saved theme | One the user made |

### Changing a theme

The Themes, Colours, Fonts and Text Sizes pages all have the same **Theme** card at the top, naming the
theme and whether it has changes not saved.

| Rule | Behaviour |
|------|-----------|
| A change | Shows in the whole app at once, and is held in a working copy until saved |
| **Save** | Writes the changes to the theme. Disabled for a built-in theme, which says to save as new |
| **Save as new** | Saves what is showing under a new name and changes to it. The name is required, and must differ from every other theme's in more than case |
| **Discard** | Goes back to the theme as last saved |
| Undoing a change by hand | The theme counts as unchanged again |
| Choosing another theme | Disabled until the changes are saved or discarded |
| Closing the app | Changes not saved are lost |
| **Delete** | Saved themes only, at once. If it is in use, the app goes back to Follow system |

### Values

| Kind | Control | Rule |
|------|---------|------|
| Colour | Picker and `#RRGGBB` text | Text that is not a colour outlines the box and changes nothing. There is no alpha. Meter tracks are the accent, warning and danger colours at low opacity, and Fluent's accent steps are worked out from the accent |
| Font | List of the fonts on this computer | Interface defaults to Inter, which comes with Joktan; Headings, Brand and Menu to the Interface font; Code to the system's monospaced font. A font this computer does not have is listed as not installed, kept, and drawn in Inter |
| Text size | Number from 8 to 72 | An empty box changes nothing |

Icons, spacing and the height of rows do not follow the text sizes, so very large text can be clipped.

### Saved state

The theme chosen and the saved themes are in `themes.json` in the data folder. A theme is read at
start-up before the splash screen opens.

| Case | Behaviour |
|------|-----------|
| No file, or one that cannot be read | Follow system, and no saved themes |
| The theme chosen no longer exists | Follow system |
| A value missing or unusable, such as after an update adds a token | Taken from Light or Dark, whichever matches the theme's controls. Sizes outside the range are brought within it |
| A saved theme named like a built-in one or an earlier saved one | Ignored, and dropped at the next save |
