namespace RestartAMDAdrenalin.Utilities;

internal static class TextMatchers
{
    internal static bool ContainsAnyToken(string value, string[] tokens)
    {
        // Check Each Token Against the Value
        foreach (var token in tokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsAnyMarker(string value, string[] markers)
    {
        // Check Each Marker Against the Value
        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
