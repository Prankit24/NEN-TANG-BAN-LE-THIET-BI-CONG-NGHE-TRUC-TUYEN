using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace EMua.Services.AI
{
    /// <summary>
    /// So khớp từ khóa theo RANH GIỚI TỪ. Dùng thay cho string.Contains với các từ ngắn như "ai", "it", "dev", "lol":
    /// Contains("ai") sẽ khớp nhầm trong "tại", "hai", "mai"... làm chatbot hiểu sai ý định.
    /// </summary>
    public static class TextMatch
    {
        private static readonly ConcurrentDictionary<string, Regex> Cache = new();

        public static bool HasWord(string? text, string word)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(word)) return false;
            var regex = Cache.GetOrAdd(word, w => new Regex(
                @"(?<![\p{L}\p{N}])" + Regex.Escape(w) + @"(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
            return regex.IsMatch(text);
        }

        public static bool HasAnyWord(string? text, params string[] words)
        {
            foreach (var w in words) if (HasWord(text, w)) return true;
            return false;
        }
    }
}
