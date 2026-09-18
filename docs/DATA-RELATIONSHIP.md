# How an FFXIV NPC ID ties back to its levequests

## Summary

There is no single column that says "this NPC hands out these leves," and the two columns that look like they do are both misleading. The working rule:

1. **Confirm the NPC is a levemete** — it must carry a `GuildleveAssignment` in its `ENpcData`. Exactly 36 NPCs do.
2. **Find the settlement it serves** — read `PlaceName{Issued}` off any leve that names it via `Leve.Level{Levemete}`.
3. **Battlecraft, Miner, Botanist and Fisher** leves: everything issued at that settlement.
4. **The eight crafting classes**: allocated in `LeveRewardItem` groups, *not* by issuing place. A group goes to the place holding most of its rows; ties go to the city.

City-state hubs are the exception at steps 2–3: no leve names them, so their settlement comes from their map placement, and they aggregate battlecraft/gathering/fisher across the whole `Leve.Town`. Step 4 still applies to them unchanged — hubs have their own crafting leves, they just do not inherit the settlements'.

Two traps:

- `Leve.Level{Levemete}` does **not** always point at a levemete. On crafting and fishing leves it points at the *client*.
- `PlaceName{Issued}` does **not** decide who offers a crafting leve. Leve 158 is issued at Limsa Lominsa but is offered by Swygskyf at Swiftperch.

### Verification status

The model reproduces in-game debugger output for six levemetes — all three city-state hubs (T'mokkri `1000970`, Gontrant `1000101`, Eustace `1001794`) and three settlements (Swygskyf `1001788`, Orwen `1001791`, Nyell `1000823`). The settlement results match exactly; the hub results contain every observed row across multi-capture scrolls of their lists.

Note that the in-game list is **capped per class and level** — 4 battlecraft, 2 gathering, 3 crafting. You can never see a levemete's full pool at once, so absence from an in-game capture is not evidence that a leve is not offered. Only presence is.

## The sheets involved

| Sheet | Role |
|---|---|
| `ENpcBase` / `ENpcResident` | The NPC. Parallel sheets sharing one key (e.g. `1000970` = T'mokkri). `Base` holds behaviour data, `Resident` holds names. |
| `GuildleveAssignment` | Marks an NPC as a levemete and gives its flavour (`Levemete`, `Grand Company Leves`, `Temple Leves`, `Crystarium Leves`, `Gleaner Leves`, `Tuliyollal Leves`). |
| `Level` | A placement in the world: coordinates, a `Map`, and an `Object` pointing at whatever stands there. |
| `Leve` | The levequest itself. |
| `Town` | The city-state a leve belongs to. Two columns only: `Name`, `Icon`. |

## Step 1 — is this NPC a levemete?

`ENpcBase` carries 32 generic-reference slots, `ENpcData[0..31]`. Each raw value encodes both a target sheet and a row; SaintCoinach decodes it via `ENpcBase.GetData(i)`, which returns a row in whatever sheet the value points to. For T'mokkri, slot 0 decodes to a `GuildleveAssignment` row with `Type = Levemete`.

```
ENpcBase#1000970 → ENpcData[0] → GuildleveAssignment (Type = Levemete)
```

**Exactly 36 NPCs carry a `GuildleveAssignment`, and that is the complete levemete roster.** This is the only trustworthy "is a levemete" test. It does not tell you *which* leves — `GuildleveAssignment` holds only a type, a dialogue pointer and two unlock `Quest` links.

## Step 2 — `Level{Levemete}` is overloaded

`Leve` has a column `Level{Levemete}` (index 26) resolving through `Level` back to an `ENpcBase`:

```
Leve.Level{Levemete} → Level → Level.Object → ENpcBase
```

The name suggests it always gives the issuing levemete. It does not. Measured across all 1808 leves:

| `LeveAssignmentType` | Object is a flagged levemete | Count |
|---|---|---|
| Battlecraft | always | 191 |
| Miner | always | 130 |
| Botanist | always | 130 |
| The Maelstrom / Twin Adder / Immortal Flames | always | 22 each |
| Carpenter, Blacksmith, Armorer, Goldsmith, Leatherworker, Weaver, Alchemist, Culinarian | **never** | 140 each |
| Fisher | **never** | 120 |

Zero exceptions in either direction. On the crafting and fishing rows the resolved NPC's name always appears inside the `LeveClient` text — e.g. leve 155 resolves to Fewon Bulion and its client reads *"Client: Independent Exporter, Fewon Bulion"*. Those NPCs are **clients**, the lore characters commissioning the work. Bango Zango, Fewon Bulion, Q'molosi, Moyce and Shue-Hann are all clients, not levemetes; none of them carries a `GuildleveAssignment`.

So of the 68 distinct NPCs named by this column, 33 are genuine levemetes and 35 are clients. Treating the column as an issuer list silently invents levemetes that cannot hand out anything in game.

Its real use is narrower but still essential: on the six types where it *is* a levemete, it tells you which settlement that levemete serves.

## Step 3 — `PlaceName{Issued}` groups everything except crafting

Group every leve by `PlaceName{Issued}` and each settlement resolves to exactly one regular levemete, plus at most one Grand Company levemete:

| Issued at | Leves | Levemete(s) |
|---|---|---|
| Swiftperch | 23 | Swygskyf |
| Red Rooster Stead | 30 | Wyrkholsk |
| Aleport | 20 | Orwen |
| Costa del Sol | 56 | Nahctahr + C'lafumyn (GC) |
| Camp Drybone | 29 | Poponagu + Kikiri (GC) |
| Quarrymill | 103 | Nyell |
| The Crystarium | 165 | Eirikur |
| Tuliyollal | 120 | Malihali |
| Kugane | 165 | Keltraeng |
| Old Sharlayan | 120 | Grigge |
| Foundation | 360 | Eloin |
| **Gridania / Limsa Lominsa / Ul'dah** | 48 / 80 / 72 | **none** |

For **Battlecraft, Miner, Botanist and Fisher** this is the whole story: the levemete supplies everything issued at its settlement, regardless of which client commissioned it.

Where a Grand Company levemete shares a settlement, split on `LeveAssignmentType`: keys 13/14/15 (The Maelstrom, Order of the Twin Adder, Immortal Flames) go to the GC levemete, everything else to the regular one.

**Crafting leves do not follow this** — see the next section.

## Step 3b — crafting is allocated by `LeveRewardItem`

The eight crafting classes are handed out in `LeveRewardItem` groups (column index 23) that deliberately straddle the city/settlement boundary. Level-10 Blacksmith:

| ID | Issued at | Client | Reward group |
|---|---|---|---|
| 153 | Limsa Lominsa | Bango Zango | 26 |
| 154 | Limsa Lominsa | Bango Zango | 26 |
| 155 | **Swiftperch** | Fewon Bulion | 26 |
| 156 | Swiftperch | Fewon Bulion | 27 |
| 157 | Swiftperch | Fewon Bulion | 27 |
| 158 | **Limsa Lominsa** | Bango Zango | 27 |

In game, T'mokkri offers 153/154/155 and Swygskyf offers 156/157/158 — the reward groups, *not* the issuing places. Both `PlaceName{Issued}` and `Level{Levemete}` would give 155/156/157, which is wrong.

**A group belongs to the place holding most of its rows.** Group 26 is 2× Limsa → the city; group 27 is 2× Swiftperch → Swygskyf. The odd row out travels with its group.

From level 30 the groups become a pair plus a singleton, and the pair can tie:

| Level | Group | Members | Owner |
|---|---|---|---|
| 30 | 34 | 177 (Costa del Sol), 178 (Limsa) | tie → **city** |
| 30 | 241 | 179 (Limsa) | city |
| 30 | 35 | 180, 181 (Costa del Sol) | Costa del Sol |
| 30 | 242 | 182 (Costa del Sol) | Costa del Sol |

**On a tie the city wins.** This is confirmed by T'mokkri's in-game list containing 177, 178 and 179 while Costa del Sol's levemete takes 180, 181, 182. The net effect across a tier is that the city takes the first three of each six-leve block and the settlement takes the last three.

## Step 4 — the three city hubs

Gridania, Limsa Lominsa and Ul'dah have leves issued in their name but **no levemete designated there**. Their counters — **Gontrant** (`1000101`), **T'mokkri** (`1000970`) and **Eustace** (`1001794`) — are named by zero leves, so step 2 yields nothing to locate them with.

For battlecraft, gathering and fisher leves these three aggregate their entire city-state via `Leve.Town` (column index 5). T'mokkri's in-game list contains 511 (Red Rooster Stead), 547–551 (Swiftperch), 574–580 (Aleport), 763/765 (Swiftperch fisher) and 779/781 (Costa del Sol fisher) — settlements right across La Noscea.

Hubs carry plenty of crafting leves of their own — 90 of T'mokkri's 162 are Blacksmith, Armorer and Culinarian. What they do *not* do is aggregate crafting the way they aggregate the other categories: a hub gets only the reward groups the city itself wins, which is why T'mokkri shows 153/154/155 and never Swiftperch's 156/157/158.

Which crafts appear at all follows the city's guilds. Limsa Lominsa has the Blacksmiths', Armorers' and Culinarians' guilds, so Town#1 carries no Carpenter, Goldsmith, Leatherworker, Weaver or Alchemist leves — matching T'mokkri's in-game list.

`Town` is **jurisdictional, not geographic**: leve 167 is issued at Quarrymill in the South Shroud but belongs to Town#1, Limsa Lominsa. Region- or map-based matching therefore cannot reproduce it.

Town pools: 1 Limsa Lominsa 240 · 2 Gridania 321 · 3 Ul'dah 239 · 4 Ishgard 387 · 7 Kugane 165 · 10 Crystarium 165 · 12 Old Sharlayan 120 · 14 Tuliyollal 120 · 0 unassigned 51.

### Resolving a hub's Town

`Town` has no location or NPC link, so derive it from placement:

```
Level.Object == npcId  →  Level.Map
TerritoryType where TerritoryType.Map == that map
  →  collect PlaceName + PlaceName{Zone} names
  →  string-match against Town.Name
```

Gotcha: `Map.TerritoryType` comes back empty for some maps (map 11, Limsa Lominsa Upper Decks, among them), so the lookup must run **backwards** — scan `TerritoryType` for rows whose `Map` matches, rather than reading `TerritoryType` off the `Map` row.

## Unshipped rows

Some leves were never released and never localised. They keep their Japanese name in the English sheet and carry a stub description of 94–101 characters, against 531–731 for live rows.

Detect them by testing the name for any character at or above `U+2E80` while reading the English sheet. Across all 1808 rows that yields exactly 13:

| ID | Lvl | Type | Town | Issued at | Name |
|---|---|---|---|---|---|
| 508 | 1 | Battlecraft | Limsa Lominsa | Red Rooster Stead | 獲得任務：オーレリアのバラスト袋 |
| 514 | 1 | Battlecraft | Ul'dah | Scorpion Crossing | 獲得任務：装飾用の羽 |
| 525 | 5 | Battlecraft | Limsa Lominsa | Red Rooster Stead | 討伐任務：シープを狙う獣たち |
| 531 | 5 | Battlecraft | Ul'dah | Scorpion Crossing | 討伐任務：ウルダハ近郊の害虫駆除 |
| 552 | 10 | Battlecraft | Limsa Lominsa | Swiftperch | 懐柔任務：入植地の番犬候補 |
| 554 | 10 | Battlecraft | Limsa Lominsa | Swiftperch | 捜索任務：ばらまかれた妖花の種 |
| 562 | 10 | Battlecraft | Ul'dah | Horizon | 懐柔任務：発破代わりのボム |
| 564 | 10 | Battlecraft | Ul'dah | Horizon | 捜索任務：ペイストの罠 |
| 582 | 15 | Battlecraft | Ul'dah | Camp Drybone | 追撃任務：砂を呼ぶゴート |
| 597 | 20 | Battlecraft | Limsa Lominsa | Moraby Drydocks | 討伐任務：放牧されたドードー退治 |
| 822 | 30 | The Maelstrom | Limsa Lominsa | Costa del Sol | 援護指令：第426洞穴団せん滅作戦 |
| 827 | 30 | Order of the Twin Adder | Gridania | Camp Tranquil | 援護指令：白狼隊の実戦訓練 |
| 832 | 30 | Immortal Flames | Ul'dah | Little Ala Mhigo | 援護指令：アラミゴ志願兵の実戦訓練 |

This list was confirmed against an independently maintained in-game ignore list — 13 for 13, no false positives and no misses.

They are all Battlecraft or Grand Company; no crafting or gathering row is affected. The Grand Company entries are exactly one per company, all level 30, all named 援護指令 ("support order"). The ten battlecraft rows split five Limsa / five Ul'dah with none for Gridania, and use task-type name prefixes the live leves never do: 獲得 (acquisition), 討伐 (subjugation), 懐柔 (pacification), 捜索 (search), 追撃 (pursuit).

Note that the console renders these as `????:????????` under a non-UTF-8 code page, which looks like a placeholder string but is not — inspect the char codes, or set `Console.OutputEncoding`, rather than trusting the printed text.

### Blank padding rows

Separate and unrelated: 51 rows are entirely empty — every field zero, no name, no `Town`, no place. They occupy IDs 0–20, 662–673 and a sparse run through 836–877. These are sheet padding rather than unshipped content, and no place- or town-based rule will ever return them.

## Limits

Applying `Town` to a *regional* levemete is wrong — it would hand Swygskyf, a Swiftperch counter with 21 leves, the entire 240-leve Limsa pool. Regional levemetes never touch `Town`; they resolve through `PlaceName{Issued}` and reward groups.

`Town` is used only for the three hubs, in two distinct ways:

- **As a membership filter**, for battlecraft, gathering and fisher only: every leve with that `Town` is in the hub's pool.
- **As an identifier**, for crafting: the town's `Name` resolves to the city's own `PlaceName` key, and that key decides which crafting reward groups the hub wins.

So a hub's crafting leves do depend on `Town`, just not by direct membership. Hubs have crafting leves like any other levemete — 90 of T'mokkri's 162.

Sub-level gating (class, level, unlock quest) lives in `ClassJobCategory`, `ClassJobLevel` and the `GuildleveAssignment.Quest` links rather than in any field above. The tool reports a levemete's full pool, not what a given character sees at a given moment.

## Reference: `LeveAssignmentType`

| Key | Name |
|---|---|
| 1 | Battlecraft |
| 2 | Miner |
| 3 | Botanist |
| 4 | Fisher |
| 5–12 | Carpenter, Blacksmith, Armorer, Goldsmith, Leatherworker, Weaver, Alchemist, Culinarian |
| 13 | The Maelstrom |
| 14 | Order of the Twin Adder |
| 15 | Immortal Flames |

`GuildleveAssignmentCategory` groups these into 6 buckets (battlecraft / gathering / crafting / one per Grand Company), but it is a generic grouping table — not keyed per NPC, and it does not narrow a levemete's pool.

## Implementation

See [`src/LeveFinder/Program.cs`](../src/LeveFinder/Program.cs). Build and usage instructions are in the [README](../README.md).

Output is a C# collection initialiser of leve IDs, plus a header stating which rule fired and which categories the NPC covers. Non-levemete NPCs are rejected with an explanation rather than returning a client's commission list. Unshipped rows are excluded and listed; pass `--include-unused` to keep them.
