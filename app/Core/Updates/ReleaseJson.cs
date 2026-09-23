using System.Text.Json.Serialization;

namespace SpeakForever.Updates;

/// <summary>Source-generated JSON for GitHub's release response: no reflection at run time.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UpdateChecker.Release))]
sealed partial class ReleaseJson : JsonSerializerContext;
