/// <summary>
/// Lista centralizada de idiomas soportados por el sistema Idiomas.
/// Todos los editor scripts deben usar esta clase en lugar de definir
/// sus propias listas de codigos/nombres.
///
/// Para agregar un idioma nuevo, solo hay que modificar este archivo.
/// </summary>
public static class IdiomasLanguages
{
    // =====================================================================
    // Codigos ISO y nombres nativos (orden fijo, indices paralelos)
    // =====================================================================

    public static readonly string[] Codes = {
        "en", "es", "ja", "ko", "zh-CN", "zh-TW", "ru", "pt-BR", "fr", "de", "ca"
    };

    public static readonly string[] NativeNames = {
        "English", "Español", "日本語", "한국어", "中文 (简体)",
        "中文 (繁體)", "Русский", "Português", "Français", "Deutsch", "Català"
    };

    public static readonly string[] LatinNames = {
        "English", "Espanol", "Japanese", "Korean",
        "Chinese Simplified", "Chinese Traditional",
        "Russian", "Portuguese", "French", "German", "Catalan"
    };

    // =====================================================================
    // Labels pre-construidos para dropdowns/popups
    // =====================================================================

    /// <summary>Labels formato "en — English" (con nombre nativo).</summary>
    public static readonly string[] PopupLabels;

    /// <summary>Labels formato "en — English" (con nombre latin).</summary>
    public static readonly string[] PopupLabelsLatin;

    static IdiomasLanguages()
    {
        int len = Codes.Length;
        PopupLabels = new string[len];
        PopupLabelsLatin = new string[len];
        for (int i = 0; i < len; i++)
        {
            PopupLabels[i] = $"{Codes[i]} — {NativeNames[i]}";
            PopupLabelsLatin[i] = $"{Codes[i]} — {LatinNames[i]}";
        }
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    /// <summary>Busca el indice de un codigo de idioma. Devuelve -1 si no existe.</summary>
    public static int IndexOf(string code)
    {
        if (string.IsNullOrEmpty(code)) return -1;
        for (int i = 0; i < Codes.Length; i++)
        {
            if (Codes[i] == code) return i;
        }
        return -1;
    }

    /// <summary>Devuelve el nombre nativo de un codigo, o el codigo mismo si no se encuentra.</summary>
    public static string GetNativeName(string code)
    {
        int idx = IndexOf(code);
        return idx >= 0 ? NativeNames[idx] : code;
    }

    /// <summary>Devuelve el nombre latin de un codigo, o el codigo mismo si no se encuentra.</summary>
    public static string GetLatinName(string code)
    {
        int idx = IndexOf(code);
        return idx >= 0 ? LatinNames[idx] : code;
    }
}
