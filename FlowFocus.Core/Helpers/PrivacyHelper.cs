namespace FlowFocus.Core.Helpers;

/// <summary>
/// Хелпер для маскирования текста задач в режиме приватности
/// </summary>
public static class PrivacyHelper
{
    /// <summary>
    /// Маскирует текст, сохраняя первый символ (букву) нетронутым.
    /// Например: "Купить хлеб" -> "К***"
    /// </summary>
    public static string MaskText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Length == 1)
            return trimmed;

        // Учитываем суррогатные пары для корректной работы с emoji и расширенными символами Unicode
        var firstCharLength = char.IsHighSurrogate(trimmed[0]) && trimmed.Length > 1 && char.IsLowSurrogate(trimmed[1]) ? 2 : 1;
        if (trimmed.Length == firstCharLength)
            return trimmed;

        return trimmed[..firstCharLength] + "***";
    }
}
