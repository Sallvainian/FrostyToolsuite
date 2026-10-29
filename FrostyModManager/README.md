# FrostyModManager

A native (Avalonia) mod manager on top of FrostySdk/FrostyModSupport. Built and tested on Linux with Dead Space (2023)
running through Proton.

## Build
```bash
cd FrostyModManager
dotnet publish -c Release --runtime linux-x64 --self-contained /p:DebugType=None
```
Publish FrostyCli into the same folder if you want both, they share `Profiles`, `Sdk`, `Keys`, `Caches` and `ThirdParty`.

## Usage
1. Pick the game (top right). Steam installs with a matching profile are detected automatically, anything else via
   ⋮ → *Select game executable…*.
2. First time only, for games that need it (e.g. Dead Space): start the game, wait for the main menu, then click
   *Set up now*. This reads the initfs key and the type sdk out of the game's memory.
3. Add mods with *Add mods* or by dropping `.fbmod`, `.fbcollection` or `.zip` files onto the window. Frosty 1.x mods
   are converted on import. Click **+** on a mod (or double-click it) to add it to the load order of the current pack;
   like Frosty 1.x, the mod lower in the load order wins when two mods change the same file.
4. **Compile** generates `ModData/Default` and installs it into the game folder: the original files are moved to
   `FrostyBackup` and the modded files are symlinked in their place. It also copies the bcrypt shim next to the exe and
   adds a per-exe `bcrypt=native,builtin` override to the Proton prefix. The game must be closed.
5. **Launch** starts the game through Steam. Dead Space ignores `-dataPath`, which is why mods are compiled into the
   game folder instead of passed as a launch argument.
6. **Restore vanilla** moves the originals back and removes the modded files.
