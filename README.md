# CruciblePilot (dev plugin, v0.1)

AutoDuty drives the board; this plugin does the two things AutoDuty can't:

1. **Sets the degree** (AutoDuty can, Crucible SUX can't) and pushes the run settings for one board.
2. **Puts the chosen trio on the battlehorns every fight** and presses "start battle"
   (AD's `FightPicks` is turned off so the two don't fight over the UI).

## Build & deploy

```powershell
pwsh -File .\build.ps1
```

Copies `CruciblePilot.dll` + `CruciblePilot.json` into
`%APPDATA%\XIVLauncher\devPlugins\CruciblePilot\`.

In game: `/xlplugins` -> **Dev Plugins** -> enable **CruciblePilot**.

## Commands

`/cp` opens the settings window (same as `/cp ui`). Chat subcommands still work:

| Command | Meaning |
|---|---|
| `/cp board 1-5` | 1=Novice I, 2=Novice II, 3=Novice III, 4=Advanced I, 5=Advanced II |
| `/cp degree standard|first|second|third` | difficulty for the next `/cp start` |
| `/cp trio <a> <b> <c>` | familiar numbers (`#` from the Bestiary) for the selected board |
| `/cp auto on|off` | battlehorn autopilot |
| `/cp start` | push settings to AutoDuty (team, degree, no FightPicks) and queue one run |
| `/cp stop` | stop AutoDuty |
| `/cp status` | board/degree/trio, AD IPC state, current horn rows |
| `/cp team` | force AutoDuty's CustomTeam to the trio via reflection |
| `/cp dump` | dump the open `XBMPetParty` rows (familiar number, HP, selection index) |

## How the autopilot works

Every 350 ms, while in a board territory (1339-1343) with `XBMPetParty` + `XBMStageDetailList` open:

- reads the roster rows from the addon's AtkValues (entry count at index 5, entries at `6 + i*77`,
  familiar number at +76, selection index at +74),
- toggles rows (`FireCallback(1, rowIndex)`) until the configured trio occupies selection indices 0-2,
- presses start (`FireCallback(8)` on the stage detail) and answers the "unfilled horns" prompt with Yes.

## Known limits (v0.1)

- The trio is **static per board**; it does not read the current encounter yet (that's the next step:
  encounter-aware picks using the enemies in the arena / board node).
- `CustomTeam` is set by reflection into AutoDuty's loaded assembly because AD's config-override IPC
  rejects list values. If that breaks after an AutoDuty update, set the team in AD's Crucible tab and
  use `/cp auto on` only (roster must contain the trio).
- If AutoDuty's own `FightPicks` is left on, both plugins will grab the horn UI - `/cp start` turns it off.
