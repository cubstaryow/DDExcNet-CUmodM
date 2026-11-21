using System;
using System.Globalization;
using System.Text;

namespace DDEN.MPfix
{
    internal static class NameDisplayRules
    {
        internal static string Clean(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var result = new StringBuilder();
            int scalars = 0;
            for (int i = 0; i < input.Length && scalars < 32; i++)
            {
                char c = input[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                    { result.Append(c).Append(input[++i]); scalars++; }
                    continue;
                }
                if (char.IsLowSurrogate(c) || char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format
                    || c == '<' || c == '>' || c == '\u2028' || c == '\u2029') continue;
                result.Append(c); scalars++;
            }
            return result.ToString().Trim();
        }
        internal static string Choose(string original, string profile, ulong steamId)
        {
            string source = original ?? "";
            string cleaned = Clean(source), persona = Clean(profile);
            bool validPersona = persona.Length > 0 && persona != steamId.ToString()
                && !persona.Equals("[unknown]", StringComparison.OrdinalIgnoreCase) && persona != "[UNKNOWN]";
            bool broken = cleaned.Length == 0 || cleaned == steamId.ToString() || cleaned == "EMPTY_STRING"
                || cleaned == "ERROR-NO-NAME" || cleaned.Trim('?', ' ', '\ufffd').Length == 0;
            if (validPersona && (broken || source == AsciiOnly(profile) || source == LegacyBytes(profile))) return persona;
            return cleaned.Length == 0 ? "玩家" : cleaned;
        }
        private static string AsciiOnly(string text)
        {
            var b = new StringBuilder();
            foreach (char c in text ?? "")
                if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "_#$()+=-".IndexOf(c) >= 0) b.Append(c);
            return b.ToString();
        }
        private static string LegacyBytes(string text)
        {
            var b = new StringBuilder();
            foreach (char c in text ?? "") b.Append((char)(byte)c);
            return b.ToString();
        }
    }
}
