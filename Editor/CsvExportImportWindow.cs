using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VRC.SDK3.Data;

/// <summary>
/// Ventana de Editor para exportar e importar traducciones en formato CSV.
/// Permite colaborar con traductores que no usan Unity (ej: Google Sheets).
///
/// Formato CSV:
///   key,en,es,ja,ko,...
///   btn_start,Start,Inicio,スタート,시작,...
///   btn_close,Close,Cerrar,閉じる,닫기,...
///
/// Flujo:
///   1. Exportar CSV desde el JSON actual
///   2. Compartir CSV con traductores (Google Sheets, Excel, etc.)
///   3. Importar CSV de vuelta al JSON
/// </summary>
public class CsvExportImportWindow : EditorWindow
{
    private static string S(string key) => IdiomasEditorStrings.Get(key);
    private string _jsonPath;
    private string _statusMessage = "";
    private Vector2 _scrollPos;

    [MenuItem("Tools/Idiomas/Exportar-Importar CSV", false, 100)]
    public static void OpenWindow()
    {
        CsvExportImportWindow window = GetWindow<CsvExportImportWindow>(
            true, S("csv_window_title"), true);
        window.minSize = new Vector2(450, 350);
        window.FindJsonPath();
        window.Show();
    }

    private void FindJsonPath()
    {
        // Buscar en Assets/ (datos del usuario) y Packages/ (instalado via VPM)
        string[][] searchPaths = new string[][] {
            new[] { "Assets/Idiomas_Data" },
            new[] { "Assets/Idiomas/Data" },
            new[] { "Packages/com.benderdios.idiomas/Data" }
        };
        for (int s = 0; s < searchPaths.Length; s++)
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", searchPaths[s]);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith("translation.json"))
                {
                    _jsonPath = Path.GetFullPath(path);
                    return;
                }
            }
        }
        _jsonPath = "";
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(S("csv_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_desc"),
            EditorStyles.miniLabel);
        EditorGUILayout.Space(8);

        // --- Archivo JSON ---
        EditorGUILayout.LabelField(S("csv_json_file"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(_jsonPath) ? S("csv_not_found") : Path.GetFileName(_jsonPath),
            EditorStyles.helpBox);

        if (string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath))
        {
            EditorGUILayout.HelpBox(S("csv_no_json_warning"), MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // === EXPORTAR ===
        EditorGUILayout.LabelField(S("csv_export_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_export_desc"),
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath));
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button(S("csv_export_btn"), GUILayout.Height(28)))
        {
            ExportCsv();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(12);

        // === IMPORTAR ===
        EditorGUILayout.LabelField(S("csv_import_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_import_desc"),
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button(S("csv_import_btn"), GUILayout.Height(28)))
        {
            ImportCsv();
        }
        GUI.backgroundColor = Color.white;

        // --- Status ---
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox);
        }

        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    // Exportar
    // =====================================================================

    private void ExportCsv()
    {
        string json = File.ReadAllText(_jsonPath, Encoding.UTF8);
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data) ||
            data.TokenType != TokenType.DataDictionary)
        {
            _statusMessage = S("error_parse_json");
            return;
        }

        DataDictionary root = data.DataDictionary;

        // Recolectar idiomas y claves
        DataList langKeys = root.GetKeys();
        List<string> languages = new List<string>();
        for (int i = 0; i < langKeys.Count; i++)
            languages.Add(langKeys[i].String);
        languages.Sort();

        HashSet<string> allKeysSet = new HashSet<string>();
        for (int i = 0; i < languages.Count; i++)
        {
            if (root.TryGetValue(languages[i], out DataToken lt) &&
                lt.TokenType == TokenType.DataDictionary)
            {
                DataList keys = lt.DataDictionary.GetKeys();
                for (int k = 0; k < keys.Count; k++)
                    allKeysSet.Add(keys[k].String);
            }
        }
        List<string> allKeys = new List<string>(allKeysSet);
        allKeys.Sort();

        // Construir CSV
        StringBuilder sb = new StringBuilder();

        // Cabecera
        sb.Append("key");
        for (int i = 0; i < languages.Count; i++)
        {
            sb.Append(",");
            sb.Append(languages[i]);
        }
        sb.AppendLine();

        // Filas
        for (int k = 0; k < allKeys.Count; k++)
        {
            sb.Append(CsvEscape(allKeys[k]));
            for (int i = 0; i < languages.Count; i++)
            {
                sb.Append(",");
                string value = "";
                if (root.TryGetValue(languages[i], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary &&
                    lt.DataDictionary.TryGetValue(allKeys[k], out DataToken vt))
                {
                    value = vt.String;
                }
                sb.Append(CsvEscape(NormalizeCsvExportLineEndings(value)));
            }
            sb.AppendLine();
        }

        // Guardar
        string csvDir = Path.GetDirectoryName(_jsonPath);

        string savePath = EditorUtility.SaveFilePanel(
            S("csv_save_dialog"), csvDir, "translation", "csv");
        if (string.IsNullOrEmpty(savePath)) return;

        File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();

        _statusMessage = string.Format(S("csv_exported"), allKeys.Count, languages.Count, Path.GetFileName(savePath));
        Debug.Log($"[Idiomas CSV] {_statusMessage}");
    }

    // =====================================================================
    // Importar
    // =====================================================================

    private void ImportCsv()
    {
        string csvPath = EditorUtility.OpenFilePanel(S("csv_open_dialog"), "", "csv");
        if (string.IsNullOrEmpty(csvPath)) return;

        string csv = File.ReadAllText(csvPath, Encoding.UTF8);
        List<string[]> records = ParseCsvRecords(csv);
        if (records.Count < 2)
        {
            _statusMessage = S("csv_empty");
            return;
        }

        // Parsear cabecera
        string[] header = records[0];
        if (header.Length < 2 || header[0].Trim().ToLower() != "key")
        {
            _statusMessage = S("csv_bad_header");
            return;
        }

        string[] languages = new string[header.Length - 1];
        for (int i = 1; i < header.Length; i++)
            languages[i - 1] = header[i].Trim();

        // Cargar JSON existente
        Dictionary<string, Dictionary<string, string>> translations;
        if (!string.IsNullOrEmpty(_jsonPath) && File.Exists(_jsonPath))
        {
            string json = File.ReadAllText(_jsonPath, Encoding.UTF8);
            translations = IdiomasEditorUtils.ParseJsonToDictionary(json);
            if (translations == null)
                translations = new Dictionary<string, Dictionary<string, string>>();
        }
        else
        {
            translations = new Dictionary<string, Dictionary<string, string>>();
        }

        // Parsear filas
        int imported = 0;
        int processedRows = 0;
        for (int row = 1; row < records.Count; row++)
        {
            string[] cols = records[row];
            if (cols.Length == 1 &&
                string.IsNullOrWhiteSpace(cols[0]))
            {
                continue;
            }
            processedRows++;
            if (cols.Length < 2) continue;

            string key = cols[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            for (int c = 0; c < languages.Length && c + 1 < cols.Length; c++)
            {
                string lang = languages[c];
                string value = NormalizeLineEndings(cols[c + 1]);

                if (string.IsNullOrEmpty(value)) continue;

                if (!translations.ContainsKey(lang))
                    translations[lang] = new Dictionary<string, string>();

                translations[lang][key] = value;
                imported++;
            }
        }

        // Escribir JSON
        if (string.IsNullOrEmpty(_jsonPath))
            _jsonPath = Path.GetFullPath("Assets/Idiomas_Data/translation.json");

        string dir = Path.GetDirectoryName(_jsonPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(_jsonPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        _statusMessage = string.Format(S("csv_imported"), imported, Path.GetFileName(csvPath));
        Debug.Log($"[Idiomas CSV] {_statusMessage}");

        EditorUtility.DisplayDialog(S("csv_import_done_title"),
            string.Format(
                S("csv_import_done_msg"),
                imported,
                string.Join(", ", languages),
                processedRows),
            S("ok"));
    }

    // =====================================================================
    // Utilidades CSV
    // =====================================================================

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Si contiene coma, comillas o salto de linea, envolver en comillas
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    /// <summary>
    /// Normaliza los saltos de linea dentro de una celda CSV.
    /// En Windows se exportan como CRLF; el JSON y Unity mantienen LF.
    /// </summary>
    private static string NormalizeCsvExportLineEndings(string value)
    {
        string normalized = NormalizeLineEndings(value);
        return Application.platform == RuntimePlatform.WindowsEditor
            ? normalized.Replace("\n", "\r\n")
            : normalized;
    }

    /// <summary>
    /// Normaliza CRLF y CR a LF al importar, independientemente del sistema.
    /// </summary>
    private static string NormalizeLineEndings(string value)
    {
        if (value == null) return "";
        return value
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
    }

    /// <summary>
    /// Parsea registros CSV respetando comillas, comas y saltos de linea
    /// dentro de los campos.
    /// </summary>
    private static List<string[]> ParseCsvRecords(string csv)
    {
        List<string[]> records = new List<string[]>();
        List<string> fields = new List<string>();
        bool inQuotes = false;
        StringBuilder current = new StringBuilder();

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Comilla doble escapada ""
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    records.Add(fields.ToArray());
                    fields.Clear();
                    if (c == '\r' &&
                        i + 1 < csv.Length &&
                        csv[i + 1] == '\n')
                    {
                        i++;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (fields.Count > 0 || current.Length > 0)
        {
            fields.Add(current.ToString());
            records.Add(fields.ToArray());
        }
        return records;
    }

}
