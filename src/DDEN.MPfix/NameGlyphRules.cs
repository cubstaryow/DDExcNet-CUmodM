using System;
using System.Text;

namespace DDEN.MPfix
{
    internal static class NameGlyphRules
    {
        internal static string ReplaceMissing(string text, Func<uint, bool> available)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                uint code = length == 2 ? (uint)char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                if (!char.IsSurrogate(text[i]) || length == 2)
                {
                    if (char.IsWhiteSpace(text[i]) || available(code)) result.Append(text, i, length);
                    else result.Append('?');
                }
                else result.Append('?');
                i += length - 1;
            }
            string rendered = result.ToString();
            return rendered == text ? text : rendered;
        }
    }
}
