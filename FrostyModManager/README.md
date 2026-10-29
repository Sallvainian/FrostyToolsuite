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
1. Pick the game. Steam installs with a matching profile are detected automatically, anything else via *Browse…*.
2. First time only, for games that need it (e.g. Dead Space): start the game, wait for the main menu, then click
   *Set up from running game*. This reads the initfs key and the type sdk out of the game's memory.
3. Add mods with *Add mods…* or by dropping `.fbmod`, `.fbcollection` or `.zip` files onto the window. Frosty 1.x mods
   are converted on import. Mods higher in the list win conflicts.
4. *Launch modded* generates `ModData/Default` in the game folder, copies the bcrypt shim next to the exe, adds a
   per-exe `bcrypt=native,builtin` override to the Proton prefix (only possible while the game is closed) and starts
   the game through Steam with `-dataPath ModData\Default`.
