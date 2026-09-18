# Porting LeveFinder from SaintCoinach to Lumina

Branch: `lumina-backend`. Goal: a second engine that reads game data through
Lumina (the library ChilledLeves already uses at runtime via
`Lumina.Excel.Sheets` + Dalamud's `Svc.Data`), so the leve-resolution logic
can eventually run live inside the ChilledLeves plugin process instead of
only as an offline generator. `main` keeps the proven SaintCoinach engine
untouched until this one matches it.

## Distribution: NuGet, not copied DLLs

`Lumina` and `Lumina.Excel` are published on nuget.org (current: `Lumina.Excel`
7.5.0). No need to reference the DLLs out of a local XIVLauncher/Dalamud
install — a standalone console project can just
`dotnet add package Lumina.Excel` like SaintCoinach's own NuGet packages.
That keeps this buildable by anyone who clones the repo, same as today.

Version note: ChilledLeves resolves its Lumina version indirectly through
`Dalamud.NET.Sdk/14.0` (`net10.0-windows8.0`), so it's pinned to whatever
Lumina build that Dalamud release ships. Pin the standalone project to the
closest matching `Lumina.Excel` version so struct shapes line up before any
later in-plugin migration — check `Dalamud.NET.Sdk`'s own dependency pin
when scaffolding.

## What's confirmed from ChilledLeves' own usage ([ExcelHelper.cs](../../ChilledLeves/ChilledLeves/Utilities/ExcelHelper.cs))

- Modern Lumina generates row structs per sheet in `Lumina.Excel.Sheets`,
  each an `IExcelRow<T>` over an `ExcelPage`/offset/row — not POCOs like
  SaintCoinach's.
- Access pattern: `ExcelSheet<T>.GetRow(id)`; generic-reference columns
  resolve through `.Value` (a `RowRef`/`RowRef<T>`), e.g.
  `TerritoryType.PlaceName.Value.Name`.
- Not every sheet has generated bindings. ChilledLeves already had to
  hand-write one (`LeveGuildleveAssignment`, sheet path `leve/GuildleveAssignment`)
  with a manual `[Sheet("...")] struct` implementing `IExcelRow<T>` and raw
  `page.ReadString(offset, ...)` calls. **`ENpcBase` and the top-level
  `GuildleveAssignment` sheet SaintCoinach uses may need the same manual
  treatment** — first thing to check when scaffolding, not assume away.

## Column-by-column porting risk — resolved by reflecting on the real assembly

`src/LeveFinder.Lumina` is a diagnostic console app (no game install needed)
that loads the pinned Lumina.Excel assembly and reflects over the row
structs the SaintCoinach engine depends on. Run with `dotnet run` from that
folder. Findings, much better than feared:

| Current (SaintCoinach) | Lumina equivalent (confirmed) |
|---|---|
| `npc.Base.GetData(i)` scanning `ENpcBase.DataCount` generic-ref slots for a `GuildleveAssignment` row | `ENpcBase.ENpcData` is `Collection<RowRef>` (untyped). Iterate it and call `.Is<GuildleveAssignment>()` / `.GetValueOrDefault<GuildleveAssignment>()` per slot — same loop shape as today. |
| `leve.LevemeteLevel.Object` (`Leve.Level{Levemete}` → `Level` → `Level.Object`) | `Leve.LevelLevemete` is a named `RowRef<Level>`; `Level.Object` is an untyped `RowRef` (can point at more than `ENpcBase`, same as SaintCoinach) — resolve with `.GetValueOrDefault<ENpcBase>()`. |
| `((IRelationalRow)leve).GetRaw("LeveRewardItem")` raw-column hack | **Named property**: `Leve.LeveRewardItem` is `RowRef<T>`. No raw access needed at all. |
| `((IRelationalRow)leve).GetRaw("Town")` raw-column hack | **Named property**: `Leve.Town` is `RowRef<Town>`. No raw access needed. |
| `leve.PlaceNameIssued`, `leve.LeveAssignmentType` | Named `RowRef<T>` properties, as expected — least risky part confirmed. |
| `gameData.GetSheet("Town")` (untyped SaintCoinach sheet) | `Town` **is** a generated sheet (`Lumina.Excel.Sheets.Town`, `Name`/`Icon`/`RowId`) — no hand-written struct needed, unlike the guess in the first draft of this doc. |

The two `RowRef` shapes in play:
- `RowRef<T>` (named typed columns): `.Value` (direct `T`), `.ValueNullable`, `.IsValid`, `.RowId`.
- `RowRef` (untyped — `ENpcData` elements, `Level.Object`, `Level.EventId`): `.Is<T>()`, `.GetValueOrDefault<T>()`, `.TryGetValue<T>(out T)`, `.RowId`, `.RowType`.

Net effect: this port needs **zero** hand-written `[Sheet(...)]` structs (the
`LeveGuildleveAssignment` workaround ChilledLeves needed was for a different,
unrelated sheet path — not a sign this project needs the same treatment).
Every accessor the current engine uses has a direct, named equivalent.

Confirmed pinned versions (read off the user's current Dalamud dev install
file version info): `Lumina.dll` 7.6.0, `Lumina.Excel.dll` 7.5.1. NuGet only
had `Lumina.Excel` up to 7.5.0 at scaffold time; used that instead (assembly
version reports as 7.0.0.0 regardless — Lumina doesn't bump `AssemblyVersion`
with every release, so this mismatch is expected, not a sign of drift).

## Sequence

1. Scaffold `src/LeveFinder.Lumina` — standalone console app, NuGet-referenced
   Lumina/Lumina.Excel, same CLI shape as today (`gamePath npcId`) so it's
   testable without a running game or Dalamud.
2. Resolve the "is this NPC a levemete" test first (`ENpcBase` → `GuildleveAssignment`
   scan) in isolation — it's the cheapest possible slice to prove the RowRef
   plumbing works at all, and every fixture NPC depends on it.
3. Port the two-hop `Level{Levemete}` → settlement resolution next — needed
   for the non-hub path (Swygskyf/Orwen/Nyell fixtures).
4. Port `LeveRewardItem` grouping and the `Town` raw column — needed for
   crafting groups and the three hub fixtures.
5. Regression: rerun all 6 fixtures from
   [project_verification_ground_truth](../../.claude/projects/c--Users-mrben-Documents-GitHub-ffxiv-levefinder/memory/project_verification_ground_truth.md),
   diff as ID sets against the known-good lists, not by eye.
6. Only after fixtures pass: swap the standalone `Lumina.GameData` bootstrap
   for `Svc.Data`/ECommons calls to prove the same resolution code runs live
   inside a Dalamud plugin process.
7. Merge to `main` only once the new engine's output matches the old one
   across all 36 ARR levemetes, not just the 6 fixtures.

## Status

- **Step 1 (scaffold) — done.** Struct shapes confirmed, see table above.
- **Step 2 (levemete test) — done and verified against real game data.**
  `dotnet run -- "<gamePath>"` in `src/LeveFinder.Lumina` loads
  `game/sqpack` directly with `Lumina.GameData` (no ARealmReversed
  equivalent needed — pass the sqpack folder, not the install root) and
  finds **exactly 36 NPCs** with a `GuildleveAssignment`, the same 36 IDs
  the SaintCoinach engine finds. Confirms `RowRef.Is<T>()` /
  `.GetValueOrDefault<T>()` works correctly on `ENpcBase.ENpcData`.
  Bonus: `GuildleveAssignment.Type` also confirms every post-ARR levemete
  in ChilledLeves' hardcoded list (Eloin/Temple, Eirikur/Crystarium,
  Grigge/Gleaner, Malihali/Tuliyollal) is a real levemete, not a client.
- **Next: step 3**, the two-hop `Level{Levemete}` → settlement resolution
  (`Leve.LevelLevemete.Value.Object`), needed for the non-hub fixtures
  (Swygskyf/Orwen/Nyell).
