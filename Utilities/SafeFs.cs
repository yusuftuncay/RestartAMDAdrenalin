namespace AdrenalinRestart.Utilities;

internal static class SafeFs
{
    #region Methods
    internal static IEnumerable<string> EnumerateFiles(string directoryPath, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    internal static IEnumerable<string> EnumerateDirectories(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(
                directoryPath,
                "*",
                SearchOption.TopDirectoryOnly
            );
        }
        catch
        {
            return [];
        }
    }

    internal static IEnumerable<string> EnumerateFilesRecursivePruned(
        string rootDirectory,
        string pattern,
        string[] blockedPathTokens,
        int maxDepth = int.MaxValue
    )
    {
        // Initialize the Traversal Stack With the Root
        var directoryStack = new Stack<(string dir, int depth)>();
        directoryStack.Push((rootDirectory, 0));

        while (directoryStack.Count > 0)
        {
            var (currentDirectory, depth) = directoryStack.Pop();

            // Skip Directories With Blocked Path Tokens
            if (TextMatchers.ContainsAnyToken(currentDirectory, blockedPathTokens))
            {
                continue;
            }

            IEnumerable<string> filePaths;
            try
            {
                filePaths = Directory.EnumerateFiles(
                    currentDirectory,
                    pattern,
                    SearchOption.TopDirectoryOnly
                );
            }
            catch
            {
                filePaths = [];
            }

            // Yield Files in the Current Directory
            foreach (var filePath in filePaths)
            {
                yield return filePath;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(
                    currentDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly
                );
            }
            catch
            {
                childDirectories = [];
            }

            if (depth >= maxDepth)
                continue;

            // Push Child Directories for Later Traversal
            foreach (var childDirectory in childDirectories)
            {
                directoryStack.Push((childDirectory, depth + 1));
            }
        }
    }
    #endregion
}
