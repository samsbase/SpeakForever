using System.Text;
using System.Text.RegularExpressions;

namespace SpeakForever.Speech;

/// <summary>
/// Puts WoW: Forever names back where Whisper heard something that sounds like one but isn't a
/// real word: "Stratham" becomes Stratholme, "Chandra Lass" becomes Shen'dralas. Two rules keep it
/// from rewriting what you actually said:
/// <list type="bullet">
/// <item>Only stretches of speech with at least one word that isn't English (nor chat slang) are
/// changed. Whisper spells real words itself, so "innovate" stays "innovate".</item>
/// <item>A one-word name that is itself a real word, or sounds the same as one, is never used:
/// Innervate (innovate), Everlook (overlook), Mograine (migraine).</item>
/// </list>
/// </summary>
public sealed partial class NameCorrector
{
    const int MaxSpanWords = 4;
    const double MinLetterSimilarity = 0.6; // every genuine correction measured scored 0.6 or more
    const double MinTwinLetterSimilarity = 0.7; // a name this close to a same-sounding word is left out

    // Chat words that aren't in the dictionary but must never be taken for a name.
    static readonly string[] ChatWords =
        ["addon", "addons", "afk", "aggro", "brb", "dps", "lfg", "lfm", "wts", "wtb", "pst", "omw", "thx", "ty", "np", "kk",
         "plz", "pls", "lmao", "lol", "rn", "sry", "ding", "grats", "rez", "res", "inv", "ppl", "gg", "ffs", "imo", "tbh",
         "idk", "nvm", "irl", "ofc", "btw"];

    static readonly Lazy<NameCorrector> SharedLazy = new(() => new NameCorrector(Resource("wow-names.txt"), EnglishWords()));

    /// <summary>Built from the names and dictionary shipped in the app. The first use takes a fraction of a second.</summary>
    public static NameCorrector Shared => SharedLazy.Value;

    readonly HashSet<string> words;
    readonly Name[] names;

    sealed record Name(string Text, string Sound, string Letters, int Words);

    /// <param name="names">Game names, one per entry; blank entries and #comments are skipped.</param>
    /// <param name="englishWords">Real words, lowercase. Capitalised entries (proper nouns) are ignored.</param>
    internal NameCorrector(IEnumerable<string> names, IEnumerable<string> englishWords)
    {
        words = [.. englishWords.Where(w => w.Length > 0 && !w.Any(char.IsUpper)), .. ChatWords];
        var bySound = new Dictionary<string, List<string>>();
        foreach (var w in words)
        {
            var key = Sound(w);
            if (!bySound.TryGetValue(key, out var list)) bySound[key] = list = [];
            list.Add(w);
        }

        var kept = new List<Name>();
        foreach (var raw in names)
        {
            var text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            int parts = WordPart().Count(text);
            string sound = Sound(text), letters = Letters(text);
            if (parts == 1)
            {
                if (IsWord(text)) continue;
                if (bySound.TryGetValue(sound, out var twins) && twins.Any(w => Similarity(w, letters) >= MinTwinLetterSimilarity)) continue;
            }
            if (sound.Length > 0) kept.Add(new Name(text, sound, letters, parts));
        }
        this.names = [.. kept];
    }

    /// <summary>The names it will correct to, after leaving out the ones too close to real words.</summary>
    public IEnumerable<string> Names => names.Select(n => n.Text);

    /// <summary>The text with names put back, and what was changed.</summary>
    public (string Text, IReadOnlyList<(string Heard, string Name)> Changes) Correct(string text)
    {
        var tokens = Token().Matches(text);
        var candidates = new List<(double Score, int Start, int End, string Name, string Heard)>();
        for (int i = 0; i < tokens.Count; i++)
        {
            for (int j = i; j < Math.Min(i + MaxSpanWords, tokens.Count); j++)
            {
                // A span is words separated only by spaces, with at least one that isn't a real word.
                if (j > i && !string.IsNullOrWhiteSpace(text[(tokens[j - 1].Index + tokens[j - 1].Length)..tokens[j].Index])) break;
                bool anyNonWord = false;
                for (int t = i; t <= j && !anyNonWord; t++) anyNonWord = !IsWord(tokens[t].Value);
                if (!anyNonWord) continue;

                int start = tokens[i].Index, end = tokens[j].Index + tokens[j].Length;
                var heard = text[start..end];
                string sound = Sound(heard), letters = Letters(heard);
                if (sound.Length == 0) continue;
                int spanWords = j - i + 1;
                foreach (var name in names)
                {
                    if (Math.Abs(name.Words - spanWords) > 1 || sound[0] != name.Sound[0] || Math.Abs(sound.Length - name.Sound.Length) > 2) continue;
                    if (letters == name.Letters)
                    {
                        // Already this name, give or take case and apostrophes: it claims these words first.
                        candidates.Add((-1, start, end, name.Text, heard));
                        continue;
                    }
                    int distance = Levenshtein(sound, name.Sound);
                    if (distance > AllowedDistance(name.Sound.Length)) continue;
                    double similarity = Similarity(letters, name.Letters);
                    if (similarity < MinLetterSimilarity) continue;
                    candidates.Add(((double)distance / name.Sound.Length - similarity * 0.01, start, end, name.Text, heard));
                }
            }
        }

        var taken = new List<(int Start, int End)>();
        var changes = new List<(int Start, int End, string Name, string Heard)>();
        foreach (var c in candidates.OrderBy(c => c.Score).ThenBy(c => c.Start).ThenBy(c => c.End))
        {
            if (taken.Any(t => c.Start < t.End && t.Start < c.End)) continue;
            taken.Add((c.Start, c.End));
            if (c.Heard != c.Name) changes.Add((c.Start, c.End, c.Name, c.Heard));
        }
        var result = new StringBuilder(text);
        foreach (var c in changes.OrderByDescending(c => c.Start))
            result.Remove(c.Start, c.End - c.Start).Insert(c.Start, c.Name);
        return (result.ToString(), [.. changes.OrderBy(c => c.Start).Select(c => (c.Heard, c.Name))]);
    }

    bool IsWord(string word) =>
        words.Contains(Letters(word)) || words.Contains(word.ToLowerInvariant().Trim('\''));

    // Short names must sound exactly right; longer ones may be a sound or two off.
    static int AllowedDistance(int length) => length <= 3 ? 0 : length <= 7 ? 1 : 2;

    static double Similarity(string a, string b) => 1 - (double)Levenshtein(a, b) / Math.Max(Math.Max(a.Length, b.Length), 1);

    /// <summary>Lowercase letters only: "Krol'dok Stronghold" is "kroldokstronghold".</summary>
    internal static string Letters(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            char lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z') sb.Append(lower);
        }
        return sb.ToString();
    }

    /// <summary>
    /// A rough key for how it sounds: spellings of the same sound are merged ("ph" is "f", "sch" is
    /// "sk", soft "c" is "s"), vowels after the first letter are dropped, and doubled letters collapse.
    /// "Stratholme" and "Stratham" differ only by the "l"; "Scholomance" and "Scalomance" are the same.
    /// </summary>
    internal static string Sound(string text)
    {
        var s = Letters(text);
        foreach (var (from, to) in (ReadOnlySpan<(string, string)>)[("kn", "n"), ("gn", "n"), ("wr", "r"), ("ps", "s"), ("x", "s")])
        {
            if (s.StartsWith(from, StringComparison.Ordinal))
            {
                s = to + s[from.Length..];
                break;
            }
        }
        foreach (var (from, to) in (ReadOnlySpan<(string, string)>)
                 [("sch", "sk"), ("tch", "x"), ("ch", "x"), ("sh", "x"), ("ph", "f"), ("th", "0"), ("ck", "k"), ("qu", "kw"),
                  ("q", "k"), ("wh", "w"), ("dg", "j"), ("gh", ""), ("x", "ks")])
            s = s.Replace(from, to, StringComparison.Ordinal);

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            sb.Append(ch switch
            {
                'c' => i + 1 < s.Length && s[i + 1] is 'e' or 'i' or 'y' ? 's' : 'k',
                'z' => 's',
                'v' => 'f',
                _ => ch,
            });
        }
        s = sb.Replace("ks", "x").ToString();
        if (s.Length == 0) return s;

        sb.Clear().Append(s[0] is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' ? 'a' : s[0]);
        foreach (char ch in s.AsSpan(1))
        {
            if (ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'h' or 'w') continue;
            if (sb[^1] != ch) sb.Append(ch);
        }
        return sb.ToString();
    }

    static int Levenshtein(string a, string b)
    {
        Span<int> previous = stackalloc int[b.Length + 1];
        Span<int> current = stackalloc int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            current.CopyTo(previous);
        }
        return previous[b.Length];
    }

    static List<string> Resource(string name)
    {
        using var stream = typeof(NameCorrector).Assembly.GetManifestResourceStream($"SpeakForever.Speech.Names.{name}")
                           ?? throw new InvalidOperationException($"The {name} resource is missing from the build.");
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    // The word list starts with its licence, which ends at a "---" line.
    static IEnumerable<string> EnglishWords() => Resource("english-words.txt").SkipWhile(l => l != "---").Skip(1);

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*")]
    private static partial Regex Token();

    [GeneratedRegex(@"[A-Za-z']+")]
    private static partial Regex WordPart();
}
