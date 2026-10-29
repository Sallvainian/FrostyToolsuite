# FrostyModManager

A native (Avalonia) mod manager on top of FrostySdk/FrostyModSupport. Built and tested on Linux with Dead Space (2023)
running through Proton.

## Install
Grab a build from the [releases](https://github.com/Sallvainian/FrostyToolsuite/releases). Every format contains both
the mod manager (`frosty-mod-manager`) and the command line tool (`frosty`).

| Format | For | How |
|---|---|---|
| `.AppImage` | any distro, including immutable ones (Bazzite, SteamOS, Silverblue) | `chmod +x` it and run it; `<appimage> cli --help` runs the CLI |
| `.tar.xz` / `.tar.gz` | any distro, no root | extract, then `./install.sh` (adds an app-menu entry and `~/.local/bin` commands; `--uninstall` removes them) |
| `.deb` | Debian, Ubuntu, Mint, Pop!_OS | `sudo apt install ./frosty-mod-manager_*.deb` |
| `.rpm` | Fedora, openSUSE | `sudo dnf install ./frosty-mod-manager-*.rpm` |
| `.pkg.tar.zst` | Arch, Manjaro, EndeavourOS, CachyOS | `sudo pacman -U frosty-mod-manager-*.pkg.tar.zst` |

Keys, sdks, caches, mods and settings live in `~/.local/share/frosty` (`$XDG_DATA_HOME/frosty`) no matter which format is
installed, so switching formats keeps everything. Set `FROSTY_DATA_DIR` to use another folder.

## Build
```bash
packaging/linux/build.sh 1.0.0        # every format into packaging/linux/dist
```
Pushing a `v*` tag builds the same set on GitHub Actions and publishes it as a release. For a quick local build of just
the app:
```bash
dotnet publish FrostyModManager -c Release --runtime linux-x64 --self-contained /p:DebugType=None -o out
dotnet publish FrostyCli -c Release --runtime linux-x64 --self-contained /p:DebugType=None -o out
```

## Usage
1. Pick the game (top right). Steam installs with a matching profile are detected automatically, anything else via
   ⋮ → *Select game executable…*.
2. First time only, for games that need it (e.g. Dead Space): start the game, wait for the main menu, then click
   *Set up now*. This reads the initfs key and the type sdk out of the game's memory.
3. Add mods with *Add mods* or by dropping `.fbmod`, `.fbcollection`, `.zip`, `.rar` or `.7z` files onto the window. Frosty 1.x mods
   are converted on import. Click **+** on a mod (or double-click it) to add it to the load order of the current pack;
   like Frosty 1.x, the mod lower in the load order wins when two mods change the same file.
4. **Compile** generates `ModData/Default` and installs it into the game folder: the original files are moved to
   `FrostyBackup` and the modded files are symlinked in their place. It also copies the bcrypt shim next to the exe and
   adds a per-exe `bcrypt=native,builtin` override to the Proton prefix. The game must be closed, and the manager
   refuses to load a game that is running with compiled mods.
5. **Launch** starts the game through Steam. Dead Space ignores `-dataPath`, which is why mods are compiled into the
   game folder instead of passed as a launch argument.
6. **Restore vanilla** moves the originals back and removes the modded files.
