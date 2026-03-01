namespace RestartAMDAdrenalin.Models;

// Snapshot of Discovered Games and the Time it Was Generated
public sealed record GameCache(DateTime GeneratedUtc, HashSet<string> ProcessNames);
