using System;
using System.Collections.Generic;
using System.Linq;

using SaintCoinach;
using SaintCoinach.Ex.Relational;
using SaintCoinach.Xiv;

namespace SaintCoinach.LeveFinder {
    class Program {
        // LeveAssignmentType keys for the three Grand Companies. Where a settlement
        // has both a regular and a Grand Company levemete, these split between them.
        static readonly int[] GrandCompanyTypes = { 13, 14, 15 };

        static int Main(string[] args) {
            if (args.Length < 2) {
                Console.WriteLine("Usage: SaintCoinach.LeveFinder <gamePath> <npcId> [--include-unused]");
                return 1;
            }

            var gamePath = args[0];
            var npcId = int.Parse(args[1]);
            var includeUnused = args.Contains("--include-unused");

            var realm = new ARealmReversed(gamePath, Ex.Language.English);
            var gameData = realm.GameData;

            var npc = gameData.ENpcs.Get(npcId);
            if (npc?.Base == null) {
                Console.WriteLine($"NPC {npcId} not found.");
                return 1;
            }

            if (!IsLevemete(npc, out var levemeteType)) {
                Console.WriteLine($"// {npc.Singular} ({npcId}) is not a levemete - it has no GuildleveAssignment.");
                Console.WriteLine("// (NPCs named by Leve.Level{Levemete} on crafting/fishing leves are clients, not issuers.)");
                return 1;
            }

            var leveSheet = gameData.GetSheet<Leve>();
            var cityPlaceKeys = CityPlaceKeys(gameData);

            // Battlecraft, Miner, Botanist and Grand Company leves name their levemete
            // directly. Use those to learn which settlement this levemete serves.
            var designated = leveSheet
                .Where(lv => lv.LevemeteLevel?.Object != null && lv.LevemeteLevel.Object.Key == npcId)
                .ToArray();

            Leve[] leves;
            string basis;

            if (designated.Length > 0) {
                var place = designated[0].PlaceNameIssued;
                var wantGC = IsGrandCompany(designated[0]);

                // Battlecraft, gathering and Grand Company leves sit at the settlement
                // they are issued at.
                var own = leveSheet
                    .Where(lv => lv.PlaceNameIssued?.Key == place.Key
                                 && IsGrandCompany(lv) == wantGC
                                 && !IsCrafting(lv));

                // Crafting leves are allocated in reward-item groups that straddle the
                // boundary between a city and its outlying settlement: PlaceName{Issued}
                // and Level{Levemete} both disagree with what the game actually offers.
                // The whole group goes to the levemete of its majority issuing place.
                var craftGroups = leveSheet
                    .Where(IsCrafting)
                    .GroupBy(RewardGroupOf)
                    .Where(g => WinningPlaceKey(g, cityPlaceKeys) == place.Key)
                    .SelectMany(g => g);

                leves = wantGC ? own.ToArray() : own.Concat(craftGroups).ToArray();
                basis = $"levemete for {place}";
            } else {
                // City-state hubs (Gridania, Limsa Lominsa, Ul'dah) are designated by
                // no leve at all. They aggregate battlecraft and gathering across the
                // whole city-state, but keep only their own crafting groups.
                var townKey = ResolveTownByLocation(gameData, npcId);
                if (townKey == null) {
                    Console.WriteLine($"// {npc.Singular} ({npcId}): levemete, but no Town could be resolved.");
                    return 1;
                }
                var townName = Convert.ToString(gameData.GetSheet("Town")[townKey.Value]["Name"]);
                var cityPlaceKey = CityPlaceKey(gameData, townName);

                var townWide = leveSheet
                    .Where(lv => TownOf(lv) == townKey.Value && !IsCrafting(lv) && !IsGrandCompany(lv));

                var craftGroups = leveSheet
                    .Where(IsCrafting)
                    .GroupBy(RewardGroupOf)
                    .Where(g => WinningPlaceKey(g, cityPlaceKeys) == cityPlaceKey)
                    .SelectMany(g => g);

                leves = townWide.Concat(craftGroups).ToArray();
                basis = $"{townName} hub: city-state battlecraft/gathering + own crafting groups";
            }

            var unused = leves.Where(IsUnused).Select(lv => lv.Key).OrderBy(k => k).ToArray();
            if (!includeUnused)
                leves = leves.Where(lv => !IsUnused(lv)).ToArray();

            var leveIds = leves.Select(lv => lv.Key).OrderBy(k => k).ToArray();
            var types = leves.Select(lv => lv.LeveAssignmentType.ToString())
                .Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t);

            Console.WriteLine($"// {npc.Singular} ({npcId}) - {levemeteType}");
            Console.WriteLine($"// {leveIds.Length} leves, {basis} [{string.Join(", ", types)}]");
            if (unused.Length > 0)
                Console.WriteLine($"// {unused.Length} unused row(s) {(includeUnused ? "included" : "excluded")}: {string.Join(", ", unused)}");
            Console.WriteLine("Leves = new()");
            Console.WriteLine("{");
            for (var i = 0; i < leveIds.Length; i += 15)
                Console.WriteLine("    " + string.Join(", ", leveIds.Skip(i).Take(15)) + ",");
            Console.WriteLine("}");

            return 0;
        }

        static bool IsLevemete(ENpc npc, out string type) {
            for (var i = 0; i < ENpcBase.DataCount; i++) {
                var row = npc.Base.GetData(i);
                if (row?.Sheet?.Name == "GuildleveAssignment") {
                    type = Convert.ToString(row["Type"]);
                    return true;
                }
            }
            type = null;
            return false;
        }

        static bool IsGrandCompany(Leve leve) {
            return GrandCompanyTypes.Contains(leve.LeveAssignmentType?.Key ?? 0);
        }

        // The eight crafting classes. Only these are allocated by reward group; Fisher
        // shares Level{Levemete}'s client quirk but is distributed like gathering.
        static bool IsCrafting(Leve leve) {
            var key = leve.LeveAssignmentType?.Key ?? 0;
            return key >= 5 && key <= 12;
        }

        static int RewardGroupOf(Leve leve) {
            return Convert.ToInt32(((IRelationalRow)leve).GetRaw("LeveRewardItem"));
        }

        // The PlaceName the city itself is issued under (e.g. "Limsa Lominsa").
        static int CityPlaceKey(XivCollection gameData, string townName) {
            return gameData.GetSheet<Leve>()
                .Where(lv => lv.PlaceNameIssued != null
                             && string.Equals(lv.PlaceNameIssued.Name.ToString(), townName, StringComparison.OrdinalIgnoreCase))
                .Select(lv => lv.PlaceNameIssued.Key)
                .FirstOrDefault();
        }

        // Which levemete a crafting reward-group belongs to: the issuing place holding
        // the most of its rows. Groups straddling a city/settlement boundary can tie
        // (e.g. group 34 = one Costa del Sol row, one Limsa Lominsa row) and the city
        // takes those.
        static int WinningPlaceKey(IEnumerable<Leve> group, HashSet<int> cityPlaceKeys) {
            return group
                .Where(lv => lv.PlaceNameIssued != null)
                .GroupBy(lv => lv.PlaceNameIssued.Key)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => cityPlaceKeys.Contains(g.Key))
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        // Place keys that name a city-state rather than an outlying settlement.
        static HashSet<int> CityPlaceKeys(XivCollection gameData) {
            var townNames = gameData.GetSheet("Town")
                .Select(t => Convert.ToString(t["Name"]))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return gameData.GetSheet<Leve>()
                .Where(lv => lv.PlaceNameIssued != null && townNames.Contains(lv.PlaceNameIssued.Name.ToString()))
                .Select(lv => lv.PlaceNameIssued.Key)
                .ToHashSet();
        }

        // Unshipped rows were never localised - they keep their Japanese name in the
        // English sheet and carry a stub description.
        static bool IsUnused(Leve leve) {
            var name = leve.Name.ToString();
            return string.IsNullOrWhiteSpace(name) || name.Any(c => c >= 0x2E80);
        }

        static int TownOf(Leve leve) {
            return Convert.ToInt32(((IRelationalRow)leve).GetRaw("Town"));
        }

        static int? ResolveTownByLocation(XivCollection gameData, int npcId) {
            var mapKeys = gameData.GetSheet<Level>()
                .Where(l => l.Object != null && l.Object.Key == npcId)
                .Select(l => l.Map?.Key)
                .Where(k => k != null)
                .Distinct()
                .ToArray();

            var placeNames = gameData.GetSheet<TerritoryType>()
                .Where(t => t.Map != null && mapKeys.Contains(t.Map.Key))
                .SelectMany(t => new[] { t.PlaceName?.Name.ToString(), t.ZonePlaceName?.Name.ToString() })
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var town in gameData.GetSheet("Town")) {
                var name = Convert.ToString(town["Name"]);
                if (!string.IsNullOrEmpty(name) && placeNames.Contains(name))
                    return town.Key;
            }
            return null;
        }
    }
}
