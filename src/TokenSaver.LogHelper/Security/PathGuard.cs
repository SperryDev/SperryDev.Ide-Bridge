namespace TokenSaver.LogHelper.Security;

/// <summary>
/// Gatekeeper for file-backed log sources. A path is accepted only when it canonicalizes to a location
/// under one of the allowed roots, and no directory or file on the way is a symlink or junction.
/// The host runs as LocalSystem in service mode, so this check is the only barrier.
/// </summary>
public static class PathGuard
{
    public static bool TryResolve(string? path, IReadOnlyList<string> allowedRoots, out string? canonical, out string? reason)
    {
        canonical = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Path is empty.";
            return false;
        }

        if (allowedRoots.Count == 0)
        {
            reason = "No log roots are configured.";
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "UNC, device and extended-length paths are not allowed.";
            return false;
        }

        if (HasStreamSuffix(path))
        {
            reason = "Alternate data streams are not allowed.";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "Path is invalid.";
            return false;
        }

        if (!TryFindRoot(full, allowedRoots, out string root))
        {
            reason = "Path is outside the allowed log roots.";
            return false;
        }

        if (HasLinkInPath(full, root))
        {
            reason = "Symbolic links and junctions are not allowed in log paths.";
            return false;
        }

        canonical = full;
        return true;
    }

    private static bool HasStreamSuffix(string path)
    {
        int start = path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':' ? 2 : 0;
        return path.IndexOf(':', start) >= 0;
    }

    private static bool TryFindRoot(string full, IReadOnlyList<string> roots, out string matchedRoot)
    {
        foreach (string configured in roots)
        {
            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                matchedRoot = rootFull;
                return true;
            }
        }

        matchedRoot = string.Empty;
        return false;
    }

    private static bool HasLinkInPath(string full, string root)
    {
        if (IsReparsePoint(full))
        {
            return true;
        }

        for (string? current = Path.GetDirectoryName(full);
             current is not null && current.Length >= root.Length;
             current = Path.GetDirectoryName(current))
        {
            if (IsReparsePoint(current))
            {
                return true;
            }

            if (current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
