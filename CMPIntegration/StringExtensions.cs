using System.Text.RegularExpressions;

namespace alloy_events_test.CMPIntegration
{
    public static class StringExtensions
    {
        public static string RegexReplace(this string input, string pattern, string replacement)
        {
            return Regex.Replace(input, pattern, replacement);
        }
    }
}
