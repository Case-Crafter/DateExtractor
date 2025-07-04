// DateExtractor/DateExtractor.cs
//
// Usage
// -----
// • via built-in cultures (precedence = list order):
//     var ex = new DateExtractor(new List<CultureInfo>
//     {
//         new CultureInfo("en-US"),
//         new CultureInfo("nb-NO")
//     });
//
// • via custom JSON definitions:
//     var ex = new DateExtractor(new List<string> { frJson, arJson });
//
// var dates = ex.Extract(pdfText);

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DateExtractor.JsonGen;

namespace DateExtractor
{
    internal sealed record PatternEntry(Regex Regex, string[] Formats);
    internal sealed record CulturePattern(
        CultureInfo Culture, 
        string Delimiters, 
        List<PatternEntry> Patterns,
        List<string>? Ordinals);

    /// <summary>
    ///  Extracts <see cref="DateTime"/> instances from arbitrary text.
    ///  Pattern precedence equals the order of cultures/JSON supplied
    ///  to the constructor.  Duplicate dates are preserved.
    /// </summary>
    public sealed class DateExtractor
    {
        private readonly List<CulturePattern> _patternSets; // caller order
        private readonly TextCleaner _cleaner;

        /// <summary>
        ///  Build an extractor using the library’s embedded pattern files
        ///  (<c>Patterns/&lt;culture>.json</c>).  Order in the list defines
        ///  matching precedence.
        /// </summary>
        /// <param name="cultures">e.g. <c>[ "en-US", "nb-NO" ]</c></param>
        /// <exception cref="ArgumentException">List null/empty.</exception>
        /// <exception cref="NotSupportedException">
        ///   A requested culture has no embedded pattern file.
        /// </exception>
        public DateExtractor(List<CultureInfo> cultures)
        {
            if (cultures is null || cultures.Count == 0)
                throw new ArgumentException("Culture list must not be empty.", nameof(cultures));

            _patternSets = cultures.Select(LoadEmbeddedPattern).ToList();
            _cleaner = BuildCleaner(_patternSets);
        }

        /// <summary>
        ///  Build an extractor from caller-supplied JSON pattern strings.
        ///  List order = precedence order.  Bypasses embedded resources.
        /// </summary>
        /// <param name="jsonPatterns">At least one culture JSON document.</param>
        /// <exception cref="ArgumentException">List null/empty.</exception>
        public DateExtractor(List<string> jsonPatterns)
        {
            if (jsonPatterns is null || jsonPatterns.Count == 0)
                throw new ArgumentException("JSON list must not be empty.", nameof(jsonPatterns));

            _patternSets = jsonPatterns.Select(ParseJsonPattern).ToList();
            _cleaner = BuildCleaner(_patternSets);
        }

        /// <summary>
        ///  Cleans <paramref name="input"/> and returns every recognised date.
        ///  Duplicate dates remain duplicated in the result.
        /// </summary>
        public List<DateTime> Extract(string input)
        {
            // inside DateExtractor.Extract(string input)

            var cleaned = _cleaner.Clean(input ?? string.Empty);
            var dates = new List<DateTime>();
            var usedSpans = new List<(int start, int length)>();

            foreach (var cps in _patternSets)              // culture precedence
            {
                foreach (var pe in cps.Patterns)
                {
                    foreach (Match m in pe.Regex.Matches(cleaned))
                    {
                        // ► skip if any earlier match consumed this slice
                        bool overlaps = usedSpans.Any(s =>
                            m.Index < s.start + s.length && s.start < m.Index + m.Length);

                        if (overlaps) continue;

                        //var candidate = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(m.Value);

                        if (DateTime.TryParseExact(m.Value, pe.Formats, cps.Culture, DateTimeStyles.None, out var dt))
                        {
                            dates.Add(dt);
                            usedSpans.Add((m.Index, m.Length));
                        }
                        else
                        {

                        }
                    }
                }
            }
            return dates;

        }

        // ─────────────────── helpers ─────────────────── //

        private static TextCleaner BuildCleaner(IEnumerable<CulturePattern> cps)
        {
            var allDelims = string.Concat(cps.Select(cp => cp.Delimiters))
                                  .Distinct()
                                  .ToArray();

            var allOrdinals = cps
                                       .SelectMany(cp => cp.Ordinals ?? Enumerable.Empty<string>())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToArray();
            
                     return new TextCleaner(allDelims.Length == 0 ? null : new string(allDelims),
                allOrdinals);

            /*
            return allDelims.Length == 0
                   ? new TextCleaner(null)               // raw-delimiter mode
                   : new TextCleaner(new string(allDelims));
            */
        }

        private static CulturePattern LoadEmbeddedPattern(CultureInfo culture)
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = $"DateExtractor.Patterns.{culture.Name}.json";

            using var stream = asm.GetManifestResourceStream(res)
                         ?? throw new NotSupportedException(
                                $"Culture '{culture.Name}' is not supported. " +
                                $"Missing embedded resource '{res}'.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            return ParseJsonPattern(json);
        }

        private static CulturePattern ParseJsonPattern(string json)
        {
            var pf = JsonSerializer.Deserialize(
                json,
                PatternFileContext.Default.PatternFile);

            //var culture = new CultureInfo(pf.Culture);
            var culture = NormalizeAbbrevDots(new CultureInfo(pf.Culture));
            var delims = pf.Delimiters ?? string.Empty;

            var compiled = new List<PatternEntry>();

            foreach (var p in pf.Patterns)
            {
                // format can be string or array → normalise to string[]
                string[] fmts = p.Format.ValueKind switch
                {
                    JsonValueKind.String => new[] { p.Format.GetString()! },
                    JsonValueKind.Array => p.Format.EnumerateArray()
                        .Select(e => e.GetString()!)
                        .ToArray(),
                    _ => throw new JsonException(
                        $"Invalid 'format' for pattern in {pf.Culture}.")
                };

                compiled.Add(new PatternEntry(
                    new Regex(p.Regex,
                        RegexOptions.Compiled | RegexOptions.IgnoreCase),
                    fmts));
            }

            compiled = compiled
                .OrderByDescending(pe => pe.Regex.ToString().Contains(@"\d{4}"))
                .ToList();

            return new CulturePattern(culture, delims, compiled, pf.Ordinals);
        }

        private static CultureInfo NormalizeAbbrevDots(CultureInfo culture)
        {
            var clone = (CultureInfo)culture.Clone();
            var fmt = clone.DateTimeFormat;

            string StripDot(string s) => s?.TrimEnd('.') ?? "";

            fmt.AbbreviatedMonthNames = fmt.AbbreviatedMonthNames
                .Select(StripDot).ToArray();
            fmt.AbbreviatedMonthGenitiveNames = fmt.AbbreviatedMonthGenitiveNames
                .Select(StripDot).ToArray();
            fmt.MonthNames[12] =
                fmt.AbbreviatedMonthNames[12] = "";  // keep 13th element empty
            fmt.MonthGenitiveNames[12] =
                fmt.AbbreviatedMonthGenitiveNames[12] = "";

            clone.DateTimeFormat = fmt;
            return clone;
        }
    }
}
