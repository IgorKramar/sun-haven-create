# Verification record

## Native game checkpoint: 14 October 2026

| Component | Tested configuration |
| --- | --- |
| Sun Haven | v3.1.3b, Steam build 24721658 |
| Engine | Unity 2022.3.62f2, Mono |
| Platform | Windows, local single-player |
| Loader | BepInEx 5.4.23.5 x64 |
| Mod | 0.3.0 |

Testing used a separate CreateLab character. The original four character saves matched their backups after testing. No personal saves or game assemblies are published.

| Check | Observed outcome |
| --- | --- |
| Build the five-machine chain | 64 logs → 43 logs: exact 21-log payment |
| Deposit and mill three wheat | Native wheat 30 → 27; three flour produced |
| Collect one flour | One flour in native inventory; two in output chest |
| Save, return to menu and reload | Five machines and the same resources restored |
| Try collection with full inventory | Output buffer retained; no item removed |
| Dismantle empty wheel | Eight logs refunded; remaining network stopped |
| Rebuild wheel | Eight logs paid; power restored |
| Render first generated art | Sprites viewed beside native scenery |
| Open workshop note | Original Russian text displayed in native dialogue |
| Repeat milling with new art | Three more wheat consumed; chest rose from two to five flour |

Small state snapshots are in [evidence](evidence/). Screenshots show a development character and an unfinished vanilla tutorial, not a finished factory or final visual polish.

## Source checkpoint: 31 October 2026

All 17 engine-independent checks passed again in the prepared public tree:

```sh
dotnet run --project tests/Kinetics.Checks.csproj --configuration Release
```

These cover conservation, transport speed, loss of power, gear ratios, source conflicts, overload/recovery, full buffers, occupied placement, invalid time and save validation. CI runs the same checks. It does not run Sun Haven or build against proprietary game assemblies.

No fresh game launch was performed during repository preparation. Compatibility with game updates after the native checkpoint, multiplayer and combinations with other mods remains unverified.
