namespace Editors.Ipc
{
    public static class ExternalPackStartupArgumentParser
    {
        public static string? FindPackPath(
            IEnumerable<string> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            foreach (var argument in arguments)
            {
                var path = ExternalPackPath.Normalize(argument);
                if (ExternalPackPath.IsPackPath(path))
                {
                    return path;
                }
            }

            return null;
        }
    }

    public static class ExternalPackPath
    {
        public static bool IsPackPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                return string.Equals(
                    Path.GetExtension(path),
                    ".pack",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var path = input.Trim();
            if (path.Length >= 2 &&
                ((path[0] == '"' && path[^1] == '"') ||
                 (path[0] == '\'' && path[^1] == '\'')))
            {
                path = path[1..^1];
            }

            path = path.Replace('/', '\\');
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }
    }
}
