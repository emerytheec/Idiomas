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
///   2. Seleccionar idioma origen (el que tiene las claves completas)
///   3. Marcar idiomas destino
///   4. Clic "Traducir" — traduce con MyMemory y muestra progreso
///   5. Revisar traducciones en la vista previa
///   6. Clic "Guardar" — escribe al JSON
/// </summary>
public class AutoTranslateWindow : EditorWindow
{
    // =====================================================================
    // Idiomas soportados
    // =====================================================================

    private static readonly string[] LANG_CODES = {
        "en", "es", "ja", "ko", "zh-CN", "zh-TW", "ru", "pt-BR", "fr", "de", "ca"
    };

    private static readonly string[] LANG_NAMES = {
        "English", "Español", "日本語", "한국어", "中文 (简体)",
        "中文 (繁體)", "Русский", "Português", "Français", "Deutsch", "Català"
    };

    // Mapeo de nuestros codigos a codigos de MyMemory API
    private static readonly Dictionary<string, string> API_CODES = new Dictionary<string, string>()
    {
        { "en", "en" }, { "es", "es" }, { "ja", "ja" }, { "ko", "ko" },
        { "zh-CN", "zh-CN" }, { "zh-TW", "zh-TW" }, { "ru", "ru" },
        { "pt-BR", "pt" }, { "fr", "fr" }, { "de", "de" }, { "ca", "ca" }
    };

    // =====================================================================
    // Estado
    // =====================================================================

    private string _jsonPath;
    private Dictionary<string, Dictionary<string, string>> _translations;
    private int _sourceLanguageIndex;
    private bool[] _targetChecked;
    private Vector2 _scrollPos;
    private Vector2 _previewScroll;

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
                    dict[key] = val.String;
            }
            _translations[lang] = dict;
        }

        // Autodetectar idioma origen (el que tiene mas claves)
        _sourceLanguageIndex = 0;
        int maxKeys = 0;
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            if (_translations.ContainsKey(LANG_CODES[i]) &&
                _translations[LANG_CODES[i]].Count > maxKeys)
            {
                maxKeys = _translations[LANG_CODES[i]].Count;
                _sourceLanguageIndex = i;
            }
        }

        // Marcar idiomas que tienen claves faltantes
        string srcLang = LANG_CODES[_sourceLanguageIndex];
        if (_translations.ContainsKey(srcLang))
        {
            for (int i = 0; i < LANG_CODES.Length; i++)
            {
                if (i == _sourceLanguageIndex) continue;
                int missing = CountMissing(srcLang, LANG_CODES[i]);
                _targetChecked[i] = missing > 0;
            }
        }

        _statusMessage = $"JSON cargado: {_translations.Count} idioma(s), {maxKeys} clave(s).";
    }

    private int CountMissing(string sourceLang, string targetLang)
    {
        if (!_translations.ContainsKey(sourceLang)) return 0;
        var srcKeys = _translations[sourceLang];

        if (!_translations.ContainsKey(targetLang))
            return srcKeys.Count;

        var tgtKeys = _translations[targetLang];
        int missing = 0;
        foreach (var key in srcKeys.Keys)
        {
            if (!tgtKeys.ContainsKey(key))
                missing++;
        }
        return missing;
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
        EditorGUILayout.LabelField("API: MyMemory (gratis, 5000 palabras/dia, sin registro)",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // Archivo
        EditorGUILayout.LabelField("Archivo:", Path.GetFileName(_jsonPath));

        // --- Idioma origen ---
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Idioma Origen", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Desde que idioma se traducira a los demas.",
            EditorStyles.miniLabel);

        string[] sourceOptions = BuildSourceOptions();
        int newSourceIdx = EditorGUILayout.Popup("Traducir desde:", _sourceLanguageIndex, sourceOptions);
        if (newSourceIdx != _sourceLanguageIndex)
        {
            _sourceLanguageIndex = newSourceIdx;
            _hasResults = false;
        }

        string srcLang = LANG_CODES[_sourceLanguageIndex];
        int srcKeyCount = _translations.ContainsKey(srcLang) ? _translations[srcLang].Count : 0;
        EditorGUILayout.LabelField($"Claves en '{srcLang}': {srcKeyCount}");

        // --- Idiomas destino ---
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Idiomas Destino", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Marca los idiomas a los que quieres traducir.",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(3);

        int totalMissingSelected = 0;
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            if (i == _sourceLanguageIndex) continue;

            int missing = CountMissing(srcLang, LANG_CODES[i]);
            int existing = _translations.ContainsKey(LANG_CODES[i])
                ? _translations[LANG_CODES[i]].Count : 0;

            string label;
            if (missing == 0)
                label = $"{LANG_CODES[i]} — {LANG_NAMES[i]}  ({existing} claves, completo)";
            else if (existing == 0)
                label = $"{LANG_CODES[i]} — {LANG_NAMES[i]}  (nuevo, {missing} por traducir)";
            else
                label = $"{LANG_CODES[i]} — {LANG_NAMES[i]}  ({existing} existentes, {missing} faltan)";

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

    private string[] BuildSourceOptions()
    {
        string[] options = new string[LANG_CODES.Length];
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            int count = _translations.ContainsKey(LANG_CODES[i])
                ? _translations[LANG_CODES[i]].Count : 0;
            options[i] = $"{LANG_CODES[i]} — {LANG_NAMES[i]} ({count} claves)";
        }
        return options;
    }

    // =====================================================================
    // Traduccion con MyMemory API
    // =====================================================================

    private void RunTranslation()
    {
        string srcLang = LANG_CODES[_sourceLanguageIndex];
        if (!_translations.ContainsKey(srcLang))
        {
            _statusMessage = $"El idioma origen '{srcLang}' no tiene claves en el JSON.";
            return;
        }

        var srcKeys = _translations[srcLang];
        string srcApiCode = GetApiCode(srcLang);

        _newTranslations = new Dictionary<string, Dictionary<string, string>>();
        _totalTranslated = 0;
        _totalErrors = 0;

        // Contar total para progress bar
        int totalToTranslate = 0;
        for (int i = 0; i < LANG_CODES.Length; i++)
        {
            if (!_targetChecked[i] || i == _sourceLanguageIndex) continue;
            totalToTranslate += CountMissing(srcLang, LANG_CODES[i]);
        }

        int processed = 0;

        try
        {
            for (int i = 0; i < LANG_CODES.Length; i++)
            {
                if (!_targetChecked[i] || i == _sourceLanguageIndex) continue;

                string tgtLang = LANG_CODES[i];
                string tgtApiCode = GetApiCode(tgtLang);
                int missing = CountMissing(srcLang, tgtLang);
                if (missing == 0) continue;

                var tgtExisting = _translations.ContainsKey(tgtLang)
                    ? _translations[tgtLang]
                    : new Dictionary<string, string>();

                var newEntries = new Dictionary<string, string>();

                foreach (var kvp in srcKeys)
                {
                    string key = kvp.Key;
                    string sourceText = kvp.Value;

                    if (tgtExisting.ContainsKey(key)) continue;

                    processed++;
                    float progress = (float)processed / totalToTranslate;
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Traduciendo...",
                        $"[{tgtLang}] {key}: \"{sourceText}\" ({processed}/{totalToTranslate})",
                        progress);

                    if (cancel)
                    {
                        _statusMessage = $"Cancelado. Traducidos: {_totalTranslated}, Errores: {_totalErrors}";
                        _hasResults = _newTranslations.Count > 0;
                        EditorUtility.ClearProgressBar();
                        Repaint();
                        return;
                    }

                    string translated = TranslateText(sourceText, srcApiCode, tgtApiCode);
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
    /// Traduce un texto usando la API de MyMemory.
    /// Retorna null si falla.
    /// </summary>
    private string TranslateText(string text, string fromLang, string toLang)
    {
        if (string.IsNullOrEmpty(text)) return "";

        try
        {
            string encoded = Uri.EscapeDataString(text);
            string url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair={fromLang}|{toLang}";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 10000; // 10 segundos
            request.UserAgent = "UnityEditor-Idiomas/1.0";

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                return ExtractTranslation(json);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AutoTranslate] Error traduciendo '{text}' ({fromLang}→{toLang}): {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extrae "translatedText" de la respuesta JSON de MyMemory.
    /// Formato: {"responseData":{"translatedText":"texto traducido"},...}
    /// </summary>
    private string ExtractTranslation(string json)
    {
        // Buscar "translatedText":"..."
        Match match = Regex.Match(json, "\"translatedText\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (match.Success)
        {
            string result = match.Groups[1].Value;

            // Decodificar secuencias de escape JSON en orden correcto
            // 1. Primero decodificar \uXXXX (unicode escapes)
            result = Regex.Replace(result, @"\\u([0-9a-fA-F]{4})", m =>
            {
                int code = int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                return ((char)code).ToString();
            });

            // 2. Luego las secuencias simples
            result = result.Replace("\\\"", "\"");
            result = result.Replace("\\n", "\n");
            result = result.Replace("\\t", "\t");
            result = result.Replace("\\/", "/");
            result = result.Replace("\\\\", "\\");

            return result;
        }
        return null;
    }

    private string GetApiCode(string langCode)
    {
        if (API_CODES.ContainsKey(langCode))
            return API_CODES[langCode];
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

            // Nombre del idioma
            string langName = lang;
            for (int i = 0; i < LANG_CODES.Length; i++)
            {
                if (LANG_CODES[i] == lang) { langName = LANG_NAMES[i]; break; }
            }

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
