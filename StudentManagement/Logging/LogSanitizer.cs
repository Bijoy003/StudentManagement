using System.Text.RegularExpressions;

namespace StudentManagement.Logging
{
    public static class LogSanitizer
    {
        private static readonly Regex[] SensitivePatterns =
        [
            new(@"(?i)(password|pwd|pass|secret|token|apikey|api[_-]?key|auth[_-]?token|access[_-]?token|refresh[_-]?token|client[_-]?secret|private[_-]?key)\s*[:=]\s*[^\s,}\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"(?i)(connectionstring|connstr)\s*[:=]\s*[^\s,}\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"(?i)(authorization|bearer)\s+[^\s,}\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"(?i)(email|mail)\s*[:=]\s*[^\s,}\]]+@[^\s,}\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", RegexOptions.Compiled),
            new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
        ];

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = input;
            foreach (var pattern in SensitivePatterns)
            {
                result = pattern.Replace(result, match =>
                {
                    var prefix = match.Value.Split(':', '=', ' ')[0];
                    return $"{prefix}=***REDACTED***";
                });
            }
            return result;
        }

        public static object SanitizeObject(object obj)
        {
            if (obj == null)
                return null;

            if (obj is string str)
                return Sanitize(str);

            if (obj is IDictionary<string, object> dict)
            {
                var sanitized = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    sanitized[kvp.Key] = SanitizeObject(kvp.Value);
                }
                return sanitized;
            }

            if (obj is IEnumerable<object> enumerable)
            {
                return enumerable.Select(SanitizeObject).ToList();
            }

            return obj;
        }
    }
}