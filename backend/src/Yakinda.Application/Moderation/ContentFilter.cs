using System.Globalization;
using System.Reflection;
using System.Text;
using FluentValidation;

namespace Yakinda.Application.Moderation;

/// <summary>
/// Kullanıcıların yazdığı metinlerde (etkinlik başlığı/açıklaması, görünen ad, yorum, mesaj)
/// uygunsuz ifade arar. App Store kuralları (1.2) kullanıcı içeriği olan uygulamalarda bir filtre ister;
/// şikayet ve engelleme ile birlikte çalışır. Liste: Moderation/blocked-words.txt
/// </summary>
public static class ContentFilter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Lazy<(HashSet<string> Exact, string[] Prefixes)> Words = new(Load);

    /// <summary>
    /// Metin uygunsuz bir ifade içeriyor mu? Boş metin temizdir. Metni hem Türkçe hem değişmez kültürle
    /// küçük harfe çevirip (I ve İ harfleri iki kültürde farklı dönüşür) her iki hâlinde de arar.
    /// </summary>
    public static bool IsObjectionable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Türkçe küçük harf ("I" → "ı") Türkçe kelimeler için, değişmez kültür ("I" → "i") büyük harfle
        // yazılmış İngilizce kelimeler için ("BITCH"); ikisi de denenir.
        return ContainsBlocked(Normalize(text, Turkish)) || ContainsBlocked(Normalize(text, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Sadeleştirilmiş metni kelimelere ayırır; kelimelerden biri listede tam olarak geçiyorsa ya da
    /// listedeki bir önekle başlıyorsa true döner.
    /// </summary>
    private static bool ContainsBlocked(string normalized)
    {
        var (exact, prefixes) = Words.Value;
        foreach (var token in Tokenize(normalized))
        {
            if (exact.Contains(token))
                return true;
            foreach (var prefix in prefixes)
            {
                if (token.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Küçük harf, rakam/sembol hileleri ve tekrarlanan harfler.</summary>
    internal static string Normalize(string text, CultureInfo culture)
    {
        var lower = text.ToLower(culture);
        var builder = new StringBuilder(lower.Length);
        char previous = '\0';
        foreach (var raw in lower)
        {
            var c = raw switch
            {
                '0' => 'o',
                '1' or '!' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' => 't',
                _ => raw
            };
            if (c == previous && char.IsLetter(c))
                continue; // "fuuuck" → "fuck"
            builder.Append(c);
            previous = c;
        }
        return builder.ToString();
    }

    /// <summary>Metni harf dizilerine (kelimelere) ayırır; harf olmayan her karakter ayırıcı sayılır.</summary>
    private static IEnumerable<string> Tokenize(string normalized)
    {
        var start = -1;
        for (var i = 0; i <= normalized.Length; i++)
        {
            var isLetter = i < normalized.Length && char.IsLetter(normalized[i]);
            if (isLetter && start < 0)
            {
                start = i;
            }
            else if (!isLetter && start >= 0)
            {
                yield return normalized[start..i];
                start = -1;
            }
        }
    }

    /// <summary>
    /// Kelime listesini assembly'ye gömülü blocked-words.txt dosyasından bir kez okur. Boş ve # ile
    /// başlayan satırlar atlanır; sonu yıldızla biten kayıtlar önek, diğerleri tam kelime olarak saklanır.
    /// </summary>
    private static (HashSet<string>, string[]) Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("blocked-words.txt", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var exact = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.EndsWith('*'))
                prefixes.Add(Normalize(line.TrimEnd('*'), Turkish));
            else
                exact.Add(Normalize(line, Turkish));
        }
        return (exact, prefixes.ToArray());
    }
}

public static class ContentFilterValidation
{
    /// <summary>FluentValidation kuralı: metin uygunsuz ifade içermesin.</summary>
    public static IRuleBuilderOptions<T, string?> NotObjectionable<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => !ContentFilter.IsObjectionable(value))
            .WithMessage("{PropertyName} uygunsuz bir ifade içeriyor. Lütfen düzelt.");
}
