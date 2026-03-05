namespace RestartAMDAdrenalin.Utilities;

internal static class Logger
{
    // Pad Width Matches "[HH:mm:ss] " (11 chars) so List Items Align Under the Header Text
    private static readonly string s_pad = new(' ', 11);

    internal static void Log(string message, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void LogList(
        string header,
        IReadOnlyList<string> items,
        ConsoleColor itemColor = ConsoleColor.Gray
    )
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(header);
        foreach (var item in items)
        {
            Console.Write(s_pad);
            Console.ForegroundColor = itemColor;
            Console.WriteLine($"- {item}");
        }
        Console.ResetColor();
    }

    internal static void LogHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(title);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('-', title.Length));
        Console.ResetColor();
    }

    internal static void LogItem(string item, ConsoleColor color = ConsoleColor.Gray)
    {
        Console.Write(s_pad);
        Console.ForegroundColor = color;
        Console.WriteLine($"- {item}");
        Console.ResetColor();
    }
}
