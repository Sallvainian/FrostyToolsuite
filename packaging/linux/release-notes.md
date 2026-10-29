Native Linux build of **Frosty Mod Manager** (FrostyToolsuite v2 with a new Avalonia GUI), plus the `frosty` command line tool. Tested with Dead Space (2023) on Steam/Proton.

### Which file do I want?
| File | Use it on |
|---|---|
| `*.AppImage` | Any distro, including immutable ones (Bazzite, SteamOS, Fedora Atomic). `chmod +x` and run. |
| `*.tar.xz` / `*.tar.gz` | Any distro without root: extract and run `./install.sh` |
| `*.deb` | Debian / Ubuntu / Mint / Pop!_OS |
| `*.rpm` | Fedora / openSUSE |
| `*.pkg.tar.zst` | Arch / Manjaro / EndeavourOS / CachyOS |

All are x86_64 and self-contained (no .NET install needed). Your keys, sdks and mods live in `~/.local/share/frosty`, shared by every format.

### Using it
Pick the game, do the one-time setup from the running game if asked, add mods (`.fbmod`, `.fbcollection`, `.zip`, `.rar`, `.7z`; Frosty 1.x mods are converted automatically), add them to the load order, **Compile** with the game closed, then **Launch**. **Restore vanilla** undoes everything. Restore vanilla before Steam updates or verifies the game, then compile again.

Verify downloads with `sha256sum -c SHA256SUMS --ignore-missing`.
