using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DateExtractor.PatternSchema;

public sealed class PatternFile
{
    public string Culture { get; set; } = default!;
    public string? Delimiters { get; set; }      // may be null / omitted

    /// <summary>
    ///  Optional list of ordinal suffixes to strip during cleanup
    ///  (e.g. ["st","nd","rd","th"] for en-US, ["º","ª"] for es-ES).
    /// </summary>
    public List<string>? Ordinals { get; set; }
    
    public List<PatternEntry> Patterns { get; set; } = new();
}
