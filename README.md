# LeveFinder

Given an FFXIV levemete's ENpc ID, print every levequest that levemete can hand out.

```
$ LeveFinder.exe "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" 1001788

// Swygskyf (1001788) - Levemete
// 21 leves, levemete for Swiftperch [Armorer, Battlecraft, Blacksmith, Culinarian, Fisher]
// 2 unused row(s) excluded: 552, 554
Leves = new()
{
    156, 157, 158, 216, 217, 218, 276, 277, 278, 547, 548, 549, 550, 551, 553,
    555, 556, 762, 763, 764, 765,
}
```

Output is a C# collection initialiser, ready to paste into a data table.

## Why this is not a one-line lookup

There is no column in the game data that says "this NPC hands out these leves," and the two columns that look like they do are both misleading:

- **`Leve.Level{Levemete}`** points at a real levemete for Battlecraft, Miner, Botanist and Grand Company leves — but on crafting and fishing leves it points at the *client*, the lore character commissioning the work. Of the 68 NPCs it names, only 33 are levemetes; the other 35 can't hand out anything.
- **`Leve.PlaceName{Issued}`** does not decide who offers a crafting leve. Leve 158 is tagged "issued at Limsa Lominsa" with a Limsa client, yet it is handed out by Swygskyf at Swiftperch.

The crafting classes are allocated in `LeveRewardItem` groups that straddle the city/settlement boundary, and three city-state hub levemetes behave differently again. [docs/DATA-RELATIONSHIP.md](docs/DATA-RELATIONSHIP.md) walks through the whole thing with the evidence.

## Accuracy

The rules are derived from the game data and verified against live in-game state, captured with a debugger plugin, for six levemetes — all three city-state hubs and three settlements:

| NPC | ID | Role | Leves | Result |
|---|---|---|---|---|
| Swygskyf | 1001788 | Swiftperch | 21 | exact match |
| Orwen | 1001791 | Aleport | 20 | exact match |
| Nyell | 1000823 | Quarrymill | 87 | exact match |
| T'mokkri | 1000970 | Limsa Lominsa hub | 162 | every observed row present |
| Gontrant | 1000101 | Gridania hub | 188 | every observed row present |
| Eustace | 1001794 | Ul'dah hub | 161 | every observed row present |

Between them these cover towns carrying two, three and eight crafting classes. Each hub resolves exactly its city's guilds — Blacksmith/Armorer/Culinarian for Limsa, Carpenter/Leatherworker for Gridania, Goldsmith/Weaver/Alchemist for Ul'dah.

The list of unshipped rows the tool excludes was also confirmed independently: the detection rule finds exactly 13 across all 1808 leves, matching a hand-maintained in-game ignore list with no false positives and no misses.

Note that a levemete's in-game list is **capped per class and level** — 4 battlecraft, 2 gathering, 3 crafting — so you can never see the full pool at once. This tool reports the whole pool. When comparing against the game, treat presence as evidence and absence as inconclusive.

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (the project targets net7.0) and a FFXIV installation to read.

```sh
git clone --recursive https://github.com/MrGuffels/ffxiv-levefinder.git
cd ffxiv-levefinder
dotnet build src/LeveFinder/LeveFinder.csproj
```

Already cloned without `--recursive`?

```sh
git submodule update --init
```

## Run

```sh
cd src/LeveFinder/bin/Debug/net7.0
./LeveFinder.exe "<game install path>" <npcId> [--include-unused]
```

Two things to get right:

- The path is the **install root** — the folder containing `game/`, not the `game` folder itself.
- Run the executable **from its build output directory**. SaintCoinach resolves its `Definitions/` folder relative to the working directory.

`--include-unused` keeps rows that were never released and never localised. These keep their Japanese name in the English sheet and carry a stub description; they are excluded by default and listed in the header when present.

Passing a non-levemete NPC is rejected with an explanation rather than silently returning a client's commission list.

## Credits

Built on [SaintCoinach](https://github.com/xivapi/SaintCoinach), included as a submodule, which does all the real work of reading the game's EXD files.

## Licence

WTFPL, matching SaintCoinach. See [LICENSE](LICENSE).
