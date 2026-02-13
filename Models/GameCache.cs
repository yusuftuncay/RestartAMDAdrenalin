namespace RestartAMDAdrenalin.Models;

public sealed record GameCache(DateTime GeneratedUtc, HashSet<string> ProcessNames);
