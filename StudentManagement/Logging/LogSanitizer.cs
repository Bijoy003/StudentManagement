using System.Text.RegularExpressions;

namespace StudentManagement.Logging
{
    /// <summary>
    /// Best-effort redaction for log text. Every pattern keeps the surrounding key so the redacted
    /// event still reads sensibly ("Password=***") without leaking the value.
    /// </summary>
    public static class LogSanitizer
    {
        private const string Redacted = "***";

        private static readonly (Regex Pattern, string Replacement)[] SensitivePatterns =
        [
            // password=..., api_key: ..., client_secret=..., access_token=...
            (new(@"(?i)\b(password|pwd|pass|secret|token|apikey|api[_-]?key|auth[_-]?token|access[_-]?token|refresh[_-]?token|client[_-]?secret|private[_-]?key)\b\s*[:=]\s*[^\s,;}\]]+", RegexOptions.Compiled),
                $"$1={Redacted}"),

            // connectionstring=..., connstr: ...
            (new(@"(?i)\b(connectionstring|connstr)\b\s*[:=]\s*[^\s,}\]]+", RegexOptions.Compiled),
                $"$1={Redacted}"),

            // Authorization: Bearer <token>, hashed bearer token, etc.
            (new(@"(?i)\b(authorization|bearer)\b\s*[:=]?\s*(?:bearer\s+)?[^\s,;}\]]+", RegexOptions.Compiled),
                $"$1 {Redacted}"),

            // Any e-mail address, whether or not it carries a label.
            (new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled),
                $"***@{Redacted}"),

            // Card numbers only when written in groups, so 16-digit student identifiers survive.
            (new(@"\b\d{4}[ -]\d{4}[ -]\d{4}[ -]\d{4}\b", RegexOptions.Compiled),
                "****-****-****-****"),

            (new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
                "***-**-****"),
        ];

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var result = input;
            foreach (var (pattern, replacement) in SensitivePatterns)
            {
                result = pattern.Replace(result, replacement);
            }

            return result;
        }

        public static object? SanitizeObject(object? obj)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj is string str)
            {
                return Sanitize(str);
            }

            if (obj is IDictionary<string, object?> dictionary)
            {
                var sanitized = new Dictionary<string, object?>();
                foreach (var (key, value) in dictionary)
                {
                    sanitized[key] = SanitizeObject(value);
                }

                return sanitized;
            }

            if (obj is IEnumerable<object?> enumerable)
            {
                return enumerable.Select(SanitizeObject).ToList();
            }

            return obj;
        }
    }
}
