using System.ComponentModel;
using VoiceForever.Configuration;
using VoiceForever.Presentation;
using VoiceForever.Speech;

namespace VoiceForever.Core.Tests;

/// <summary>The model list's rows, the models folder, and cleaning up transcripts.</summary>
public sealed class ModelTests
{
    const string Turbo = @"C:\models\ggml-large-v3-turbo-q5_0.bin", Base = @"C:\models\ggml-base.bin";

    [Theory]
    [InlineData(false, null, null, false, ModelRowState.Available)]
    [InlineData(false, null, null, true, ModelRowState.Downloading)]
    [InlineData(true, null, null, false, ModelRowState.Installed)]
    [InlineData(true, Turbo, null, false, ModelRowState.InUse)]
    [InlineData(true, Base, Turbo, false, ModelRowState.Loading)]
    [InlineData(true, Turbo, Base, false, ModelRowState.InUse)] // still in use while another loads
    public void ARowShowsWhatTheModelIsDoing(bool installed, string? loaded, string? loading, bool downloading, ModelRowState expected) =>
        Assert.Equal(expected, ModelRow.StateOf(Turbo, installed, loaded, loading, downloading));

    [Fact]
    public void EachStateOffersItsOwnActions()
    {
        var row = new ModelRow(Turbo, "Turbo", "574 MB", null, null) { State = ModelRowState.Installed };
        Assert.Equal((false, false, true, false), (row.ShowDownload, row.ShowProgress, row.ShowUse, row.ShowStatus));
        row.State = ModelRowState.Loading;
        Assert.Equal((false, true, "Loading..."), (row.ShowUse, row.ShowStatus, row.StateLabel));
    }

    [Fact]
    public void ChangingStateNotifiesEveryDependentProperty()
    {
        var row = new ModelRow(Turbo, "Turbo", "574 MB", null, null);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        row.State = ModelRowState.Downloading;
        row.Progress = 0.5;
        Assert.Subset(changed.ToHashSet(), new HashSet<string?> { nameof(ModelRow.ShowDownload), nameof(ModelRow.ShowProgress), nameof(ModelRow.ProgressText) });
        Assert.Equal("50%", row.ProgressText.Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingTheSameValueRaisesNothing()
    {
        var row = new ModelRow(Turbo, "Turbo", "574 MB", null, null);
        int raised = 0;
        ((INotifyPropertyChanged)row).PropertyChanged += (_, _) => raised++;
        row.State = ModelRowState.Available;
        Assert.Equal(0, raised);
    }

    [Fact]
    public void InstalledListsModelsSmallestFirstAndDisplayNamesUseTheCatalog()
    {
        Directory.CreateDirectory(AppPaths.Models);
        var small = Path.Combine(AppPaths.Models, "ggml-base.bin");
        var big = Path.Combine(AppPaths.Models, "ggml-mine.bin");
        File.WriteAllBytes(small, new byte[10]);
        File.WriteAllBytes(big, new byte[20]);
        try
        {
            Assert.Equal([small, big], ModelCatalog.Installed());
            Assert.Equal("Base", ModelCatalog.DisplayName(small));
            Assert.Equal("mine (0.0 GB)", ModelCatalog.DisplayName(big));
        }
        finally
        {
            File.Delete(small);
            File.Delete(big);
        }
    }

    [Theory]
    [InlineData("Hello [BLANK_AUDIO] there (music) *laughs*", "Hello there")]
    [InlineData("a | b", "a / b")] // "|" starts an escape sequence in WoW chat
    public void TranscriptsAreCleanedForChat(string raw, string expected) => Assert.Equal(expected, Transcriber.Clean(raw));

    [Fact]
    public void LongTranscriptsAreCutAtAWordWithinTheChatLimit()
    {
        var text = Transcriber.Clean(string.Join(' ', Enumerable.Repeat("word", 100)));
        Assert.InRange(text.Length, 250, 255);
        Assert.EndsWith("word", text, StringComparison.Ordinal);
    }
}
