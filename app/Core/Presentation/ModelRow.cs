using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpeakForever.Speech;

namespace SpeakForever.Presentation;

/// <summary>
/// One row of the app's model list, for data binding. It holds no UI types, so the rules for
/// what each row shows are testable without WinUI.
/// </summary>
public sealed class ModelRow(string path, string name, string summary, string? blurb, ModelInfo? catalogEntry) : INotifyPropertyChanged
{
    ModelRowState state;
    double progress;
    bool canUse = true, hasDivider;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Summary { get; } = summary;
    public string? Blurb { get; } = blurb;

    /// <summary>The catalog entry, or null for a model the user put in the folder themselves.</summary>
    public ModelInfo? CatalogEntry { get; } = catalogEntry;

    public bool IsRecommended => CatalogEntry?.Recommended == true;
    public bool HasBlurb => Blurb is not null;
    public string DeleteLabel => $"Delete {Name}";

    public ModelRowState State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            Raise(nameof(ShowDownload));
            Raise(nameof(ShowProgress));
            Raise(nameof(ShowUse));
            Raise(nameof(ShowStatus));
            Raise(nameof(StateLabel));
        }
    }

    /// <summary>Download progress, 0 to 1.</summary>
    public double Progress
    {
        get => progress;
        set
        {
            if (Set(ref progress, value)) Raise(nameof(ProgressText));
        }
    }

    /// <summary>False while another model loads or the microphone test runs.</summary>
    public bool CanUse
    {
        get => canUse;
        set => Set(ref canUse, value);
    }

    /// <summary>Every row but the first has a divider above it.</summary>
    public bool HasDivider
    {
        get => hasDivider;
        set => Set(ref hasDivider, value);
    }

    public bool ShowDownload => State == ModelRowState.Available;
    public bool ShowProgress => State == ModelRowState.Downloading;
    public bool ShowUse => State == ModelRowState.Installed;
    public bool ShowStatus => State is ModelRowState.Loading or ModelRowState.InUse;
    public string StateLabel => State == ModelRowState.Loading ? "Loading…" : "In use";
    public string ProgressText => $"{Progress:P0}";

    /// <summary>What a model's row should show, from what's on disk and what the engine is doing.</summary>
    public static ModelRowState StateOf(string path, bool installed, string? loaded, string? loading, bool downloading) =>
        downloading ? ModelRowState.Downloading
        : SamePath(path, loading) ? ModelRowState.Loading
        : SamePath(path, loaded) ? ModelRowState.InUse
        : installed ? ModelRowState.Installed
        : ModelRowState.Available;

    static bool SamePath(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(property);
        return true;
    }

    void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
