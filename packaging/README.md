# Packaging

Files used when Joktan is packaged for distribution. Nothing here is compiled into the app.

| File                  | Purpose                                                                |
|-----------------------|------------------------------------------------------------------------|
| `app-icon/joktan.svg` | The app icon as a vector drawing; the source for every other icon file |
| `macos/Joktan.icns`   | The icon for a macOS `.app` bundle, with the margin macOS icons have   |

The icons the running app uses are in `src/Joktan.Core/Assets/`:

| File                                         | Used for                                                              |
| -------------------------------------------- | --------------------------------------------------------------------- |
| `src/Joktan.Core/Assets/joktan.ico`          | Window icons, and the icon of the Windows executable (16 to 256 px)   |
| `src/Joktan.Core/Assets/joktan-icon.png`     | The About window and the splash screen                                |
| `src/Joktan.Core/Assets/joktan-icon-mac.png` | The macOS Dock icon when the app is run without a bundle              |

If the icon changes, edit `app-icon/joktan.svg` and regenerate the other files from it.
