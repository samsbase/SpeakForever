using SpeakForever.Speech;

namespace SpeakForever.Core.Tests;

public sealed class NameCorrectorTests
{
    static string Correct(string text) => NameCorrector.Shared.Correct(text).Text;

    // What Whisper wrote, from Windows text-to-speech reading WoW: Forever chat.
    [Theory]
    [InlineData("LFG Kroll Dock Stronghold, need a tank.", "LFG Krol'dok Stronghold, need a tank.")]
    [InlineData("Anyone want to run Hall of Thames or Ruins of Lord Aron?", "Anyone want to run Hall of Thanes or Ruins of Lordaeron?")]
    [InlineData("LFM Blackmore Hold, 2 DPS.", "LFM Blackmaw Hold, 2 DPS.")]
    [InlineData("Heading to Alcus Prison, then Maraudon and Zul'Farrak.", "Heading to Alcaz Prison, then Maraudon and Zul'Farrak.")]
    [InlineData("I am OOM after Scalomance.", "I am OOM after Scholomance.")]
    [InlineData("Meet at Zephyrus Isle near Chandra Lass.", "Meet at Zephras Isle near Shen'dralas.")]
    [InlineData("Shaman here with Maele Strom Weapon.", "Shaman here with Maelstrom Weapon.")]
    [InlineData("Nomearegan and Stratham runs, rez me please.", "Gnomeregan and Stratholme runs, rez me please.")]
    [InlineData("No Morrigan tonight?", "Gnomeregan tonight?")]
    public void MisheardNamesArePutBack(string heard, string expected) => Assert.Equal(expected, Correct(heard));

    [Theory]
    [InlineData("Can a druid innovate me?")] // a real word: Innervate sounds too close to it to use
    [InlineData("Can a druid inervate me?")]
    [InlineData("Anyone want to run Hall of Things?")] // all real words: left for the prompt to get right
    [InlineData("Thanks for the group, see you tomorrow.")]
    [InlineData("My brother Stephen is coming online later.")]
    [InlineData("Selling linen cloth and copper ore cheap.")]
    [InlineData("Let's meet at the mailbox next to the auction house.")]
    [InlineData("Pull the next pack when the healer has mana.")]
    [InlineData("thx for the invite, omw now")]
    [InlineData("np, ty for the heals kk")]
    [InlineData("lfg rfc need tank and heals pst")]
    [InlineData("wts mageweave cloth, pst with offers")]
    [InlineData("can someone summon me to the meeting stone plz")]
    [InlineData("whats the drop rate on that sword lmao")]
    [InlineData("u guys doing bfd or wc tonight")]
    [InlineData("my addon broke after the patch")]
    [InlineData("hey Legolaz want to group up")]
    [InlineData("Arthaz is our guild leader")]
    [InlineData("sry lagging hard rn")]
    public void EverythingElseIsLeftAlone(string said) => Assert.Equal(said, Correct(said));

    [Fact]
    public void ANameAlreadyRightIsntSwallowedIntoALongerMatch() =>
        Assert.Equal("then Maraudon and back", Correct("then Maraudon and back"));

    [Fact]
    public void ChangesAreReported() =>
        Assert.Equal([("Stratham", "Stratholme")], NameCorrector.Shared.Correct("Stratham runs").Changes);

    [Fact]
    public void NamesTooCloseToRealWordsAreLeftOut()
    {
        var names = NameCorrector.Shared.Names.ToHashSet();
        Assert.DoesNotContain("Innervate", names); // is a word, and sounds like innovate
        Assert.DoesNotContain("Everlook", names); // overlook
        Assert.DoesNotContain("Mograine", names); // migraine
        Assert.Contains("Stratholme", names);
        Assert.Contains("Krol'dok Stronghold", names);
        Assert.Contains("Maelstrom Weapon", names); // real words, but only used when Whisper splits them into ones that aren't
    }

    [Fact]
    public void TheSoundKeyMergesSpellingsOfTheSameSound()
    {
        Assert.Equal(NameCorrector.Sound("Scholomance"), NameCorrector.Sound("Scalomance"));
        Assert.Equal(NameCorrector.Sound("Shen'dralas"), NameCorrector.Sound("Chandra Lass"));
        Assert.Equal(NameCorrector.Sound("Gnomeregan"), NameCorrector.Sound("Nomearegan"));
    }

    [Fact]
    public void CorrectingRunsBeforeTheChatLengthLimit() =>
        Assert.Equal("Stratholme", Transcriber.Clean("Stratham", NameCorrector.Shared));
}
