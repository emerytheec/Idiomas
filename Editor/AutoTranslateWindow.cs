using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VRC.SDK3.Data;

/// <summary>
/// Ventana de Editor para auto-traducir las claves faltantes del JSON de traducciones.
/// Usa la API gratuita MyMemory (https://mymemory.translated.net/).
///
/// Limites de MyMemory (sin API key):
///   - 5000 palabras por dia
///   - Sin registro requerido
///   - Soporta todos los idiomas comunes
///
/// Flujo:
///   1. Se abre desde el Inspector del LocalizationManager
///   2. Marcar idiomas destino a traducir
///   3. Clic "Traducir" — para cada clave, detecta automaticamente
///      el idioma fuente (el que ya tiene texto) y traduce desde ahi
///   4. Revisar traducciones en la vista previa
///   5. Clic "Guardar" — escribe al JSON
/// </summary>
public class AutoTranslateWindow : EditorWindow
{
    // =====================================================================
    // Idiomas soportados
    // =====================================================================

    // Idiomas centralizados en IdiomasLanguages.cs
    private static string[] LANG_CODES => IdiomasLanguages.Codes;
    private static string[] LANG_NAMES => IdiomasLanguages.NativeNames;

    // Mapeo de codigos internos a codigos de cada API
    private static readonly Dictionary<string, string> MYMEMORY_CODES = new Dictionary<string, string>()
    {
        { "en", "en" }, { "es", "es" }, { "ja", "ja" }, { "ko", "ko" },
        { "zh-CN", "zh-CN" }, { "zh-TW", "zh-TW" }, { "ru", "ru" },
        { "pt-BR", "pt" }, { "fr", "fr" }, { "de", "de" }, { "ca", "ca" }
    };

    private static readonly Dictionary<string, string> LINGVA_CODES = new Dictionary<string, string>()
    {
        { "en", "en" }, { "es", "es" }, { "ja", "ja" }, { "ko", "ko" },
        { "zh-CN", "zh" }, { "zh-TW", "zh_HANT" }, { "ru", "ru" },
        { "pt-BR", "pt" }, { "fr", "fr" }, { "de", "de" }, { "ca", "ca" }
    };

    // Instancias de Lingva (fallback si la principal cae)
    private static readonly string[] LINGVA_HOSTS = {
        "lingva.ml",
        "translate.plausibility.cloud",
        "lingva.garudalinux.org",
    };

    // =====================================================================
    // Estado
    // =====================================================================

    private string _jsonPath;
    private Dictionary<string, Dictionary<string, string>> _translations;
    private bool[] _targetChecked;
    private Vector2 _scrollPos;
    private Vector2 _previewScroll;

    // Todas las claves que existen en algun idioma
    private HashSet<string> _allKeys;

    // Resultados de traduccion
    private Dictionary<string, Dictionary<string, string>> _newTranslations;
    private bool _hasResults;
    private string _statusMessage = "";
    private int _totalTranslated;
    private int _totalErrors;

    // =====================================================================
    // Abrir ventana
    // =====================================================================

    public static void Open(string jsonPath)
    {
        AutoTranslateWindow window = GetWindow<AutoTranslateWindow>(
            true, "Auto-Traducir Idiomas", true);
        window.minSize = new Vector2(550, 600);
        window._jsonPath = jsonPath;
        window._hasResults = false;
        window._statusMessage = "";
        window.LoadJson();
        window.Show();
    }

    // =====================================================================
    // Cargar JSON
    // =====================================================================

    private void LoadJson()
    {
        _targetChecked = new bool[LANG_CODES.Length];
        _newTranslations = new Dictionary<string, Dictionary<string, string>>();
        _allKeys = new HashSet<string>();

        if (string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath))
        {
            _translations = null;
            _statusMessage = "No se encontro el archivo JSON.";
            return;
        }

        string json = File.ReadAllText(_jsonPath, Encoding.UTF8);
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data) ||
            data.TokenType != TokenType.DataDictionary)
        {
            _translations = null;
            _statusMessage = "Error al parsear el JSON.";
            return;
        }

        // Parsear a Dictionary
        _translations = new Dictionary<string, Dictionary<string, string>>();
        DataDictionary root = data.DataDictionary;
        DataList langs = root.GetKeys();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i].String;
            if (!root.TryGetValue(lang, out DataToken langToken)) continue;
            if (langToken.TokenType != TokenType.DataDictionary) continue;

            var dict = new Dictionary<string, string>();
            DataDictionary langData = langToken.DataDictionary;
            DataList keys = langData.GetKeys();
            for (int j = 0; j < keys.Count; j++)
            {
                string key = keys[j].String;
                if (langData.TryGetValue(key, out DataToken val))
                {
                    dict[key] = val.String;
                    _allKeys.Add(key);
                }
            }
            _translations[lang] = dict;
        }

        // Marcar idiomas que tienen claves faltantes
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            int missing = CountMissing(LANG_CODES[i]);
            _targetChecked[i] = missing > 0;
        }

        _statusMessage = $"JSON cargado: {_translations.Count} idioma(s), {_allKeys.Count} clave(s) totales.";
    }

    /// <summary>
    /// Cuenta cuantas claves faltan en un idioma destino.
    /// Compara contra TODAS las claves conocidas en el JSON.
    /// </summary>
    private int CountMissing(string targetLang)
    {
        if (_allKeys == null || _allKeys.Count == 0) return 0;

        if (!_translations.ContainsKey(targetLang))
            return _allKeys.Count;

        var tgtKeys = _translations[targetLang];
        int missing = 0;
        foreach (string key in _allKeys)
        {
            if (!tgtKeys.ContainsKey(key))
                missing++;
        }
        return missing;
    }

    /// <summary>
    /// Para una clave dada, busca el idioma que tiene el texto original.
    /// Prioriza: en > es > el primero que encuentre.
    /// Excluye el idioma destino.
    /// Retorna null si no se encuentra en ningun idioma.
    /// </summary>
    private string FindSourceLanguage(string key, string excludeLang)
    {
        // Priorizar ingles y español como fuentes mas confiables
        string[] priority = { "en", "es" };
        for (int p = 0; p < priority.Length; p++)
        {
            if (priority[p] == excludeLang) continue;
            if (_translations.ContainsKey(priority[p]) &&
                _translations[priority[p]].ContainsKey(key))
                return priority[p];
        }

        // Buscar en cualquier otro idioma
        foreach (var langPair in _translations)
        {
            if (langPair.Key == excludeLang) continue;
            if (langPair.Value.ContainsKey(key))
                return langPair.Key;
        }

        return null;
    }

    // =====================================================================
    // UI
    // =====================================================================

    private void OnGUI()
    {
        if (_translations == null)
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Error);
            if (GUILayout.Button("Recargar"))
                LoadJson();
            return;
        }

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        // --- Cabecera ---
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Auto-Traducir Idiomas", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("APIs: Lingva > MyMemory > SimplyTranslate (gratis, sin registro)",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // Archivo
        EditorGUILayout.LabelField("Archivo:", Path.GetFileName(_jsonPath));

        // --- Resumen de claves por idioma ---
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Estado del JSON", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Claves totales: {_allKeys.Count} (distribuidas entre {_translations.Count} idioma(s))",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            "Cada clave se traduce desde el idioma que ya tiene su texto original.",
            EditorStyles.miniLabel);

        // --- Idiomas destino ---
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Idiomas a Completar", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Marca los idiomas a los que quieres traducir las claves faltantes.",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(3);

        int totalMissingSelected = 0;
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            string langCode = LANG_CODES[i];
            int existing = _translations.ContainsKey(langCode)
                ? _translations[langCode].Count : 0;
            int missing = CountMissing(langCode);

            string label;
            if (missing == 0)
                label = $"{langCode} — {LANG_NAMES[i]}  ({existing} claves, completo)";
            else if (existing == 0)
                label = $"{langCode} — {LANG_NAMES[i]}  (nuevo, {missing} por traducir)";
            else
                label = $"{langCode} — {LANG_NAMES[i]}  ({existing} existentes, {missing} faltan)";

            EditorGUILayout.BeginHorizontal();
            _targetChecked[i] = EditorGUILayout.ToggleLeft(label, _targetChecked[i]);
            if (_targetChecked[i])
                totalMissingSelected += missing;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField($"Total a traducir: {totalMissingSelected} texto(s)",
            EditorStyles.helpBox);

        // --- Boton traducir ---
        EditorGUILayout.Space(8);
        EditorGUI.BeginDisabledGroup(totalMissingSelected == 0);
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button($"Traducir {totalMissingSelected} Texto(s) con MyMemory",
            GUILayout.Height(32)))
        {
            RunTranslation();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        // --- Status ---
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox);
        }

        // --- Vista previa de resultados ---
        if (_hasResults && _newTranslations.Count > 0)
        {
            DrawPreview();
        }

        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    // Traduccion con MyMemory API
    // =====================================================================

    private void RunTranslation()
    {
        _newTranslations = new Dictionary<string, Dictionary<string, string>>();
        _totalTranslated = 0;
        _totalErrors = 0;

        // Contar total para progress bar
        int totalToTranslate = 0;
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            if (!_targetChecked[i]) continue;
            totalToTranslate += CountMissing(LANG_CODES[i]);
        }

        int processed = 0;

        try
        {
            for (int i = 0; i < LANG_CODES.Length; i++)
            {
                if (!_targetChecked[i]) continue;

                string tgtLang = LANG_CODES[i];
                int missing = CountMissing(tgtLang);
                if (missing == 0) continue;

                var tgtExisting = _translations.ContainsKey(tgtLang)
                    ? _translations[tgtLang]
                    : new Dictionary<string, string>();

                var newEntries = new Dictionary<string, string>();

                foreach (string key in _allKeys)
                {
                    // Ya tiene traduccion en este idioma
                    if (tgtExisting.ContainsKey(key)) continue;

                    // Buscar idioma fuente para esta clave
                    string srcLang = FindSourceLanguage(key, tgtLang);
                    if (srcLang == null) continue; // No hay fuente

                    string sourceText = _translations[srcLang][key];

                    processed++;
                    float progress = (float)processed / totalToTranslate;
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Traduciendo...",
                        $"[{srcLang}→{tgtLang}] {key}: \"{sourceText}\" ({processed}/{totalToTranslate})",
                        progress);

                    if (cancel)
                    {
                        _statusMessage = $"Cancelado. Traducidos: {_totalTranslated}, Errores: {_totalErrors}";
                        _hasResults = _newTranslations.Count > 0;
                        EditorUtility.ClearProgressBar();
                        Repaint();
                        return;
                    }

                    // Si fuente y destino son el mismo idioma, copiar directamente
                    if (srcLang == tgtLang)
                    {
                        newEntries[key] = sourceText;
                        _totalTranslated++;
                        continue;
                    }

                    string translated = TranslateText(sourceText, srcLang, tgtLang);
                    if (translated != null)
                    {
                        newEntries[key] = translated;
                        _totalTranslated++;
                    }
                    else
                    {
                        // Fallback: usar texto original con marca
                        newEntries[key] = $"[TODO:{tgtLang}] {sourceText}";
                        _totalErrors++;
                    }
                }

                if (newEntries.Count > 0)
                {
                    _newTranslations[tgtLang] = newEntries;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _hasResults = true;
        _statusMessage = $"Completado. Traducidos: {_totalTranslated}, Errores: {_totalErrors}. " +
                         "Revisa la vista previa y guarda.";
        Repaint();
    }

    /// <summary>
    /// Traduce un texto usando cadena de fallback: Lingva -> MyMemory -> SimplyTranslate.
    /// Protege bloques de rich text con contenido tecnico antes de traducir.
    /// Retorna null solo si TODAS las APIs fallan.
    /// </summary>
    private string TranslateText(string text, string fromLang, string toLang)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Proteger bloques de rich text con contenido tecnico
        List<string> protectedBlocks = new List<string>();
        string cleanText = ProtectRichTextBlocks(text, protectedBlocks);

        // Si despues de proteger no queda nada traducible, devolver el original
        string testClean = cleanText;
        for (int p = 0; p < protectedBlocks.Count; p++)
            testClean = testClean.Replace($"{{{p}}}", "");
        if (string.IsNullOrWhiteSpace(testClean))
            return text;

        // Traducir el texto con placeholders
        string translated = TranslateWithFallback(cleanText, fromLang, toLang);
        if (translated == null) return null;

        // Restaurar bloques protegidos
        for (int p = 0; p < protectedBlocks.Count; p++)
        {
            translated = translated.Replace($"{{{p}}}", protectedBlocks[p]);
            // Algunas APIs agregan espacio alrededor de {0}, limpiar variantes
            translated = translated.Replace($"{{ {p} }}", protectedBlocks[p]);
            translated = translated.Replace($"{{ {p}}}", protectedBlocks[p]);
            translated = translated.Replace($"{{{p} }}", protectedBlocks[p]);
        }

        return translated;
    }

    /// <summary>
    /// Cadena de fallback: Lingva -> MyMemory -> SimplyTranslate.
    /// </summary>
    private string TranslateWithFallback(string text, string fromLang, string toLang)
    {
        // 1. Lingva Translate (prueba varias instancias)
        string lingvaFrom = GetCode(LINGVA_CODES, fromLang);
        string lingvaTo = GetCode(LINGVA_CODES, toLang);
        for (int h = 0; h < LINGVA_HOSTS.Length; h++)
        {
            string result = TryLingva(LINGVA_HOSTS[h], text, lingvaFrom, lingvaTo);
            if (result != null) return result;
        }

        // 2. MyMemory
        string mmFrom = GetCode(MYMEMORY_CODES, fromLang);
        string mmTo = GetCode(MYMEMORY_CODES, toLang);
        string mmResult = TryMyMemory(text, mmFrom, mmTo);
        if (mmResult != null) return mmResult;

        // 3. SimplyTranslate
        string stResult = TrySimplyTranslate(text, fromLang, toLang);
        if (stResult != null) return stResult;

        Debug.LogWarning($"[AutoTranslate] Todas las APIs fallaron para '{text}' ({fromLang}→{toLang})");
        return null;
    }

    // =====================================================================
    // Proteccion de rich text
    // =====================================================================

    // Regex para bloques completos: <tag ...>contenido</tag>
    private static readonly Regex RICH_TEXT_BLOCK = new Regex(
        @"<(\w+)(?:[^>]*)>([^<]*)</\1>",
        RegexOptions.Compiled);

    // Regex para tags sueltos sin cierre: <sprite ...>, <br>, etc.
    private static readonly Regex RICH_TEXT_SELF = new Regex(
        @"<(?:sprite|br|page|nbsp|zwsp|indent|line-[a-z]+|margin[^>]*|pos[^>]*|voffset[^>]*|cspace[^>]*|mspace[^>]*|noparse|/noparse)[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Detecta bloques de rich text con contenido tecnico (CamelCase, sin espacios,
    /// nombres de codigo) y los reemplaza con placeholders {0}, {1}, etc.
    /// Bloques con texto normal (con espacios, tipo "Cerrar ventana") NO se protegen
    /// para que si se traduzcan.
    /// </summary>
    private string ProtectRichTextBlocks(string text, List<string> blocks)
    {
        // Primero proteger tags sueltos (sprite, br, etc.)
        text = RICH_TEXT_SELF.Replace(text, m =>
        {
            int idx = blocks.Count;
            blocks.Add(m.Value);
            return $"{{{idx}}}";
        });

        // Luego proteger bloques completos con contenido tecnico
        text = RICH_TEXT_BLOCK.Replace(text, m =>
        {
            string content = m.Groups[2].Value;

            // Decidir si el contenido es tecnico (proteger) o texto normal (traducir)
            if (IsTechnicalContent(content))
            {
                // Proteger el bloque completo (tag + contenido)
                int idx = blocks.Count;
                blocks.Add(m.Value);
                return $"{{{idx}}}";
            }

            // Texto normal dentro de tags: dejar el contenido para traducir,
            // pero proteger las tags envolventes
            string openTag = m.Value.Substring(0, m.Value.IndexOf('>') + 1);
            string closeTag = $"</{m.Groups[1].Value}>";

            int openIdx = blocks.Count;
            blocks.Add(openTag);
            int closeIdx = blocks.Count;
            blocks.Add(closeTag);

            return $"{{{openIdx}}}{content}{{{closeIdx}}}";
        });

        return text;
    }

    /// <summary>
    /// Determina si un texto parece contenido tecnico que no debe traducirse:
    /// - CamelCase (OnPlayerCollisionEnter)
    /// - snake_case (on_player_enter)
    /// - Contiene puntos (System.String)
    /// - Sin espacios y con mayusculas mezcladas
    /// - Solo simbolos/numeros
    /// </summary>
    private static bool IsTechnicalContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return true;

        string trimmed = content.Trim();

        // Sin espacios = probable nombre tecnico
        if (!trimmed.Contains(" "))
        {
            // CamelCase: tiene al menos una minuscula seguida de mayuscula
            if (Regex.IsMatch(trimmed, @"[a-z][A-Z]")) return true;

            // snake_case
            if (trimmed.Contains("_")) return true;

            // Contiene puntos (namespace.Class)
            if (trimmed.Contains(".")) return true;

            // Solo mayusculas (acronimo tipo "API", "SDK", "URL")
            if (Regex.IsMatch(trimmed, @"^[A-Z0-9]+$")) return true;
        }

        // Solo numeros/simbolos sin letras
        bool hasLetter = false;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (char.IsLetter(trimmed[i])) { hasLetter = true; break; }
        }
        if (!hasLetter) return true;

        return false;
    }

    // =====================================================================
    // API 1: Lingva Translate
    // =====================================================================

    private string TryLingva(string host, string text, string fromLang, string toLang)
    {
        try
        {
            string encoded = Uri.EscapeDataString(text);
            string url = $"https://{host}/api/v1/{fromLang}/{toLang}/{encoded}";

            string json = HttpGet(url);
            if (json == null) return null;

            // Respuesta: {"translation":"texto traducido"}
            Match match = Regex.Match(json, "\"translation\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (match.Success)
                return DecodeJsonString(match.Groups[1].Value);
        }
        catch (Exception e)
        {
            Debug.Log($"[AutoTranslate] Lingva ({host}) fallo: {e.Message}");
        }
        return null;
    }

    // =====================================================================
    // API 2: MyMemory
    // =====================================================================

    private string TryMyMemory(string text, string fromLang, string toLang)
    {
        try
        {
            string encoded = Uri.EscapeDataString(text);
            string url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair={fromLang}|{toLang}";

            string json = HttpGet(url);
            if (json == null) return null;

            // Respuesta: {"responseData":{"translatedText":"texto"},...}
            Match match = Regex.Match(json, "\"translatedText\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (match.Success)
                return DecodeJsonString(match.Groups[1].Value);
        }
        catch (Exception e)
        {
            Debug.Log($"[AutoTranslate] MyMemory fallo: {e.Message}");
        }
        return null;
    }

    // =====================================================================
    // API 3: SimplyTranslate
    // =====================================================================

    private string TrySimplyTranslate(string text, string fromLang, string toLang)
    {
        try
        {
            string encoded = Uri.EscapeDataString(text);
            string url = $"https://simplytranslate.org/api/translate/" +
                         $"?engine=google&from={fromLang}&to={toLang}&text={encoded}";

            string json = HttpGet(url);
            if (json == null) return null;

            // Respuesta: {"translated_text":"texto",...}
            Match match = Regex.Match(json, "\"translated_text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (match.Success)
                return DecodeJsonString(match.Groups[1].Value);
        }
        catch (Exception e)
        {
            Debug.Log($"[AutoTranslate] SimplyTranslate fallo: {e.Message}");
        }
        return null;
    }

    // =====================================================================
    // HTTP y utilidades
    // =====================================================================

    /// <summary>
    /// Realiza un HTTP GET y devuelve el cuerpo de la respuesta. Null si falla.
    /// </summary>
    private string HttpGet(string url)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 10000;
        request.UserAgent = "UnityEditor-Idiomas/1.0";

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Decodifica secuencias de escape JSON (\uXXXX, \n, \t, etc.)
    /// </summary>
    private static string DecodeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // 1. Unicode escapes \uXXXX
        s = Regex.Replace(s, @"\\u([0-9a-fA-F]{4})", m =>
        {
            int code = int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
            return ((char)code).ToString();
        });

        // 2. Secuencias simples
        s = s.Replace("\\\"", "\"");
        s = s.Replace("\\n", "\n");
        s = s.Replace("\\t", "\t");
        s = s.Replace("\\/", "/");
        s = s.Replace("\\\\", "\\");

        return s;
    }

    private static string GetCode(Dictionary<string, string> map, string langCode)
    {
        if (map.ContainsKey(langCode))
            return map[langCode];
        return langCode;
    }

    // =====================================================================
    // Vista previa de resultados
    // =====================================================================

    private void DrawPreview()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Vista Previa de Traducciones", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Revisa las traducciones. Las marcadas [TODO:xx] fallaron y necesitan traduccion manual.",
            EditorStyles.miniLabel);

        _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MaxHeight(250));

        foreach (var langPair in _newTranslations)
        {
            string lang = langPair.Key;
            var entries = langPair.Value;

            string langName = IdiomasLanguages.GetNativeName(lang);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField($"{lang} — {langName} ({entries.Count} traducciones)",
                EditorStyles.boldLabel);

            foreach (var kvp in entries)
            {
                bool isError = kvp.Value.StartsWith("[TODO:");
                Color prevColor = GUI.color;
                if (isError) GUI.color = new Color(1f, 0.6f, 0.3f);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(180));
                EditorGUILayout.LabelField(kvp.Value, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();

                GUI.color = prevColor;
            }
        }

        EditorGUILayout.EndScrollView();

        // --- Botones guardar / cancelar ---
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button("Guardar al JSON", GUILayout.Height(30)))
        {
            SaveTranslations();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Descartar", GUILayout.Height(30)))
        {
            _newTranslations.Clear();
            _hasResults = false;
            _statusMessage = "Traducciones descartadas.";
        }

        EditorGUILayout.EndHorizontal();
    }

    // =====================================================================
    // Guardar traducciones al JSON
    // =====================================================================

    private void SaveTranslations()
    {
        if (_newTranslations == null || _newTranslations.Count == 0) return;

        // Merge nuevas traducciones con las existentes
        foreach (var langPair in _newTranslations)
        {
            string lang = langPair.Key;
            var entries = langPair.Value;

            if (!_translations.ContainsKey(lang))
                _translations[lang] = new Dictionary<string, string>();

            foreach (var kvp in entries)
            {
                _translations[lang][kvp.Key] = kvp.Value;
            }
        }

        // Escribir JSON
        string json = WriteDictionaryToJson(_translations);
        File.WriteAllText(_jsonPath, json, Encoding.UTF8);
        AssetDatabase.Refresh();

        int totalSaved = 0;
        foreach (var lang in _newTranslations.Values)
            totalSaved += lang.Count;

        _statusMessage = $"Guardado: {totalSaved} traducciones en {_newTranslations.Count} idioma(s).";
        _newTranslations.Clear();
        _hasResults = false;

        // Recargar para actualizar contadores
        LoadJson();

        EditorUtility.DisplayDialog("Guardado",
            $"Se guardaron {totalSaved} traducciones.\n" +
            $"Archivo: {Path.GetFileName(_jsonPath)}\n\n" +
            "Revisa las marcadas [TODO:xx] y traducelas manualmente.",
            "OK");
    }

    // =====================================================================
    // JSON writer (mismo formato que CanvasLocalizerEditor)
    // =====================================================================

    private string WriteDictionaryToJson(Dictionary<string, Dictionary<string, string>> data)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");

        List<string> langs = new List<string>(data.Keys);
        langs.Sort();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i];
            sb.AppendLine($"    \"{EscapeJson(lang)}\": {{");

            List<string> keys = new List<string>(data[lang].Keys);
            keys.Sort();

            for (int j = 0; j < keys.Count; j++)
            {
                string key = keys[j];
                string value = data[lang][key];
                string comma = j < keys.Count - 1 ? "," : "";
                sb.AppendLine($"        \"{EscapeJson(key)}\": \"{EscapeJson(value)}\"{comma}");
            }

            string langComma = i < langs.Count - 1 ? "," : "";
            sb.AppendLine($"    }}{langComma}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
