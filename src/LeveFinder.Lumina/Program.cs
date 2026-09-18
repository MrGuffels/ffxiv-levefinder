using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Lumina.Excel.Sheets;
using GameData = Lumina.GameData;

namespace LeveFinder.Lumina {
    // Entry point for the Lumina port (see docs/LUMINA-PORT-PLAN.md).
    //
    //   dotnet run                          - dump the row-struct shapes this port
    //                                          depends on, no game install needed.
    //   dotnet run -- <gamePath>            - port step 2: the "is this NPC a
    //                                          levemete" test, checked against the
    //                                          known 36-NPC roster.
    //   dotnet run -- <gamePath> <npcId>    - resolve one levemete's leves, same
    //                                          shape as the SaintCoinach CLI.
    class Program {
        static readonly string[] SheetsOfInterest = { "LeveAssignmentType" };
        static readonly uint[] GrandCompanyTypes = { 13, 14, 15 };

        static int Main(string[] args) {
            if (args.Length == 0) {
                DumpSheetShapes();
                return 0;
            }

            if (args.Length == 1)
                return ScanLevemetes(args[0]);

            return ResolveOne(args[0], uint.Parse(args[1]));
        }

        static int ResolveOne(string gamePath, uint npcId) {
            var gameData = new GameData(System.IO.Path.Combine(gamePath, "game", "sqpack"));
            var (leveIds, unused, basis, levemeteType) = ResolveLeves(gameData, npcId);
            if (leveIds == null) {
                Console.WriteLine(basis); // basis carries the error message on failure
                return 1;
            }

            Console.WriteLine($"// NPC {npcId} - {levemeteType}");
            Console.WriteLine($"// {leveIds.Length} leves, {basis}");
            if (unused.Length > 0)
                Console.WriteLine($"// {unused.Length} unused row(s) excluded: {string.Join(", ", unused)}");
            Console.WriteLine(string.Join(",", leveIds));
            return 0;
        }

        // Direct port of the SaintCoinach engine in ../LeveFinder/Program.cs, using
        // the RowRef/RowRef<T> API confirmed by DumpSheetShapes and the levemete
        // scan. Returns (null, errorMessage, null, null) on failure.
        static (uint[] leveIds, uint[] unused, string basis, string levemeteType) ResolveLeves(GameData gameData, uint npcId) {
            var npcSheet = gameData.GetExcelSheet<ENpcBase>();
            var npc = npcSheet.GetRowOrDefault(npcId);
            if (npc == null)
                return (null, null, $"NPC {npcId} not found.", null);

            if (!IsLevemete(npc.Value, out var levemeteType))
                return (null, null, $"NPC {npcId} is not a levemete - it has no GuildleveAssignment.", null);

            var allLeves = gameData.GetExcelSheet<Leve>().ToArray();
            var cityPlaceKeys = CityPlaceKeys(gameData, allLeves);

            var designated = allLeves
                .Where(lv => lv.LevelLevemete.IsValid && lv.LevelLevemete.Value.Object.RowId == npcId)
                .ToArray();

            Leve[] leves;
            string basis;

            if (designated.Length > 0) {
                var placeKey = designated[0].PlaceNameIssued.RowId;
                var wantGC = IsGrandCompany(designated[0]);

                var own = allLeves.Where(lv => lv.PlaceNameIssued.RowId == placeKey
                                                && IsGrandCompany(lv) == wantGC
                                                && !IsCrafting(lv));

                var craftGroups = allLeves.Where(IsCrafting)
                    .GroupBy(RewardGroupOf)
                    .Where(g => WinningPlaceKey(g, cityPlaceKeys) == placeKey)
                    .SelectMany(g => g);

                leves = wantGC ? own.ToArray() : own.Concat(craftGroups).ToArray();
                basis = $"levemete for place {placeKey}";
            } else {
                var townKey = ResolveTownByLocation(gameData, npcId);
                if (townKey == null)
                    return (null, null, $"NPC {npcId}: levemete, but no Town could be resolved.", null);

                var townName = gameData.GetExcelSheet<Town>().GetRow(townKey.Value).Name.ToString();
                var cityPlaceKey = CityPlaceKey(allLeves, townName);

                var townWide = allLeves.Where(lv => lv.Town.RowId == townKey.Value && !IsCrafting(lv) && !IsGrandCompany(lv));

                var craftGroups = allLeves.Where(IsCrafting)
                    .GroupBy(RewardGroupOf)
                    .Where(g => WinningPlaceKey(g, cityPlaceKeys) == cityPlaceKey)
                    .SelectMany(g => g);

                leves = townWide.Concat(craftGroups).ToArray();
                basis = $"{townName} hub";
            }

            var unused = leves.Where(IsUnused).Select(lv => lv.RowId).OrderBy(k => k).ToArray();
            var kept = leves.Where(lv => !IsUnused(lv)).Select(lv => lv.RowId).OrderBy(k => k).ToArray();
            return (kept, unused, basis, levemeteType);
        }

        static bool IsGrandCompany(Leve leve) => GrandCompanyTypes.Contains(leve.LeveAssignmentType.RowId);

        static bool IsCrafting(Leve leve) {
            var key = leve.LeveAssignmentType.RowId;
            return key >= 5 && key <= 12;
        }

        static uint RewardGroupOf(Leve leve) => leve.LeveRewardItem.RowId;

        static bool IsUnused(Leve leve) {
            var name = leve.Name.ToString();
            return string.IsNullOrWhiteSpace(name) || name.Any(c => c >= 0x2E80);
        }

        static HashSet<uint> CityPlaceKeys(GameData gameData, Leve[] allLeves) {
            var townNames = gameData.GetExcelSheet<Town>()
                .Select(t => t.Name.ToString())
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return allLeves
                .Where(lv => lv.PlaceNameIssued.IsValid && townNames.Contains(lv.PlaceNameIssued.Value.Name.ToString()))
                .Select(lv => lv.PlaceNameIssued.RowId)
                .ToHashSet();
        }

        static uint CityPlaceKey(Leve[] allLeves, string townName) {
            return allLeves
                .Where(lv => lv.PlaceNameIssued.IsValid
                             && string.Equals(lv.PlaceNameIssued.Value.Name.ToString(), townName, StringComparison.OrdinalIgnoreCase))
                .Select(lv => lv.PlaceNameIssued.RowId)
                .FirstOrDefault();
        }

        static uint WinningPlaceKey(IEnumerable<Leve> group, HashSet<uint> cityPlaceKeys) {
            return group
                .Where(lv => lv.PlaceNameIssued.IsValid)
                .GroupBy(lv => lv.PlaceNameIssued.RowId)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => cityPlaceKeys.Contains(g.Key))
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        static uint? ResolveTownByLocation(GameData gameData, uint npcId) {
            var mapKeys = gameData.GetExcelSheet<Level>()
                .Where(l => l.Object.RowId == npcId && l.Object.Is<ENpcBase>())
                .Select(l => l.Map.IsValid ? l.Map.RowId : (uint?)null)
                .Where(k => k != null)
                .Distinct()
                .ToArray();

            var placeNames = gameData.GetExcelSheet<TerritoryType>()
                .Where(t => t.Map.IsValid && mapKeys.Contains(t.Map.RowId))
                .SelectMany(t => new[] {
                    t.PlaceName.IsValid ? t.PlaceName.Value.Name.ToString() : null,
                    t.PlaceNameZone.IsValid ? t.PlaceNameZone.Value.Name.ToString() : null,
                })
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var town in gameData.GetExcelSheet<Town>()) {
                var name = town.Name.ToString();
                if (!string.IsNullOrEmpty(name) && placeNames.Contains(name))
                    return town.RowId;
            }
            return null;
        }

        static string DescribeType(Type t) {
            if (!t.IsGenericType) return t.Name;
            var args = string.Join(", ", t.GetGenericArguments().Select(DescribeType));
            return $"{t.Name.Split('`')[0]}<{args}>";
        }

        static void DumpSheetShapes() {
            var excelAsm = Assembly.Load("Lumina.Excel");
            var sheetsNamespace = excelAsm.GetTypes().Where(t => t.Namespace == "Lumina.Excel.Sheets").ToArray();

            Console.WriteLine($"Lumina.Excel {excelAsm.GetName().Version}: {sheetsNamespace.Length} generated sheet types in Lumina.Excel.Sheets.\n");

            foreach (var name in SheetsOfInterest) {
                var type = sheetsNamespace.FirstOrDefault(t => t.Name == name);
                if (type == null) {
                    Console.WriteLine($"=== {name}: NOT generated - needs a hand-written [Sheet(\"...\")] struct, like ChilledLeves did for leve/GuildleveAssignment. ===\n");
                    continue;
                }

                Console.WriteLine($"=== {name} ({type.FullName}) ===");
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
                    Console.WriteLine($"  {DescribeType(prop.PropertyType),-34} {prop.Name}");
                Console.WriteLine();
            }

            foreach (var typeName in new[] { "Lumina.Excel.RowRef", "Lumina.Excel.RowRef`1", "Lumina.Excel.Collection`1" }) {
                var type = excelAsm.GetType(typeName) ?? Assembly.Load("Lumina").GetType(typeName);
                Console.WriteLine($"=== {typeName} ({(type == null ? "not found" : type.AssemblyQualifiedName)}) ===");
                if (type == null) continue;
                foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
                    Console.WriteLine($"  {m.MemberType,-10} {m}");
                Console.WriteLine();
            }
        }

        // Port of Program.IsLevemete from the SaintCoinach engine: an NPC is a
        // levemete iff one of its ENpcData slots resolves to a GuildleveAssignment
        // row. ENpcData is an untyped RowRef collection - each slot can point at a
        // different sheet, so each has to be probed with .Is<T>().
        static int ScanLevemetes(string gamePath) {
            var sqpack = System.IO.Path.Combine(gamePath, "game", "sqpack");
            Console.WriteLine($"Loading sqpack from: {sqpack}");

            var gameData = new global::Lumina.GameData(sqpack);
            var npcSheet = gameData.GetExcelSheet<ENpcBase>();

            var levemetes = npcSheet
                .Select(npc => (npc.RowId, type: IsLevemete(npc, out var t) ? t : null))
                .Where(x => x.type != null)
                .ToArray();

            Console.WriteLine($"{levemetes.Length} NPCs carry a GuildleveAssignment (expected 36, per docs/DATA-RELATIONSHIP.md).\n");
            foreach (var (rowId, type) in levemetes)
                Console.WriteLine($"  {rowId,10}  {type}");

            return levemetes.Length == 36 ? 0 : 1;
        }

        static bool IsLevemete(ENpcBase npc, out string type) {
            foreach (var slot in npc.ENpcData) {
                if (slot.Is<GuildleveAssignment>()) {
                    type = slot.GetValueOrDefault<GuildleveAssignment>()?.Type.ToString();
                    return true;
                }
            }
            type = null;
            return false;
        }
    }
}
