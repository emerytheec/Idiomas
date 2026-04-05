using UnityEditor;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using VRC.SDK3.Data;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Utilidades compartidas para los editor scripts del sistema Idiomas.
/// Centraliza la logica de parseo/escritura de JSON y busqueda de UdonBehaviour
/// que antes estaba duplicada en AutoTranslateWindow, CsvExportImportWindow,
/// LocalizationManagerEditor e IdiomasPrefabCreator.
/// </summary>
public static class IdiomasEditorUtils
{
    // =====================================================================
    // JSON: Parseo
    // =====================================================================

    /// <summary>
    /// Parsea un string JSON de traducciones a un Dictionary editable.
    /// Formato esperado: { "lang": { "key": "value", ... }, ... }
    /// Retorna null si el JSON es invalido.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ParseJsonToDictionary(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data)) return null;
        if (data.TokenType != TokenType.DataDictionary) return null;

        var result = new Dictionary<string, Dictionary<string, string>>();
        DataDictionary rootDict = data.DataDictionary;
        DataList langs = rootDict.GetKeys();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i].String;
            if (!rootDict.TryGetValue(lang, out DataToken langToken)) continue;
            if (langToken.TokenType != TokenType.DataDictionary) continue;

            var langDict = new Dictionary<string, string>();
            DataDictionary langData = langToken.DataDictionary;
            DataList keys = langData.GetKeys();
            for (int j = 0; j < keys.Count; j++)
            {
                string key = keys[j].String;
                if (langData.TryGetValue(key, out DataToken val))
                    langDict[key] = val.String;
            }
            result[lang] = langDict;
        }
        return result;
    }

    // =====================================================================
    // JSON: Escritura
    // =====================================================================

    /// <summary>
    /// Escribe un Dictionary de traducciones a formato JSON con indentacion.
    /// Ordena idiomas y claves alfabeticamente para consistencia.
    /// </summary>
    public static string WriteDictionaryToJson(Dictionary<string, Dictionary<string, string>> data)
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

    /// <summary>
    /// Escapa caracteres especiales para insertar un string dentro de JSON.
    /// Maneja: backslash, comillas, newlines, tabs, carriage return.
    /// </summary>
    public static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // =====================================================================
    // Canvas: Normalizacion de nombres y generacion de IDs unicos
    // =====================================================================

    /// <summary>
    /// Normaliza un nombre de GameObject para usarlo como ID o segmento de clave.
    /// Quita sufijos de Unity (Clone, (1)), pasa a minusculas, reemplaza separadores
    /// por underscore, y elimina caracteres no alfanumericos.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(Clone\)\s*$", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)\s*$", "");
        name = name.ToLower();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[\s\-\.\,\;\:\+\=]+", "_");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9_]", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"_+", "_");
        name = name.Trim('_');
        return name;
    }

    /// <summary>
    /// Genera un canvasId unico basado en el nombre del GameObject.
    /// Verifica todos los CanvasLocalizer de la escena para evitar colisiones.
    /// </summary>
    public static string GenerateUniqueCanvasId(string goName, CanvasLocalizer self)
    {
        string baseName = NormalizeName(goName);
        if (string.IsNullOrEmpty(baseName)) baseName = "canvas";

        HashSet<string> usedIds = new HashSet<string>();
        CanvasLocalizer[] allLocalizers = Object.FindObjectsByType<CanvasLocalizer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allLocalizers.Length; i++)
        {
            if (allLocalizers[i] == self) continue;
            string otherId = allLocalizers[i].GetCanvasId();
            if (!string.IsNullOrEmpty(otherId))
                usedIds.Add(otherId);
        }

        string finalId = baseName;
        int counter = 2;
        while (usedIds.Contains(finalId))
        {
            finalId = baseName + "_" + counter;
            counter++;
        }

        return finalId;
    }

    // =====================================================================
    // UdonSharp: Busqueda de UdonBehaviour
    // =====================================================================

    /// <summary>
    /// Busca el UdonBehaviour que respalda un UdonSharpBehaviour (proxy C#).
    /// Intenta tres metodos en cascada:
    ///   1. UdonSharpEditorUtility.GetBackingUdonBehaviour (oficial)
    ///   2. Propiedad serializada _udonSharpBackingUdonBehaviour
    ///   3. Primer UdonBehaviour del mismo GameObject (fallback)
    /// Retorna null si no se encuentra.
    /// </summary>
    public static UdonBehaviour FindUdonBehaviourFor(UdonSharpBehaviour proxy)
    {
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        if (udon != null) return udon;

        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty bp = so.FindProperty("_udonSharpBackingUdonBehaviour");
        if (bp != null && bp.objectReferenceValue != null)
        {
            udon = bp.objectReferenceValue as UdonBehaviour;
            if (udon != null) return udon;
        }

        UdonBehaviour[] udons = proxy.GetComponents<UdonBehaviour>();
        if (udons != null && udons.Length > 0) return udons[0];

        return null;
    }
}
