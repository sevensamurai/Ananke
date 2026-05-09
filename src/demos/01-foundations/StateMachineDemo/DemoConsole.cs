namespace StateMachineDemo;

/// <summary>
/// Shared console output helpers for the demo sections.
/// </summary>
static class DemoConsole
{
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"-- {title}");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void Say(string msg) => Console.WriteLine(msg);

    public static void Dim(string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    public static void PrintBanner()
    {
        Console.WriteLine("--------------------------------------------------------------");
        Console.WriteLine("  Ananke — StateMachineDemo  |  Car Engine IoT (Distributed)");
        Console.WriteLine();
        Console.WriteLine("   Parked --[Start]--> Running --[Drive]--> Moving");
        Console.WriteLine("     ^                   ^                    |");
        Console.WriteLine("     |               [Resume]             [Halt]");
        Console.WriteLine("     |                   |                    |");
        Console.WriteLine("     +---[Park]--- Idle <---------------------+");
        Console.WriteLine();
        Console.WriteLine("  Usage:  dotnet run                  (sections 1-3)");
        Console.WriteLine("          dotnet run -- --mqtt        (+ MQTT section)");
        Console.WriteLine("--------------------------------------------------------------");
    }
}
