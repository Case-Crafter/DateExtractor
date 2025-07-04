using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DateExtractor.PatternSchema;
public sealed class PatternEntry
{
    public string Regex { get; set; } = default!;

    // “format” can be either string or string[]
    // → deserialize to JsonElement, we’ll normalise later.
    public JsonElement Format { get; set; }
}