using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbiq.Services;

/// <summary>Получает справочные курсы валют из Frankfurter API v2.</summary>
public sealed class CurrencyRateService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly Dictionary<string, CurrencyRate> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _cacheFetchedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async Task<CurrencyRate> GetRateAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken = default)
    {
        string baseCode = NormalizeCode(baseCurrency);
        string quoteCode = NormalizeCode(quoteCurrency);
        if (baseCode == quoteCode)
            return new CurrencyRate(1m, DateTime.Today);

        string key = baseCode + ":" + quoteCode;
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out CurrencyRate cached) &&
                _cacheFetchedAt.TryGetValue(key, out DateTime fetchedAt) &&
                DateTime.UtcNow - fetchedAt < TimeSpan.FromHours(6))
                return cached;
        }
        finally
        {
            _cacheLock.Release();
        }

        string requestUrl = $"https://api.frankfurter.dev/v2/rate/{baseCode.ToLowerInvariant()}/{quoteCode.ToLowerInvariant()}";
        using HttpResponseMessage response = await Client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("rate", out JsonElement rateElement) || !rateElement.TryGetDecimal(out decimal rate) || rate <= 0)
            throw new InvalidDataException("Сервис курсов вернул некорректное значение.");

        DateTime rateDate = DateTime.Today;
        if (root.TryGetProperty("date", out JsonElement dateElement) &&
            DateTime.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsedDate))
            rateDate = parsedDate.Date;

        var result = new CurrencyRate(rate, rateDate);
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cache[key] = result;
            _cacheFetchedAt[key] = DateTime.UtcNow;
        }
        finally
        {
            _cacheLock.Release();
        }
        return result;
    }

    private static string NormalizeCode(string code)
    {
        string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !char.IsAsciiLetterUpper(normalized[0]) ||
            !char.IsAsciiLetterUpper(normalized[1]) || !char.IsAsciiLetterUpper(normalized[2]))
            throw new ArgumentException("Код валюты должен состоять из трёх латинских букв.");
        return normalized;
    }
}

public readonly record struct CurrencyRate(decimal Rate, DateTime Date);
