using System;
using System.Linq;
using System.Reflection;

namespace LeveFinder.Lumina {
    // Diagnostic-only entry point for the Lumina port (see docs/LUMINA-PORT-PLAN.md).
    // Dumps what the pinned Lumina.Excel version actually generates for the
    // sheets the SaintCoinach engine depends on, so the open questions in the
    // plan doc get answered from the real assembly instead of guessed at.
    // No game install needed - this only reflects over the row struct shapes.
    class Program {
        static readonly string[] SheetsOfInterest = { "ENpcBase", "ENpcResident", "Leve", "Level", "Town", "GuildleveAssignment" };

        static int Main(string[] args) {
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

            return 0;
        }
    }
}
