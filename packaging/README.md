# Packaging

Files used when Joktan is packaged for distribution. Nothing here is compiled into the app.

| File                  | Purpose                                                                |
|-----------------------|------------------------------------------------------------------------|
| `app-icon/joktan.svg` | The app icon as a vector drawing; the source for every other icon file |
| `macos/Joktan.icns`   | The icon for a macOS `.app` bundle, with the margin macOS icons have   |

The icons the running app uses are in `Assets/`:

| File                         | Used for                                                            |
|------------------------------|---------------------------------------------------------------------|
| `Assets/joktan.ico`          | Window icons, and the icon of the Windows executable (16 to 256 px) |
| `Assets/joktan-icon.png`     | The About window                                                    |
| `Assets/joktan-icon-mac.png` | The macOS Dock icon when the app is run without a bundle            |

If the icon changes, edit `app-icon/joktan.svg` and regenerate the other files from it.
