# Contributing

Start with [limitations in the README](README.md#текущие-ограничения) and the [roadmap](ROADMAP.md). Describe the game build, loader version, mod version, reproduction steps and expected/actual behavior when reporting a bug. Redact personal paths, character names and unrelated log contents. Do not attach personal saves, game DLLs, keys or extracted assets to public issues.

Run the 17 checks with .NET SDK 10. Building the plugin also requires your own Sun Haven installation and BepInEx references; [build.ps1](build.ps1) packages only the mod, its original sprites and the redistributable JSON dependency. Test gameplay changes in a separate backed-up single-player world and record what was actually verified.

For screenshots, new lore, generated art or adapted mechanics, record sources and distinguish completed behavior from proposals. Native multiplayer is not supported by the current alpha.
