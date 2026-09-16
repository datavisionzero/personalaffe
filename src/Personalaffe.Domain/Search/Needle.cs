using System.Globalization;
using System.Text;

namespace Personalaffe.Domain.Search;

/// <summary>
/// What somebody is looking for: the words they typed, broken into the pieces
/// an index can be asked about.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every word is a prefix.</strong> A person searching their own
/// workspace is remembering, not querying: they type <c>arch dec</c> and mean
/// the page called "Architecture decisions". So the words are matched as
/// beginnings, all of them, and a needle with two words finds only what carries
/// both — narrowing as more is typed, which is what a field that answers while
/// somebody is still typing has to do.
/// </para>
/// <para>
/// <strong>The splitting happens here and not in a query.</strong> What comes
/// back from <see cref="Words"/> is letters and digits and nothing else, which
/// is what lets the store build a query out of them without quoting anything:
/// there is no punctuation left to mean something. A needle is the one place in
/// this product where a caller's text reaches a query language, and it reaches
/// it already taken apart.
/// </para>
/// <para>
/// There is no operator vocabulary — no <c>AND</c>, no quoted phrase, no
/// <c>-word</c>. VISION.md asks for one search over four applications, and a
/// query language is one more thing to learn about a workspace one person is
/// looking through. What a stray <c>"</c> or <c>-</c> does here is nothing at
/// all.
/// </para>
/// </remarks>
public sealed record Needle
{
    /// <summary>
    /// How much text is accepted, in characters. Longer than a title, because
    /// pasting a sentence somebody half remembers is a real way to look for
    /// something, and far enough past that to be a mistake.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// How many words are looked for. The rest are dropped rather than refused:
    /// a pasted paragraph is still somebody looking for something, and every
    /// word past the eighth narrows one person's workspace to nothing anyway.
    /// </summary>
    public const int MaxWords = 8;

    /// <summary>
    /// The shortest word worth looking for. One letter is a prefix of most of a
    /// workspace, so it is dropped: <c>a page</c> looks for <c>page</c>, and
    /// a needle that is only single letters is nothing to look for at all.
    /// </summary>
    public const int MinWordLength = 2;

    private Needle(string text, IReadOnlyList<string> words)
    {
        Text = text;
        Words = words;
    }

    /// <summary>What was typed, trimmed. It is echoed back, and nothing else.</summary>
    public string Text { get; }

    /// <summary>
    /// The words to look for, lowercased, letters and digits only. Never empty:
    /// a needle that would have had none is a refusal instead.
    /// </summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>The needle somebody typed, or a refusal.</summary>
    /// <exception cref="Refusal">
    /// <c>validation</c>: nothing to look for, or more text than a search is.
    /// </exception>
    public static Needle Of(string? typed, string field = "q")
    {
        var text = (typed ?? string.Empty).Trim();

        if (text.Length > MaxLength)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A search is at most {MaxLength} characters, and this one is {text.Length}."));
        }

        var words = Split(text);

        if (words.Count == 0)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Say what to look for: at least {MinWordLength} letters or digits."));
        }

        return new Needle(text, words);
    }

    private static IReadOnlyList<string> Split(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var character in text)
        {
            // Letters and digits of every script, which is what keeps a needle
            // in German, Greek or Japanese a needle. Everything else is a
            // boundary, punctuation and whitespace alike: `budget-2026.pdf`
            // typed into the field is three words, and so is the file name it
            // is looking for.
            if (char.IsLetterOrDigit(character))
            {
                word.Append(character);
                continue;
            }

            Keep(words, word);

            if (words.Count == MaxWords)
            {
                return words;
            }
        }

        Keep(words, word);

        return words.Count > MaxWords ? words.GetRange(0, MaxWords) : words;
    }

    private static void Keep(List<string> words, StringBuilder word)
    {
        if (word.Length >= MinWordLength)
        {
            // Lowercased the way the index is, which is invariant and not the
            // server's locale: the same word has to reach the same token on
            // every machine this instance is ever restored onto.
            words.Add(word.ToString().ToLowerInvariant());
        }

        word.Clear();
    }
}
