<img align="center" src="Resources/FrostyBannerChucky296.svg">

<p align="center">
  <a title="Latest release" href="https://github.com/Sallvainian/FrostyToolsuite/releases">
    <img alt="Latest release" src="https://img.shields.io/github/v/release/Sallvainian/FrostyToolsuite?include_prereleases&label=RELEASE&color=3CCFC4&logo=linux&logoColor=white">
  </a>
  <a title="License" href="LICENSE">
    <img alt="License" src="https://img.shields.io/badge/LICENSE-GPL--3.0-white">
  </a>
</p>

> [!WARNING]
> This is a fork built on the in-development FrostyToolsuite v2 rewrite. It has been tested with
> **Dead Space (2023)** on Steam with Proton. Other Frostbite games have profiles but are untested here.
>
> **Use at your own risk**, and use *Restore vanilla* before Steam updates or verifies a modded game.

# Frosty Mod Manager for Linux
A native Linux mod manager for Frostbite games, built on the [FrostyToolsuite](https://github.com/FrostyToolsuite/FrostyToolsuite)
.NET 8 rewrite. No Wine-hosted Windows tools are needed: the manager and the `frosty` command line tool run natively.
Only the game itself runs under Proton.

Frosty 1.x's mod manager only runs on Windows, and the Dead Space builds of it are Windows-only forks. This fork adds
what Linux needs to mod those games:

- **A native GUI** ([FrostyModManager](FrostyModManager/README.md)) in Avalonia with a dark, Frosty-style workflow:
  mod library, packs, load order, mod details and screenshots, and a log.
- **Steam detection.** Steam installs that match a Frosty profile show up automatically, with their Steam artwork. Any
  other install can be added by picking its `.exe`.
- **One-time setup from the running game.** Games like Dead Space need an initfs key and a type SDK. Both are read out
  of the running game's memory, so no key has to be pasted in from somewhere.
- **Imports most Nexus downloads as-is.** `.fbmod`, `.fbcollection`, `.zip`, `.rar` and `.7z` all work. Frosty 1.x mods
  are converted to the v2 format on import.
- **Compile into the game folder.** Dead Space ignores `-dataPath`, so *Compile* builds the modded data, moves the
  originals into `<game>/FrostyBackup` and links the modded files in their place. It also sets up the Proton DLL
  override the game needs. *Restore vanilla* puts everything back.
- **Launch through Steam** with one click. The manager reminds you to compile if the load order changed.
- **SDK fixes for Linux:**
  - Oodle is fetched for games that link it statically (Dead Space has no `oo2core` DLL).
  - Writable data lives in `~/.local/share/frosty` instead of next to the executable.

## Install
Download a build from the [releases page](https://github.com/Sallvainian/FrostyToolsuite/releases). Every format
contains both the mod manager (`frosty-mod-manager`) and the CLI (`frosty`). All are self-contained x86_64 builds, so
no .NET install is needed.

| Format | Use it on | How |
|---|---|---|
| `.AppImage` | Any distro, including immutable ones (Bazzite, SteamOS, Fedora Atomic) | `chmod +x` it and run it. `<appimage> cli --help` runs the CLI. |
| `.tar.xz` / `.tar.gz` | Any distro, without root | Extract, then run `./install.sh`. It adds an app-menu entry and `~/.local/bin` commands. `--uninstall` removes them. |
| `.deb` | Debian, Ubuntu, Mint, Pop!_OS | `sudo apt install ./frosty-mod-manager-*.deb` |
| `.rpm` | Fedora, openSUSE | `sudo dnf install ./frosty-mod-manager-*.rpm` |
| `.pkg.tar.zst` | Arch, Manjaro, EndeavourOS, CachyOS | `sudo pacman -U frosty-mod-manager-*.pkg.tar.zst` |

Keys, SDKs, caches, mods and settings are stored in `~/.local/share/frosty` (`$XDG_DATA_HOME/frosty`) whichever format
you install, so switching formats keeps everything. Set `FROSTY_DATA_DIR` to use another folder.

## Quick start (Dead Space)
1. Open **Frosty Mod Manager**. Dead Space is picked up from your Steam library.
2. **First time only:** start the game, wait for the main menu, then click **Set up now** in the manager. Once the
   setup banner is gone, you can quit the game.
3. Click **Add mods**, or drag downloads from Nexus onto the window.
4. Click **+** on a mod to add it to the load order. When two mods change the same file, the one lower in the list wins.
5. With the game closed, click **Compile**, then **Launch**.

Turn a mod's switch off and compile again to remove it from the game. **Restore vanilla** removes every mod. See the
[FrostyModManager README](FrostyModManager/README.md) for details.

## Command line
The `frosty` CLI ([README](FrostyCli/README.md)) can load games, build `ModData` folders, update old mods and create
mods. This fork adds `find-initfs-key`, which recovers a game's initfs key from the running process:
```bash
frosty find-initfs-key "$HOME/.local/share/Steam/steamapps/common/Dead Space (2023)/Dead Space.exe"
```

## Known limitations
- x86_64 Linux only. Launching and auto-detection go through Steam.
- Games that need bundle or CAS keys are not handled by the GUI yet. Use the CLI for those.
- The game must be closed to compile or restore. The manager will not load a game that is running with compiled mods.

# Building from source
You need [Git](https://git-scm.com/downloads) and the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
(newer SDKs also build it).
```bash
git clone https://github.com/Sallvainian/FrostyToolsuite.git
cd FrostyToolsuite
dotnet build
```

To build the same packages as a release, run the packaging script. The output goes to `packaging/linux/dist`:
```bash
packaging/linux/build.sh 1.0.0
```
Pushing a `v*` tag runs the same script on GitHub Actions and publishes a release. Tags with a `-` suffix, like
`v1.1.0-beta.1`, are published as prereleases.

# Project structure

### FrostyModManager
The GUI used to pick which mods to apply to the game. In this fork it is the native Linux mod manager described above.

### FrostyCli
A CLI used to create, update and apply mods. See its [README](FrostyCli/README.md).

### FrostySdk
A library for reading the game's data.

### FrostyModSupport
A library that builds modified data the game can read.

### FrostyTypeSdkGenerator
A source generator that improves the type SDK dumped from the game's memory.

### FrostyEditor
The GUI for creating mods. It is still early in upstream development and is not part of the Linux packages.

# Credits
This is a fork of [FrostyToolsuite/FrostyToolsuite](https://github.com/FrostyToolsuite/FrostyToolsuite), the .NET 8
rewrite of the [original FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite). All the Frostbite format work
comes from the Frosty team; this fork adds the Linux mod manager, packaging and the changes listed above. Questions
about this fork belong in its [issues](https://github.com/Sallvainian/FrostyToolsuite/issues), not on the upstream
Discord.

If you want to contribute to upstream Frosty, read their [CodingStandards.cs](CodingStandards.cs) and join the
[Frosty Discord](https://discord.gg/sB8ZUAT).

Licensed under the [GPL-3.0](LICENSE). Third-party notices are in [NOTICE.md](NOTICE.md).
