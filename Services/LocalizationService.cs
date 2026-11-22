using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Orbiq.Services;

/// <summary>Offline UI localization for the nine supported interface languages.</summary>
public static class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = LoadTranslations();
    private static readonly List<TemplateEntry> TemplateTranslations = BuildTemplateTranslations();

    private sealed record TemplateEntry(Regex Pattern, string[] PlaceholderNames, Dictionary<string, string> Values);

    public static readonly (string Code, string DisplayName)[] Languages =
    {
        ("EN", "English (EN)"),
        ("RU", "Русский (RU)"),
        ("ES", "Español (ES)"),
        ("PT", "Português (PT)"),
        ("DE", "Deutsch (DE)"),
        ("FR", "Français (FR)"),
        ("ZH-CN", "简体中文 (ZH-CN)"),
        ("JA", "日本語 (JA)"),
        ("KO", "한국어 (KO)")
    };

    public static string CurrentLanguageCode { get; private set; } = "EN";

    public static CultureInfo CurrentCulture => CurrentLanguageCode switch
    {
        "RU" => CultureInfo.GetCultureInfo("ru-RU"),
        "ES" => CultureInfo.GetCultureInfo("es-ES"),
        "PT" => CultureInfo.GetCultureInfo("pt-BR"),
        "DE" => CultureInfo.GetCultureInfo("de-DE"),
        "FR" => CultureInfo.GetCultureInfo("fr-FR"),
        "ZH-CN" => CultureInfo.GetCultureInfo("zh-CN"),
        "JA" => CultureInfo.GetCultureInfo("ja-JP"),
        "KO" => CultureInfo.GetCultureInfo("ko-KR"),
        _ => CultureInfo.GetCultureInfo("en-US")
    };

    public static string NormalizeCode(string? code)
    {
        string normalized = (code ?? "EN").Trim().ToUpperInvariant();
        foreach ((string supportedCode, _) in Languages)
            if (string.Equals(normalized, supportedCode, StringComparison.Ordinal)) return supportedCode;
        return "EN";
    }

    public static void SetLanguage(string? code) => CurrentLanguageCode = NormalizeCode(code);

    public static string Translate(string? source)
    {
        if (string.IsNullOrEmpty(source) || CurrentLanguageCode == "RU") return source ?? string.Empty;
        if (Translations.TryGetValue(source, out Dictionary<string, string>? languageValues) &&
            languageValues.TryGetValue(CurrentLanguageCode, out string? translation))
            return translation;

        foreach (TemplateEntry template in TemplateTranslations)
        {
            Match match = template.Pattern.Match(source);
            if (!match.Success || !template.Values.TryGetValue(CurrentLanguageCode, out string? translatedTemplate))
                continue;

            string result = translatedTemplate;
            for (int i = 0; i < template.PlaceholderNames.Length; i++)
                result = result.Replace("{" + template.PlaceholderNames[i] + "}", match.Groups[i + 1].Value, StringComparison.Ordinal);
            return result;
        }

        return TranslateDynamic(source);
    }

    private static string TranslateDynamic(string source)
    {
        // Dynamic labels whose values change at runtime.
        if (source.StartsWith("Завершено рабочих сессий: ", StringComparison.Ordinal))
            return CurrentLanguageCode switch
            {
                "EN" => "Completed work sessions: " + source["Завершено рабочих сессий: ".Length..],
                "ES" => "Sesiones de trabajo completadas: " + source["Завершено рабочих сессий: ".Length..],
                "PT" => "Sessões de trabalho concluídas: " + source["Завершено рабочих сессий: ".Length..],
                "DE" => "Abgeschlossene Arbeitssitzungen: " + source["Завершено рабочих сессий: ".Length..],
                "FR" => "Sessions de travail terminées : " + source["Завершено рабочих сессий: ".Length..],
                "ZH-CN" => "已完成工作轮次：" + source["Завершено рабочих сессий: ".Length..],
                "JA" => "完了した作業セッション：" + source["Завершено рабочих сессий: ".Length..],
                "KO" => "완료한 작업 세션: " + source["Завершено рабочих сессий: ".Length..],
                _ => source
            };

        if (source.StartsWith("Событий: ", StringComparison.Ordinal))
            return CurrentLanguageCode switch
            {
                "EN" => "Events: " + source["Событий: ".Length..],
                "ES" => "Eventos: " + source["Событий: ".Length..],
                "PT" => "Eventos: " + source["Событий: ".Length..],
                "DE" => "Ereignisse: " + source["Событий: ".Length..],
                "FR" => "Événements : " + source["Событий: ".Length..],
                "ZH-CN" => "事件：" + source["Событий: ".Length..],
                "JA" => "イベント：" + source["Событий: ".Length..],
                "KO" => "일정: " + source["Событий: ".Length..],
                _ => source
            };

        if (source.StartsWith("Отложено до ", StringComparison.Ordinal))
            return CurrentLanguageCode switch
            {
                "EN" => "Snoozed until " + source["Отложено до ".Length..],
                "ES" => "Pospuesto hasta " + source["Отложено до ".Length..],
                "PT" => "Adiado até " + source["Отложено до ".Length..],
                "DE" => "Verschoben bis " + source["Отложено до ".Length..],
                "FR" => "Reporté jusqu’à " + source["Отложено до ".Length..],
                "ZH-CN" => "已延后至 " + source["Отложено до ".Length..],
                "JA" => "スヌーズ終了時刻：" + source["Отложено до ".Length..],
                "KO" => "다음 시간까지 미룸: " + source["Отложено до ".Length..],
                _ => source
            };

        return source;
    }


    private static List<TemplateEntry> BuildTemplateTranslations()
    {
        var templates = new List<TemplateEntry>();
        foreach (var entry in Translations)
        {
            MatchCollection placeholders = Regex.Matches(entry.Key, @"\{([A-Za-z_][A-Za-z0-9_]*)\}");
            if (placeholders.Count == 0) continue;

            var namesList = new List<string>(placeholders.Count);
            foreach (Match placeholder in placeholders)
                namesList.Add(placeholder.Groups[1].Value);
            string[] names = namesList.ToArray();
            var pattern = new System.Text.StringBuilder("^");
            int last = 0;
            foreach (Match placeholder in placeholders)
            {
                pattern.Append(Regex.Escape(entry.Key[last..placeholder.Index]));
                pattern.Append("(.+?)");
                last = placeholder.Index + placeholder.Length;
            }
            pattern.Append(Regex.Escape(entry.Key[last..]));
            pattern.Append("$");
            templates.Add(new TemplateEntry(new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline), names, entry.Value));
        }
        return templates;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadTranslations()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbiq.Localization.translations.tsv");
        if (stream is null) return result;

        using var reader = new StreamReader(stream);
        string? header = reader.ReadLine();
        if (header is null) return result;
        string[] codes = header.Split('\t');
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] values = line.Split('\t');
            if (values.Length != codes.Length || string.IsNullOrEmpty(values[0])) continue;
            var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < codes.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) localized[codes[i]] = values[i];
            result[values[0]] = localized;
        }
        return result;
    }
}

public sealed class LocalizedStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string text ? LocalizationService.Translate(text) : value ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
