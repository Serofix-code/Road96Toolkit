using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

// Lists the fields and methods of Road 96 classes from the BepInEx interop DLLs.
//
//   TypeDump find <word or regex>          which assembly a class is in
//   TypeDump <Assembly> <Class> [Class...] every field, property and method of those classes
//
// Set ROAD96 to your game folder if it is not the default Steam one.

static class Program
{
    static string _bep;

    static int Main(string[] args)
    {
        var game = Environment.GetEnvironmentVariable("ROAD96");
        if (string.IsNullOrEmpty(game)) game = @"C:\Program Files (x86)\Steam\steamapps\common\Road 96";
        _bep = Path.Combine(game, "BepInEx");
        if (!Directory.Exists(Path.Combine(_bep, "interop")))
        {
            Console.WriteLine("No BepInEx/interop folder in " + game + ". Start the game with BepInEx once, or set ROAD96.");
            return 1;
        }
        if (args.Length < 2)
        {
            Console.WriteLine("TypeDump find <word>");
            Console.WriteLine("TypeDump <Assembly> <Class> [Class...]");
            Console.WriteLine("example: TypeDump NarrativeBrain NarrativeContext Run");
            return 1;
        }

        AssemblyLoadContext.Default.Resolving += (c, n) =>
        {
            foreach (var d in new[] { "interop", "core" })
            {
                var p = Path.Combine(_bep, d, n.Name + ".dll");
                if (File.Exists(p)) return c.LoadFromAssemblyPath(p);
            }
            return null;
        };

        if (args[0] == "find") { Find(args[1]); return 0; }

        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(_bep, "interop", args[0] + ".dll"));
        var types = Types(asm);
        foreach (var name in args.Skip(1))
        {
            var t = types.FirstOrDefault(x => x.Name == name || x.FullName == name);
            if (t == null) { Console.WriteLine("not found: " + name); continue; }
            Dump(t);
        }
        return 0;
    }

    static Type[] Types(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
    }

    static void Find(string pattern)
    {
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var f in Directory.GetFiles(Path.Combine(_bep, "interop"), "*.dll"))
        {
            var n = Path.GetFileName(f);
            if (n.StartsWith("Unity") || n.StartsWith("Il2Cpp") || n.StartsWith("System") || n.StartsWith("mscorlib")) continue;
            Assembly a;
            try { a = AssemblyLoadContext.Default.LoadFromAssemblyPath(f); } catch { continue; }
            foreach (var t in Types(a))
                if (rx.IsMatch(t.Name))
                    Console.WriteLine(Path.GetFileNameWithoutExtension(f) + ": " + t.FullName + " : " + BaseName(t));
        }
    }

    static string BaseName(Type t)
    {
        try { return t.BaseType?.Name; } catch { return "?"; }
    }

    static string Name(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        var n = t.Name;
        int i = n.IndexOf('`');
        if (i > 0) n = n.Substring(0, i);
        return n + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
    }

    static void Dump(Type t)
    {
        Console.WriteLine("== " + t.FullName + " : " + BaseName(t));
        if (t.IsEnum)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                Console.WriteLine("  " + f.Name + " = " + Convert.ToInt64(f.GetRawConstantValue()));
            return;
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var p in t.GetProperties(all))
        {
            if (p.Name.StartsWith("NativeFieldInfoPtr") || p.Name.StartsWith("NativeMethodInfoPtr")) continue;
            bool st = (p.GetMethod ?? p.SetMethod)?.IsStatic == true;
            Console.WriteLine("  " + (st ? "static " : "") + Name(p.PropertyType) + " " + p.Name + (p.CanWrite ? "" : "  (get only)"));
        }
        foreach (var m in t.GetMethods(all))
        {
            if (m.IsSpecialName || m.Name.StartsWith("Native")) continue;
            var ps = string.Join(", ", m.GetParameters().Select(p => Name(p.ParameterType) + " " + p.Name));
            Console.WriteLine("  " + (m.IsStatic ? "static " : "") + Name(m.ReturnType) + " " + m.Name + "(" + ps + ")");
        }
    }
}
