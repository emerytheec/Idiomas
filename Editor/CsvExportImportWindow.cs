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
    private string _jsonPath;
    private string _statusMessage = "";
    private Vector2 _scrollPos;

    [MenuItem("Tools/Idiomas/Exportar-Importar CSV", false, 100)]
    public static void OpenWindow()
    {
        CsvExportImportWindow window = GetWindow<CsvExportImportWindow>(
            true, "Idiomas - CSV Export/Import", true);
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
        EditorGUILayout.LabelField("Exportar / Importar CSV", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Permite compartir traducciones con traductores via Google Sheets, Excel, etc.",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(8);

        // --- Archivo JSON ---
        EditorGUILayout.LabelField("Archivo JSON:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(_jsonPath) ? "(no encontrado)" : Path.GetFileName(_jsonPath),
            EditorStyles.helpBox);

        if (string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath))
        {
            EditorGUILayout.HelpBox(
                "No se encontro translation.json.\n" +
                "Exporta primero desde un CanvasLocalizer.", MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // === EXPORTAR ===
        EditorGUILayout.LabelField("Exportar a CSV", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Genera un archivo CSV con todas las traducciones.\n" +
            "Formato: key, en, es, ja, ko, ... (una fila por clave, una columna por idioma).",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath));
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("Exportar JSON a CSV", GUILayout.Height(28)))
        {
            ExportCsv();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(12);

        // === IMPORTAR ===
        EditorGUILayout.LabelField("Importar desde CSV", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Lee un archivo CSV y lo fusiona con el JSON existente.\n" +
            "Las traducciones nuevas se agregan, las existentes se actualizan.",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button("Importar CSV al JSON", GUILayout.Height(28)))
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
            _statusMessage = "Error al parsear el JSON.";
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
                sb.Append(CsvEscape(value));
            }
            sb.AppendLine();
        }

        // Guardar
        string csvDir = Path.GetDirectoryName(_jsonPath);

        string savePath = EditorUtility.SaveFilePanel(
            "Guardar CSV", csvDir, "translation", "csv");
        if (string.IsNullOrEmpty(savePath)) return;

        File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();

        _statusMessage = $"Exportado: {allKeys.Count} claves x {languages.Count} idiomas a {Path.GetFileName(savePath)}";
        Debug.Log($"[Idiomas CSV] {_statusMessage}");
    }

    // =====================================================================
    // Importar
    // =====================================================================

    private void ImportCsv()
    {
        string csvPath = EditorUtility.OpenFilePanel("Abrir CSV", "", "csv");
        if (string.IsNullOrEmpty(csvPath)) return;

        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length < 2)
        {
            _statusMessage = "El CSV esta vacio o solo tiene cabecera.";
            return;
        }

        // Parsear cabecera
        string[] header = ParseCsvLine(lines[0]);
        if (header.Length < 2 || header[0].Trim().ToLower() != "key")
        {
            _statusMessage = "El CSV debe tener 'key' como primera columna.";
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
        for (int row = 1; row < lines.Length; row++)
        {
            if (string.IsNullOrEmpty(lines[row].Trim())) continue;

            string[] cols = ParseCsvLine(lines[row]);
            if (cols.Length < 2) continue;

            string key = cols[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            for (int c = 0; c < languages.Length && c + 1 < cols.Length; c++)
            {
                string lang = languages[c];
                string value = cols[c + 1];

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

        _statusMessage = $"Importado: {imported} traducciones desde {Path.GetFileName(csvPath)}";
        Debug.Log($"[Idiomas CSV] {_statusMessage}");

        EditorUtility.DisplayDialog("Importacion Completa",
            $"Se importaron {imported} traducciones.\n" +
            $"Idiomas: {string.Join(", ", languages)}\n" +
            $"Filas procesadas: {lines.Length - 1}", "OK");
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
    /// Parsea una linea CSV respetando comillas (campos con comas dentro de comillas).
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        StringBuilder current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Comilla doble escapada ""
                    if (i + 1 < line.Length && line[i + 1] == '"')
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
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

}
