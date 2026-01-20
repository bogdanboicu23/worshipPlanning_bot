using System.Globalization;
using System.Resources;

namespace WorshipPlannerBot.Api.Services;

public class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private readonly ILogger<LocalizationService> _logger;
    private const string DefaultLanguage = "en";
    private readonly string[] _supportedLanguages = { "en", "ro", "ru" };

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;
        _resourceManager = new ResourceManager("WorshipPlannerBot.Api.Resources.Messages", typeof(LocalizationService).Assembly);
        _logger.LogInformation($"Localization service initialized with support for: {string.Join(", ", _supportedLanguages)}");
    }

    public string GetString(string key, string? languageCode = null)
    {
        try
        {
            var culture = GetCultureInfo(languageCode ?? DefaultLanguage);
            var value = _resourceManager.GetString(key, culture);

            if (value != null)
                return value;

            // Try fallback to English if not found
            if (languageCode != DefaultLanguage)
            {
                culture = GetCultureInfo(DefaultLanguage);
                value = _resourceManager.GetString(key, culture);
                if (value != null)
                    return value;
            }

            _logger.LogWarning($"Missing translation for key: {key} in language: {languageCode}");
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting translation for key: {key} in language: {languageCode}");
            return key;
        }
    }

    public string GetString(string key, string? languageCode, params object[] args)
    {
        var format = GetString(key, languageCode);
        return string.Format(format, args);
    }

    public bool IsLanguageSupported(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        return _supportedLanguages.Contains(normalized);
    }

    public string[] GetSupportedLanguages()
    {
        return _supportedLanguages;
    }

    private CultureInfo GetCultureInfo(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);

        // Map to proper culture codes
        var cultureCode = normalized switch
        {
            "ro" => "ro-RO",
            "ru" => "ru-RU",
            "en" => "en-US",
            _ => "en-US"
        };

        try
        {
            return CultureInfo.GetCultureInfo(cultureCode);
        }
        catch
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    private string NormalizeLanguageCode(string languageCode)
    {
        // Telegram sends language codes like "en", "ro", "ru", "en-US", etc.
        // We normalize to just the language part
        if (string.IsNullOrEmpty(languageCode))
            return DefaultLanguage;

        var parts = languageCode.ToLower().Split('-', '_');
        var normalized = parts[0];

        // Map common language codes
        return normalized switch
        {
            "ro" => "ro",  // Romanian
            "ru" => "ru",  // Russian
            "en" => "en",  // English
            _ => DefaultLanguage
        };
    }
}

public interface ILocalizationService
{
    string GetString(string key, string? languageCode = null);
    string GetString(string key, string? languageCode, params object[] args);
    bool IsLanguageSupported(string languageCode);
    string[] GetSupportedLanguages();
}