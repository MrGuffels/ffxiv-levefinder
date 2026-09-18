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

## Column-by-column porting risk (from [Program.cs](../src/LeveFinder/Program.cs))

Every accessor in the current engine either uses a SaintCoinach-only API or
a raw-column workaround that has no direct Lumina equivalent yet:

| Current (SaintCoinach) | Lumina concern |
|---|---|
| `npc.Base.GetData(i)` scanning `ENpcBase.DataCount` generic-ref slots for a `GuildleveAssignment` row | Lumina's generated `ENpcBase` may expose `ENpcData` as a fixed-size array of `RowRef` (untyped, since each slot can point at different sheets) — needs manual per-slot resolution, same shape as today's loop but through `RowRef.Is<GuildleveAssignment>()`/`.GetValueOrDefault<T>()` if that API exists in the pinned version. |
| `leve.LevemeteLevel.Object` (`Leve.Level{Levemete}` → `Level` → `Level.Object`) | Same two-hop `RowRef` chase; property names on the generated `Leve`/`Level` structs need discovering — not guaranteed to match SaintCoinach's friendly names. |
| `((IRelationalRow)leve).GetRaw("LeveRewardItem")` — raw column access because SaintCoinach's `Leve` wrapper doesn't name it | No `IRelationalRow` in Lumina. If the generated `Leve` struct doesn't expose this column by name either, need offset-based raw read like the `LeveGuildleveAssignment` workaround above. |
| `((IRelationalRow)leve).GetRaw("Town")` | Same concern as above. |
| `leve.PlaceNameIssued`, `leve.LeveAssignmentType` | Probably fine as named properties — least risky part of the port. |
| `gameData.GetSheet("Town")` (untyped sheet, only `Name`/`Icon` columns) | `Town` has no generated Lumina binding used anywhere in ChilledLeves today — may need a hand-written struct, same pattern as `LeveGuildleveAssignment`. |

None of this is a rename exercise — each row needs verifying against the
actual generated struct in the pinned Lumina.Excel version, then rechecking
against the fixtures.

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

## Open questions to resolve while scaffolding

- Does the pinned Lumina.Excel version expose `ENpcBase.ENpcData` at all,
  and as what type?
- Does its generated `Leve` struct name `LeveRewardItem`/`Town`, or do those
  need the manual-struct workaround?
- Which Lumina.Excel version does the current `Dalamud.NET.Sdk/14.0` pin
  ship, so the standalone project matches what ChilledLeves will eventually
  run against?
