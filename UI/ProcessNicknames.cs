using System.Collections.Generic;

namespace UI
{
    internal static class ProcessNicknames
    {
        private static readonly Dictionary<string, string> Nicknames = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["ShellExperienceHost"] = "Windows Shell"
        };

        public static string GetDisplayName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return processName ?? string.Empty;

            return Nicknames.TryGetValue(processName, out string nickname) ? nickname : processName;
        }
    }
}
