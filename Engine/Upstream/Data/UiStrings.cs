using System.Globalization;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using NMSE.Core;

namespace NMSE.Data;

/// <summary>
/// Provides localised UI string lookups from per-language JSON files.
/// Each JSON file (e.g. <c>Resources/ui/lang/ja-JP.json</c>) is a flat key-value
/// dictionary mapping dot-delimited keys (like <c>"menu.file"</c>) to translated
/// strings.
///
/// <para>
/// The class always keeps an English fallback dictionary loaded so that any missing
/// key in the active language gracefully degrades to the English original. If even
/// the English fallback is missing the key, the raw key string is returned - this
/// makes it easy to spot untranslated strings during development.
/// </para>
///
/// <para>
/// <b>Thread safety:</b> The dictionaries are replaced atomically via reference
/// assignment, so concurrent reads are safe. Callers should not cache dictionary
/// references across language switches.
/// </para>
///
/// <para>
/// <b>Accelerator keys:</b> Translated strings may include <c>&amp;</c> prefixes
/// for WinForms accelerator/mnemonic keys (e.g. <c>"&amp;Datei"</c> for German
/// <c>"&amp;File"</c>). These are preserved as-is in the JSON and applied
/// directly to control <c>.Text</c> properties.
/// </para>
/// </summary>
public static class UiStrings
{
    /// <summary>The active (possibly translated) string dictionary.</summary>
    private static Dictionary<string, string> _strings = new();

    /// <summary>English fallback dictionary, always loaded.</summary>
    private static Dictionary<string, string> _fallback = new();
    private static Dictionary<string, string> _englishTextLookup = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _englishTextUntranslated = new(StringComparer.OrdinalIgnoreCase);
    private static (Regex Pattern, string Translation)[] _englishTextPatterns = [];
    private static readonly ConcurrentDictionary<string, byte> ReportedUntranslated = new(StringComparer.Ordinal);
    private static bool _pseudoLocalization;
    private static bool _reportUntranslated;

    /// <summary>Base directory for UI string JSON files (e.g. <c>Resources/ui/lang</c>).</summary>
    private static string? _uiLangDir;

    /// <summary>The BCP 47 tag of the currently loaded UI language, or null if defaulting to English.</summary>
    private static string? _activeTag;
    public static string ActiveLanguageTag => _pseudoLocalization ? "qps-ploc" : _activeTag ?? "en-GB";
    public static bool IsPseudoLocalization => _pseudoLocalization;

    /// <summary>
    /// Gets the number of translated UI strings in the currently active language.
    /// Returns 0 when English is active (since all strings are the fallback).
    /// </summary>
    public static int TranslatedCount
    {
        get
        {
            // When active language equals fallback, nothing is "translated"
            if (_activeTag == null || ReferenceEquals(_strings, _fallback))
                return 0;
            return _strings.Count;
        }
    }

    /// <summary>
    /// Gets the total number of UI string keys defined in the English fallback.
    /// </summary>
    public static int TotalKeyCount => _fallback.Count;

    /// <summary>
    /// Gets whether a non-English UI language is currently active.
    /// </summary>
    public static bool IsActive => _activeTag != null && !ReferenceEquals(_strings, _fallback);

    /// <summary>
    /// Sets the directory that contains the per-language UI string JSON files.
    /// Must be called before <see cref="Load"/>.
    /// </summary>
    /// <param name="uiLangDir">
    /// Absolute or relative path to the directory containing UI string JSONs
    /// (e.g. <c>Resources/ui/lang</c>).
    /// </param>
    public static void SetDirectory(string uiLangDir)
    {
        _uiLangDir = uiLangDir;
    }

    /// <summary>
    /// Loads the UI string tables for the specified language.
    /// Always loads <c>en-GB.json</c> as the fallback first, then overlays
    /// the requested language on top (if different from <c>en-GB</c>).
    /// </summary>
    /// <param name="bcp47Tag">
    /// BCP 47 language tag (e.g. <c>"ja-JP"</c>). Pass <c>null</c>,
    /// empty, or <c>"en-GB"</c> to use English defaults only.
    /// </param>
    /// <returns>
    /// <c>true</c> if the language was loaded (or English was selected);
    /// <c>false</c> if the requested language file could not be found.
    /// </returns>
    public static bool Load(string? bcp47Tag)
    {
        _pseudoLocalization = string.Equals(Environment.GetEnvironmentVariable("NMSFE_PSEUDO_LOCALIZATION"), "1", StringComparison.Ordinal);
        _reportUntranslated = string.Equals(Environment.GetEnvironmentVariable("NMSFE_LOCALIZATION_AUDIT"), "1", StringComparison.Ordinal);
        ReportedUntranslated.Clear();
        if (_uiLangDir == null)
            return false;

        // Always (re-)load English fallback
        var fallback = LoadFile(Path.Combine(_uiLangDir, "en-GB.json"));
        if (fallback != null)
            _fallback = fallback;

        // If caller wants English (or no language), just point active at fallback
        if (_pseudoLocalization || string.IsNullOrEmpty(bcp47Tag) || bcp47Tag == "en-GB")
        {
            _strings = _fallback;
            _activeTag = _pseudoLocalization ? "qps-ploc" : null;
            RebuildEnglishTextLookup();
            return true;
        }

        // Try to load the requested language
        var translated = LoadFile(Path.Combine(_uiLangDir, $"{bcp47Tag}.json"));
        if (translated == null || translated.Count == 0)
        {
            _strings = _fallback;
            _activeTag = null;
            RebuildEnglishTextLookup();
            return false;
        }

        _strings = translated;
        _activeTag = bcp47Tag;
        RebuildEnglishTextLookup();
        return true;
    }

    /// <summary>
    /// Returns the localised UI string for the given key.
    /// Falls back to the English value if the key is missing in the active language,
    /// and returns the raw <paramref name="key"/> if even the English fallback is missing.
    /// </summary>
    /// <param name="key">
    /// Dot-delimited string key (e.g. <c>"menu.file"</c>, <c>"tab.player"</c>).
    /// </param>
    /// <returns>The localised string, English fallback, or the raw key if not found.</returns>
    /// <summary>Localises a complete UI label or message when it matches a resource value.</summary>
    public static string LocalizeText(string text) => LocalizeText(text, null);

    /// <summary>Localises visible text and, when requested, records unresolved UI labels.</summary>
    public static string LocalizeText(string text, string? context)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string normalized = NormalizeUiText(text);
        if (_englishTextLookup.TryGetValue(normalized, out string? translated))
        {
            if (_englishTextUntranslated.Contains(normalized) && _reportUntranslated && !string.IsNullOrWhiteSpace(context)) ReportUntranslated(text, context);
            return Pseudo(translated);
        }
        foreach (var entry in _englishTextPatterns)
        {
            Match match = entry.Pattern.Match(normalized);
            if (!match.Success) continue;
            try
            {
                object[] args = Enumerable.Range(1, match.Groups.Count - 1).Select(index => (object)match.Groups[index].Value).ToArray();
                return Pseudo(string.Format(CultureInfo.InvariantCulture, entry.Translation, args));
            }
            catch (FormatException) { }
        }
        if (_reportUntranslated && !string.IsNullOrWhiteSpace(context)) ReportUntranslated(text, context);
        return text;
    }

    private static void ReportUntranslated(string text, string context)
    {
        if (_activeTag == null || _activeTag == "en-GB" || string.IsNullOrWhiteSpace(text) || !text.Any(char.IsAsciiLetter)) return;
        string key = _activeTag + "\u001f" + context + "\u001f" + text;
        if (!ReportedUntranslated.TryAdd(key, 0)) return;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NMSFE", "LocalizationAudit");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "untranslated-" + _activeTag + ".jsonl");
            string row = JsonSerializer.Serialize(new { language = _activeTag, context, text, observedUtc = DateTimeOffset.UtcNow });
            File.AppendAllText(path, row + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* Diagnostics must never interfere with editor use. */ }
    }

    private static string Pseudo(string value)
    {
        if (!_pseudoLocalization || string.IsNullOrEmpty(value)) return value;
        var preserved = new List<string>();
        string expanded = Regex.Replace(value, @"\{[^}]+\}|%[^%]+%|\r|\n", match =>
        {
            preserved.Add(match.Value);
            return "\uE000" + (preserved.Count - 1).ToString(CultureInfo.InvariantCulture) + "\uE001";
        });
        var builder = new StringBuilder(expanded.Length + 12);
        foreach (char c in expanded)
            builder.Append(c switch
            {
                'a' => 'á', 'e' => 'ë', 'i' => 'ï', 'o' => 'ô', 'u' => 'ü',
                'A' => 'Á', 'E' => 'Ë', 'I' => 'Ï', 'O' => 'Ô', 'U' => 'Ü',
                _ => c
            });
        string translated = Regex.Replace(builder.ToString(), "\\uE000(\\d+)\\uE001", match =>
            preserved[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)]);
        return "[!! " + translated + " !!]";
    }

    private static void RebuildEnglishTextLookup()
    {
        var exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var untranslated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new List<(Regex Pattern, string Translation)>();
        foreach (var group in _fallback.Where(pair => !pair.Key.StartsWith("_meta.", StringComparison.Ordinal))
            .GroupBy(pair => NormalizeUiText(pair.Value), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0) continue;
            string[] translations = group.Select(pair => _strings.TryGetValue(pair.Key, out string? value) ? value : pair.Value)
                .Distinct(StringComparer.Ordinal).ToArray();
            string? preferred = group
                .Select(pair => _strings.TryGetValue(pair.Key, out string? value) ? value : pair.Value)
                .GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(values => values.Count())
                .ThenBy(values => values.Key, StringComparer.Ordinal)
                .Select(values => values.Key)
                .FirstOrDefault();
            if (preferred is null) continue;
            if (!group.Any(pair => _strings.ContainsKey(pair.Key))) untranslated.Add(group.Key);
            translations = [preferred];
            exact[group.Key] = translations[0];
            if (!Regex.IsMatch(group.Key, @"\{\d+\}")) continue;
            string pattern = Regex.Escape(group.Key);
            pattern = Regex.Replace(pattern, @"\\\{(\d+)\\\}", "(.+?)");
            try { patterns.Add((new Regex("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), translations[0])); }
            catch (ArgumentException) { }
        }
        _englishTextLookup = exact;
        _englishTextUntranslated = untranslated;
        _englishTextPatterns = patterns.ToArray();
    }

    private static string NormalizeUiText(string text)
    {
        string normalized = text.Replace("&", string.Empty, StringComparison.Ordinal).Replace("…", "...", StringComparison.Ordinal);
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }

    /// <summary>
    /// Returns the localised UI string for the given key, or <c>null</c> if the
    /// key is not found in either the active language or the English fallback.
    /// Useful when callers need to distinguish "key not defined" from an empty string.
    /// </summary>
    /// <param name="key">Dot-delimited string key.</param>
    /// <returns>The localised string, or <c>null</c> if the key does not exist.</returns>
    public static string? GetOrNull(string key)
    {
        if (_strings.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;
        return null;
    }

    /// <summary>
    /// Formats a localised UI string using <see cref="string.Format(string, object[])"/>.
    /// The format string is retrieved via <see cref="Get"/>, then the supplied
    /// arguments are substituted.
    /// </summary>
    /// <param name="key">Dot-delimited string key whose value contains format placeholders.</param>
    /// <param name="args">Format arguments to substitute into the retrieved string.</param>
    /// <returns>The formatted localised string.</returns>
    /// <example>
    /// <code>
    /// // en-GB.json: "status.loaded_items": "Loaded {0} game items, {1} name mappings"
    /// string msg = UiStrings.Format("status.loaded_items", itemCount, mapCount);
    /// </code>
    /// </example>
    public static string Format(string key, params object[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // If the translated string has mismatched placeholders, return unformatted
            return template;
        }
    }

    /// <summary>
    /// Resets the UI string tables to empty state. Primarily used in unit tests.
    /// </summary>
    internal static void Reset()
    {
        _strings = new Dictionary<string, string>();
        _fallback = new Dictionary<string, string>();
        _activeTag = null;
        _uiLangDir = null;
        _pseudoLocalization = false;
        _reportUntranslated = false;
        ReportedUntranslated.Clear();
    }

    /// <summary>
    /// Loads a single JSON file into a string dictionary.
    /// Returns <c>null</c> if the file does not exist or cannot be parsed.
    /// </summary>
    private static Dictionary<string, string>? LoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.DictionaryStringString);
        }
        catch
        {
            return null;
        }
    }
}
