namespace SpeakForever.Presentation;

/// <summary>Where a model in the list is up to; it decides which actions its row offers.</summary>
public enum ModelRowState
{
    /// <summary>In the catalog, not downloaded: offers Download.</summary>
    Available,

    /// <summary>Downloading: shows progress and Cancel.</summary>
    Downloading,

    /// <summary>On disk, not in use: offers Use and Delete.</summary>
    Installed,

    /// <summary>Being loaded.</summary>
    Loading,

    /// <summary>The model dictation uses.</summary>
    InUse,
}
