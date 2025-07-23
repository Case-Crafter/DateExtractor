// PdfDateExtractor/TextCleaner.cs
//
// Internal helper; exposed to tests via InternalsVisibleTo.

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DateExtractor.Tests")]

namespace DateExtractor
{
    /// <summary>
    ///  Normalises OCR / PDF-extracted text so that downstream regexes
    ///  can rely on spaces as delimiters *if* requested.
    /// </summary>
    internal sealed class TextCleaner
    {
        private static readonly Regex TabsAndNewlines = new(@"[\t\r\n]+");
        private static readonly Regex MultiSpaces = new(@"\s{2,}");
        private readonly Regex? _ordinalRegex;

        private readonly Regex? _delimRegex;   // null → no delimiter replacement

        /// <param name="delimiters">
        ///  Characters to replace with a space (e.g. "/.-,").
        ///  If <c>null</c>, empty, or whitespace-only, *no* delimiter
        ///  replacement is performed, allowing callers to supply regexes
        ///  that reference the raw characters.
        /// </param>
        public TextCleaner(string? delimiters = null, string[]? ordinals = null)
        {
            if (string.IsNullOrWhiteSpace(delimiters))
                return;                         // raw-delimiter mode

            var seen = new HashSet<char>();
            var hasHyphen = false;
            var patternSb = new StringBuilder("[");

            foreach (var ch in delimiters)
            {
                if (ch == '-')
                {
                    hasHyphen = true;           // postpone until the end
                    continue;
                }

                if (seen.Add(ch))
                    patternSb.Append(Regex.Escape(ch.ToString()));
            }

            if (hasHyphen)
                patternSb.Append('-');          // hyphen as *last* char = literal

            patternSb.Append(']');
            _delimRegex = new Regex(patternSb.ToString());

            if (ordinals is { Length: > 0 })
            {
                var alternation = string.Join('|', ordinals.Select(Regex.Escape));
                _ordinalRegex = new Regex($@"(?<=\d)\s*({alternation})\b",
                    RegexOptions.IgnoreCase);
            }
        }

        /// <summary>Return a cleaned copy of <paramref name="raw"/>.</summary>
        public string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // 1  lower-case
            var text = raw.ToLowerInvariant();

            // 2  delimiter → single space
            if (_delimRegex is not null)
                text = _delimRegex.Replace(text, " ");

            // 3 ordinals → single space
            if (_ordinalRegex is not null)
                text = _ordinalRegex.Replace(text, " ");

            // 4  tabs / CR / LF → space
            text = TabsAndNewlines.Replace(text, " ");

            // 5  collapse runs of spaces
            text = MultiSpaces.Replace(text, " ");

            // 6  strip control characters
            text = new string(text.Where(c => !char.IsControl(c)).ToArray());

            return text.Trim();
        }
    }
}
