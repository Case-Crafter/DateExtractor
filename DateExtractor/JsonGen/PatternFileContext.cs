using System.Text.Json;
using System.Text.Json.Serialization;
using DateExtractor.PatternSchema;

namespace DateExtractor.JsonGen;

/// <summary>
///  Compile-time metadata for <see cref="PatternFile"/> deserialization,
///  used by AOT builds to avoid reflection.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PatternFile))]
internal partial class PatternFileContext : JsonSerializerContext
{ }