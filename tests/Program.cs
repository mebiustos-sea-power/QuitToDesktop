using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || (args[0] != "core" && args[0] != "game"))
                throw new ArgumentException("Supply core/game, the game directory, and the Anchor Chain directory.");
            string[] search = { Path.Combine(args[1], "Sea Power_Data", "Managed"), Path.Combine(args[1], "BepInEx", "core"), args[2] };
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string name = new AssemblyName(request.Name).Name + ".dll";
                foreach (string directory in search)
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            Run(args[0] == "game", args[1]);
            Console.WriteLine("PASS " + checks + " checks on " + args[0] + ". The game was not launched and Application.Quit was not invoked.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(bool game, string directory)
    {
        Console.WriteLine("Runtime: " + typeof(object).Assembly.Location);
        CoreTests.Run();
        StartupTests.Run();
        ActivationTests.Run();
        if (game) NativeTests.Run(directory);
    }

    internal static void Check(string name, Action action)
    { action(); checks++; Console.WriteLine("PASS " + name); }
    internal static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
