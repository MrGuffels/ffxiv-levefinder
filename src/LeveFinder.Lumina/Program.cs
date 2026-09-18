using System;
using System.Linq;
using System.Reflection;

using Lumina.Excel.Sheets;

namespace LeveFinder.Lumina {
    // Diagnostic entry point for the Lumina port (see docs/LUMINA-PORT-PLAN.md).
    //
    //   dotnet run                    - dump the row-struct shapes this port depends
    //                                    on, no game install needed.
    //   dotnet run -- <gamePath>      - load real sqpack data and port step 2 of the
    //                                    plan: the "is this NPC a levemete" test,
    //                                    checked against the known 36-NPC roster.
    class Program {
        static readonly string[] SheetsOfInterest = { "ENpcBase", "ENpcResident", "Leve", "Level", "Town", "GuildleveAssignment" };

        static int Main(string[] args) {
            if (args.Length == 0) {
                DumpSheetShapes();
                return 0;
            }

            return ScanLevemetes(args[0]);
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
                    Console.WriteLine($"  {prop.PropertyType.Name,-24} {prop.Name}");
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
