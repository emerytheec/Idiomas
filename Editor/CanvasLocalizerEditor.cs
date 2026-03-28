using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using VRC.SDK3.Data;

/// <summary>
/// Inspector personalizado para CanvasLocalizer.
/// Proporciona herramientas para:
///   - Escanear automaticamente todos los textos del canvas.
///   - Generar claves de traduccion a partir de la jerarquia de GameObjects.
///   - Exportar textos originales al archivo JSON de traducciones.
///   - Excluir textos que no deben traducirse (numeros, iconos, texto dinamico).
///   - Vista previa de claves vs texto actual.
///   - Registrar automaticamente el CanvasLocalizer en el LocalizationManager.
///
/// Flujo:
///   1. Configurar canvasId y baseLanguage.
///   2. Clic "Escanear Canvas" -> detecta TMP_Text y Text en todos los hijos.
///   3. Revisar tabla: ajustar claves, marcar exclusiones.
///   4. Clic "Exportar al JSON y Aplicar" -> guarda en el JSON y llena los arrays.
/// </summary>
[CustomEditor(typeof(CanvasLocalizer))]
public class CanvasLocalizerEditor : Editor
{
    // =====================================================================
    // Propiedades serializadas
    // =====================================================================

    private SerializedProperty _manager;
    private SerializedProperty _canvasId;
    private SerializedProperty _baseLanguage;
    private SerializedProperty _tmpTexts;
    private SerializedProperty _tmpKeys;
    private SerializedProperty _legacyTexts;
    private SerializedProperty _legacyKeys;
    private SerializedProperty _excludedObjects;

    // =====================================================================
    // Estado del escaneo (solo en editor, no persiste)
    // =====================================================================

    private List<ScanEntry> _scanResults;
    private bool _hasScanResults;
    private Vector2 _scrollPos;
    private bool _showAdvanced;

    /// <summary>
    /// Resultado de escaneo para un solo componente de texto.
    /// </summary>
    private struct ScanEntry
    {
        public Component component;       // TextMeshProUGUI o Text
        public bool isTMP;                // true = TMP, false = legacy Text
        public string generatedKey;       // clave de traduccion generada/editada
        public string currentText;        // texto actual del componente
        public string objectPath;         // ruta en la jerarquia (para mostrar)
        public bool excluded;             // true = no traducir este texto
        public bool existsInJson;         // true = la clave ya existe en el JSON
    }

    // =====================================================================
    // Nombres genericos que se saltan en la generacion de claves
    // =====================================================================

    private static readonly HashSet<string> GENERIC_NAMES = new HashSet<string>
    {
        "text", "label", "title", "text (tmp)", "tmp", "textmeshpro",
        "text (1)", "text (2)", "text (3)", "text (4)", "text (5)",
        "text (6)", "text (7)", "text (8)", "text (9)", "text (10)",
        "label (1)", "label (2)", "label (3)", "label (4)", "label (5)",
        "tmptext", "tmp text", "uitext", "ui text",
        "placeholder", "text area",
    };

    // =====================================================================
    // OnEnable / OnInspectorGUI
    // =====================================================================

    private void OnEnable()
    {
        _manager = serializedObject.FindProperty("manager");
        _canvasId = serializedObject.FindProperty("canvasId");
        _baseLanguage = serializedObject.FindProperty("baseLanguage");
        _tmpTexts = serializedObject.FindProperty("tmpTexts");
        _tmpKeys = serializedObject.FindProperty("tmpKeys");
        _legacyTexts = serializedObject.FindProperty("legacyTexts");
        _legacyKeys = serializedObject.FindProperty("legacyKeys");
        _excludedObjects = serializedObject.FindProperty("_excludedObjects");
        _scanResults = null;
        _hasScanResults = false;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // --- Titulo ---
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Canvas Localizer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Localiza automaticamente todos los textos de un Canvas",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // --- Configuracion basica ---
        EditorGUILayout.PropertyField(_manager,
            new GUIContent("LocalizationManager", "El manager central de idiomas de la escena."));
        EditorGUILayout.PropertyField(_canvasId,
            new GUIContent("ID del Canvas",
                "Prefijo unico para las claves. Ej: 'settings', 'lobby', 'hud'."));
        EditorGUILayout.PropertyField(_baseLanguage,
            new GUIContent("Idioma Base",
                "Idioma en que estan escritos los textos actuales. Ej: 'es', 'en'."));

        // Validar canvasId
        string canvasId = _canvasId.stringValue;
        if (string.IsNullOrEmpty(canvasId))
        {
            EditorGUILayout.HelpBox(
                "Configura un 'ID del Canvas' antes de escanear.\n" +
                "Ejemplo: 'settings', 'main_menu', 'hud'.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(8);

        // --- Resumen del estado actual ---
        int tmpCount = _tmpTexts.arraySize;
        int legacyCount = _legacyTexts.arraySize;
        int total = tmpCount + legacyCount;
        if (total > 0)
        {
            EditorGUILayout.LabelField(
                $"Textos configurados: {tmpCount} TMP + {legacyCount} Legacy = {total} total",
                EditorStyles.helpBox);
        }
        else
        {
            EditorGUILayout.LabelField(
                "Sin textos configurados. Usa 'Escanear Canvas' para empezar.",
                EditorStyles.helpBox);
        }

        EditorGUILayout.Space(5);

        // === BOTON ESCANEAR ===
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(canvasId));

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("Escanear Canvas", GUILayout.Height(32)))
        {
            ScanCanvas();
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();

        // === RESULTADOS DEL ESCANEO ===
        if (_hasScanResults && _scanResults != null && _scanResults.Count > 0)
        {
            DrawScanResults();
        }
        else if (_hasScanResults && (_scanResults == null || _scanResults.Count == 0))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "No se encontraron textos en los hijos de este GameObject.\n" +
                "Verifica que el canvas tiene componentes Text o TextMeshProUGUI.",
                MessageType.Info);
        }

        // === DATOS AVANZADOS ===
        EditorGUILayout.Space(5);
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced,
            "Datos Avanzados (auto-llenado por el escaneo)", true);
        if (_showAdvanced)
        {
            EditorGUI.indentLevel++;
            GUI.enabled = false; // Solo lectura
            EditorGUILayout.PropertyField(_tmpTexts, new GUIContent("TMP Textos"), true);
            EditorGUILayout.PropertyField(_tmpKeys, new GUIContent("TMP Claves"), true);
            EditorGUILayout.PropertyField(_legacyTexts, new GUIContent("Legacy Textos"), true);
            EditorGUILayout.PropertyField(_legacyKeys, new GUIContent("Legacy Claves"), true);
            GUI.enabled = true;
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // =====================================================================
    // Escaneo del Canvas
    // =====================================================================

    private void ScanCanvas()
    {
        CanvasLocalizer cl = (CanvasLocalizer)target;
        Transform root = cl.transform;
        string id = _canvasId.stringValue;

        // Cargar objetos excluidos
        HashSet<GameObject> excludedSet = new HashSet<GameObject>();
        for (int i = 0; i < _excludedObjects.arraySize; i++)
        {
            Object obj = _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) excludedSet.Add(obj as GameObject);
        }

        // Cargar asignaciones existentes para preservar claves ya configuradas
        Dictionary<Component, string> existingKeys = BuildExistingKeysMap();

        // Buscar otros CanvasLocalizer hijos (para no robar sus textos)
        HashSet<Transform> childCanvasRoots = new HashSet<Transform>();
        CanvasLocalizer[] childCLs = root.GetComponentsInChildren<CanvasLocalizer>(true);
        for (int i = 0; i < childCLs.Length; i++)
        {
            if (childCLs[i] != cl) // No excluirnos a nosotros mismos
            {
                childCanvasRoots.Add(childCLs[i].transform);
            }
        }

        _scanResults = new List<ScanEntry>();
        HashSet<string> usedKeys = new HashSet<string>();

        // --- Buscar TextMeshProUGUI ---
        TextMeshProUGUI[] tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TextMeshProUGUI tmp = tmps[i];

            // Saltar si esta bajo otro CanvasLocalizer hijo
            if (IsUnderAny(tmp.transform, childCanvasRoots, root)) continue;

            // Saltar si tiene un TextLocalizer (gestionado individualmente)
            if (tmp.GetComponent<TextLocalizer>() != null) continue;

            string text = tmp.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;

            // Determinar clave: preservar existente o generar nueva
            string key;
            if (existingKeys.ContainsKey(tmp))
                key = existingKeys[tmp];
            else
                key = GenerateKey(root, tmp.transform, id, usedKeys);

            usedKeys.Add(key);

            _scanResults.Add(new ScanEntry
            {
                component = tmp,
                isTMP = true,
                generatedKey = key,
                currentText = text,
                objectPath = GetRelativePath(root, tmp.transform),
                excluded = excludedSet.Contains(tmp.gameObject),
                existsInJson = false,
            });
        }

        // --- Buscar Text (legacy) ---
        Text[] legacyTexts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < legacyTexts.Length; i++)
        {
            Text txt = legacyTexts[i];

            if (IsUnderAny(txt.transform, childCanvasRoots, root)) continue;
            if (txt.GetComponent<TextLocalizer>() != null) continue;

            string text = txt.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;

            string key;
            if (existingKeys.ContainsKey(txt))
                key = existingKeys[txt];
            else
                key = GenerateKey(root, txt.transform, id, usedKeys);

            usedKeys.Add(key);

            _scanResults.Add(new ScanEntry
            {
                component = txt,
                isTMP = false,
                generatedKey = key,
                currentText = text,
                objectPath = GetRelativePath(root, txt.transform),
                excluded = excludedSet.Contains(txt.gameObject),
                existsInJson = false,
            });
        }

        // Verificar cuales claves ya existen en el JSON
        MarkExistingKeysInJson();

        _hasScanResults = true;

        Debug.Log($"[CanvasLocalizer] Escaneo completado: {_scanResults.Count} textos encontrados en '{id}'.");
    }

    /// <summary>
    /// Construye un mapa de Component -> clave existente desde los arrays serializados.
    /// Permite preservar claves ya configuradas al re-escanear.
    /// </summary>
    private Dictionary<Component, string> BuildExistingKeysMap()
    {
        Dictionary<Component, string> map = new Dictionary<Component, string>();

        for (int i = 0; i < _tmpTexts.arraySize && i < _tmpKeys.arraySize; i++)
        {
            Object comp = _tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue;
            string key = _tmpKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp != null && !string.IsNullOrEmpty(key))
            {
                map[(Component)comp] = key;
            }
        }

        for (int i = 0; i < _legacyTexts.arraySize && i < _legacyKeys.arraySize; i++)
        {
            Object comp = _legacyTexts.GetArrayElementAtIndex(i).objectReferenceValue;
            string key = _legacyKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp != null && !string.IsNullOrEmpty(key))
            {
                map[(Component)comp] = key;
            }
        }

        return map;
    }

    /// <summary>
    /// Verifica si un Transform esta bajo alguno de los roots de otros CanvasLocalizer.
    /// Evita que un CanvasLocalizer padre robe textos de un CanvasLocalizer hijo.
    /// </summary>
    private bool IsUnderAny(Transform t, HashSet<Transform> roots, Transform ownRoot)
    {
        Transform current = t;
        while (current != null && current != ownRoot)
        {
            if (roots.Contains(current)) return true;
            current = current.parent;
        }
        return false;
    }

    /// <summary>
    /// Marca en los resultados del escaneo cuales claves ya existen en el JSON.
    /// </summary>
    private void MarkExistingKeysInJson()
    {
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null) return;

        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty tfProp = mgrSO.FindProperty("translationFile");
        if (tfProp == null) return;

        TextAsset textAsset = tfProp.objectReferenceValue as TextAsset;
        if (textAsset == null) return;

        if (!VRCJson.TryDeserializeFromJson(textAsset.text, out DataToken data)) return;
        if (data.TokenType != TokenType.DataDictionary) return;

        // Buscar en TODOS los idiomas, no solo el base
        DataDictionary rootDict = data.DataDictionary;
        HashSet<string> allJsonKeys = new HashSet<string>();

        DataList langs = rootDict.GetKeys();
        for (int l = 0; l < langs.Count; l++)
        {
            if (rootDict.TryGetValue(langs[l].String, out DataToken langToken) &&
                langToken.TokenType == TokenType.DataDictionary)
            {
                DataList keys = langToken.DataDictionary.GetKeys();
                for (int k = 0; k < keys.Count; k++)
                {
                    allJsonKeys.Add(keys[k].String);
                }
            }
        }

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];
            entry.existsInJson = allJsonKeys.Contains(entry.generatedKey);
            _scanResults[i] = entry;
        }
    }

    // =====================================================================
    // Dibujar tabla de resultados
    // =====================================================================

    private void DrawScanResults()
    {
        EditorGUILayout.Space(8);

        // --- Resumen ---
        int totalCount = _scanResults.Count;
        int excludedCount = 0;
        int newCount = 0;
        int existingCount = 0;

        for (int i = 0; i < totalCount; i++)
        {
            if (_scanResults[i].excluded) excludedCount++;
            else if (_scanResults[i].existsInJson) existingCount++;
            else newCount++;
        }

        EditorGUILayout.LabelField(
            $"Resultados del Escaneo: {totalCount} textos",
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Incluidos: {totalCount - excludedCount}  |  " +
            $"Ya en JSON: {existingCount}  |  " +
            $"Nuevos: {newCount}  |  " +
            $"Excluidos: {excludedCount}",
            EditorStyles.miniLabel);

        // --- Leyenda de colores ---
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        DrawColorLegend(new Color(0.3f, 0.7f, 0.3f, 0.3f), "En JSON");
        DrawColorLegend(new Color(1f, 0.85f, 0.2f, 0.3f), "Nuevo");
        DrawColorLegend(new Color(0.5f, 0.5f, 0.5f, 0.3f), "Excluido");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        // --- Cabecera de tabla ---
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Incl.", EditorStyles.miniLabel, GUILayout.Width(32));
        GUILayout.Label("Clave de Traduccion", EditorStyles.miniLabel, GUILayout.Width(220));
        GUILayout.Label("Texto Actual", EditorStyles.miniLabel, GUILayout.MinWidth(140));
        GUILayout.Label("Ruta", EditorStyles.miniLabel, GUILayout.Width(160));
        GUILayout.Label("Tipo", EditorStyles.miniLabel, GUILayout.Width(36));
        GUILayout.Label("JSON", EditorStyles.miniLabel, GUILayout.Width(36));
        EditorGUILayout.EndHorizontal();

        // --- Lista scrollable ---
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(380));

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];

            // Color de fondo segun estado
            Color bgColor;
            if (entry.excluded)
                bgColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            else if (entry.existsInJson)
                bgColor = new Color(0.3f, 0.7f, 0.3f, 0.15f);
            else
                bgColor = new Color(1f, 0.85f, 0.2f, 0.15f);

            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            // Checkbox incluir (invertido: check = incluido)
            bool included = EditorGUILayout.Toggle(
                !entry.excluded, GUILayout.Width(32));
            if (included == entry.excluded) // Cambio
            {
                entry.excluded = !included;
                _scanResults[i] = entry;
            }

            // Clave editable
            string newKey = EditorGUILayout.TextField(
                entry.generatedKey, GUILayout.Width(220));
            if (newKey != entry.generatedKey)
            {
                entry.generatedKey = newKey;
                _scanResults[i] = entry;
            }

            // Texto actual (solo lectura, truncado si es largo)
            string displayText = entry.currentText;
            if (displayText.Length > 45)
                displayText = displayText.Substring(0, 42) + "...";
            displayText = displayText.Replace("\n", " ").Replace("\r", "");
            EditorGUILayout.LabelField(displayText, GUILayout.MinWidth(140));

            // Ruta en la jerarquia
            string displayPath = entry.objectPath;
            if (displayPath.Length > 28)
                displayPath = "..." + displayPath.Substring(displayPath.Length - 25);
            EditorGUILayout.LabelField(displayPath,
                EditorStyles.miniLabel, GUILayout.Width(160));

            // Tipo
            EditorGUILayout.LabelField(
                entry.isTMP ? "TMP" : "Text",
                EditorStyles.miniLabel, GUILayout.Width(36));

            // Estado JSON
            string jsonLabel = entry.existsInJson ? "\u2713" : "NEW";
            EditorGUILayout.LabelField(jsonLabel,
                EditorStyles.miniLabel, GUILayout.Width(36));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(5);

        // --- Botones de accion ---
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button("Exportar al JSON y Aplicar", GUILayout.Height(30)))
        {
            ExportToJsonAndApply();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Solo Aplicar\n(sin tocar JSON)", GUILayout.Height(30)))
        {
            ApplyToArrays();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        // Boton limpiar escaneo
        if (GUILayout.Button("Cerrar Resultados del Escaneo"))
        {
            _scanResults = null;
            _hasScanResults = false;
        }
    }

    private void DrawColorLegend(Color color, string label)
    {
        Rect rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
        EditorGUI.DrawRect(rect, color);
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(55));
    }

    // =====================================================================
    // Exportar al JSON
    // =====================================================================

    /// <summary>
    /// Ruta por defecto donde se crea el JSON si no existe ninguno.
    /// </summary>
    private const string DEFAULT_JSON_DIR = "Assets/Idiomas/Data";
    private const string DEFAULT_JSON_NAME = "translation.json";

    private void ExportToJsonAndApply()
    {
        if (_scanResults == null || _scanResults.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin Resultados",
                "No hay resultados de escaneo. Pulsa 'Escanear Canvas' primero.", "OK");
            return;
        }

        // Verificar manager
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null)
        {
            EditorUtility.DisplayDialog("Error",
                "No hay LocalizationManager asignado.\n" +
                "Asigna uno en el campo 'LocalizationManager'.", "OK");
            return;
        }

        string baseLang = _baseLanguage.stringValue;
        if (string.IsNullOrEmpty(baseLang))
        {
            EditorUtility.DisplayDialog("Error",
                "El 'Idioma Base' esta vacio. Configuralo antes de exportar.", "OK");
            return;
        }

        // ============================================================
        // Obtener o CREAR el archivo JSON de traducciones
        // ============================================================
        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty tfProp = mgrSO.FindProperty("translationFile");

        string assetPath;
        string fullPath;
        Dictionary<string, Dictionary<string, string>> translations;

        TextAsset textAsset = (tfProp != null) ? tfProp.objectReferenceValue as TextAsset : null;

        if (textAsset != null)
        {
            // --- Archivo existente: leer y parsear ---
            assetPath = AssetDatabase.GetAssetPath(textAsset);
            fullPath = Path.GetFullPath(assetPath);

            if (File.Exists(fullPath))
            {
                string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);
                translations = ParseJsonToDictionary(jsonContent);
                if (translations == null)
                {
                    // JSON corrupto: empezar de cero
                    Debug.LogWarning("[CanvasLocalizer] JSON corrupto, se creara uno nuevo.");
                    translations = new Dictionary<string, Dictionary<string, string>>();
                }
            }
            else
            {
                // Referencia existe pero archivo fue borrado del disco
                Debug.LogWarning($"[CanvasLocalizer] Archivo '{assetPath}' no existe en disco. Se recreara.");
                translations = new Dictionary<string, Dictionary<string, string>>();
            }
        }
        else
        {
            // --- No hay archivo asignado: CREAR uno nuevo ---
            assetPath = DEFAULT_JSON_DIR + "/" + DEFAULT_JSON_NAME;
            fullPath = Path.GetFullPath(assetPath);
            translations = new Dictionary<string, Dictionary<string, string>>();

            // Crear directorio si no existe
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Debug.Log($"[CanvasLocalizer] Creando archivo de traducciones nuevo: {assetPath}");
        }

        // ============================================================
        // Agregar claves del escaneo al idioma base
        // ============================================================
        if (!translations.ContainsKey(baseLang))
        {
            translations[baseLang] = new Dictionary<string, string>();
        }

        int added = 0;
        int updated = 0;

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];
            if (entry.excluded) continue;

            string key = entry.generatedKey;
            string value = entry.currentText;
            if (string.IsNullOrEmpty(key)) continue;

            if (!translations[baseLang].ContainsKey(key))
            {
                translations[baseLang][key] = value;
                added++;
            }
            else
            {
                // Actualizar si el texto cambio
                if (translations[baseLang][key] != value)
                {
                    translations[baseLang][key] = value;
                    updated++;
                }
            }
        }

        // ============================================================
        // Escribir JSON al disco
        // ============================================================
        string newJson = WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);

        // Refrescar para que Unity detecte el archivo nuevo/modificado
        AssetDatabase.Refresh();

        // ============================================================
        // Asignar el archivo al LocalizationManager si no tenia uno
        // ============================================================
        if (textAsset == null)
        {
            // Cargar el TextAsset recien creado
            TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (newAsset != null && tfProp != null)
            {
                tfProp.objectReferenceValue = newAsset;
                mgrSO.ApplyModifiedProperties();
                Debug.Log($"[CanvasLocalizer] Archivo asignado automaticamente al LocalizationManager.");
            }
        }

        Debug.Log($"[CanvasLocalizer] JSON exportado: {added} nuevas, {updated} actualizadas. " +
                  $"Archivo: {assetPath}");

        // Aplicar a los arrays serializados
        ApplyToArrays();

        // Dialogo
        string msg = $"Exportacion completada.\n\n" +
                     $"Claves nuevas: {added}\n" +
                     $"Claves actualizadas: {updated}\n" +
                     $"Idioma base: '{baseLang}'\n" +
                     $"Archivo: {assetPath}";

        if (added > 0)
        {
            msg += "\n\nAgrega las traducciones para otros idiomas editando el JSON.";
        }

        EditorUtility.DisplayDialog("Exportacion Completa", msg, "OK");
    }

    // =====================================================================
    // Aplicar resultados a los arrays serializados
    // =====================================================================

    private void ApplyToArrays()
    {
        if (_scanResults == null) return;

        List<TextMeshProUGUI> tmpList = new List<TextMeshProUGUI>();
        List<string> tmpKeyList = new List<string>();
        List<Text> legacyList = new List<Text>();
        List<string> legacyKeyList = new List<string>();
        List<GameObject> excludedList = new List<GameObject>();

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];

            if (entry.excluded)
            {
                if (entry.component != null)
                    excludedList.Add(entry.component.gameObject);
                continue;
            }

            if (string.IsNullOrEmpty(entry.generatedKey)) continue;

            if (entry.isTMP)
            {
                TextMeshProUGUI tmp = entry.component as TextMeshProUGUI;
                if (tmp != null)
                {
                    tmpList.Add(tmp);
                    tmpKeyList.Add(entry.generatedKey);
                }
            }
            else
            {
                Text txt = entry.component as Text;
                if (txt != null)
                {
                    legacyList.Add(txt);
                    legacyKeyList.Add(entry.generatedKey);
                }
            }
        }

        // Escribir arrays TMP
        _tmpTexts.arraySize = tmpList.Count;
        _tmpKeys.arraySize = tmpKeyList.Count;
        for (int i = 0; i < tmpList.Count; i++)
        {
            _tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue = tmpList[i];
            _tmpKeys.GetArrayElementAtIndex(i).stringValue = tmpKeyList[i];
        }

        // Escribir arrays Legacy
        _legacyTexts.arraySize = legacyList.Count;
        _legacyKeys.arraySize = legacyKeyList.Count;
        for (int i = 0; i < legacyList.Count; i++)
        {
            _legacyTexts.GetArrayElementAtIndex(i).objectReferenceValue = legacyList[i];
            _legacyKeys.GetArrayElementAtIndex(i).stringValue = legacyKeyList[i];
        }

        // Escribir exclusiones
        _excludedObjects.arraySize = excludedList.Count;
        for (int i = 0; i < excludedList.Count; i++)
        {
            _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue = excludedList[i];
        }

        serializedObject.ApplyModifiedProperties();

        // Registrar este CanvasLocalizer en el LocalizationManager
        RegisterWithManager();

        int total = tmpList.Count + legacyList.Count;
        Debug.Log($"[CanvasLocalizer] Aplicado: {tmpList.Count} TMP + " +
                  $"{legacyList.Count} Legacy = {total} textos configurados.");
    }

    /// <summary>
    /// Registra este CanvasLocalizer en el array canvasLocalizers del LocalizationManager.
    /// </summary>
    private void RegisterWithManager()
    {
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null) return;

        CanvasLocalizer cl = (CanvasLocalizer)target;
        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty clProp = mgrSO.FindProperty("canvasLocalizers");
        if (clProp == null) return;

        // Verificar si ya esta registrado
        for (int i = 0; i < clProp.arraySize; i++)
        {
            if (clProp.GetArrayElementAtIndex(i).objectReferenceValue == cl)
                return; // Ya registrado
        }

        // Agregar al final
        int index = clProp.arraySize;
        clProp.arraySize = index + 1;
        clProp.GetArrayElementAtIndex(index).objectReferenceValue = cl;
        mgrSO.ApplyModifiedProperties();

        Debug.Log($"[CanvasLocalizer] Registrado en LocalizationManager.");
    }

    // =====================================================================
    // Generacion de claves
    // =====================================================================

    /// <summary>
    /// Genera una clave de traduccion a partir de la jerarquia del GameObject.
    /// Formato: {canvasId}_{segmento1}_{segmento2}
    /// Nombres genericos como "Text", "Label" se saltan en favor del padre.
    /// Si hay duplicados se agrega _2, _3, etc.
    /// </summary>
    private string GenerateKey(Transform root, Transform textTransform,
        string canvasId, HashSet<string> usedKeys)
    {
        // Construir segmentos de ruta desde root hasta el texto
        List<string> segments = new List<string>();
        Transform current = textTransform;

        while (current != null && current != root)
        {
            segments.Insert(0, current.name);
            current = current.parent;
        }

        // Si el ultimo segmento es un nombre generico, quitarlo
        // (el nombre del padre es mas descriptivo)
        if (segments.Count > 1)
        {
            string lastName = segments[segments.Count - 1].ToLower().Trim();
            if (GENERIC_NAMES.Contains(lastName))
            {
                segments.RemoveAt(segments.Count - 1);
            }
        }

        // Normalizar cada segmento
        for (int i = 0; i < segments.Count; i++)
        {
            segments[i] = NormalizeName(segments[i]);
        }

        // Quitar segmentos vacios
        segments.RemoveAll(s => string.IsNullOrEmpty(s));

        // Construir clave
        string key;
        if (segments.Count == 0)
        {
            key = canvasId + "_text";
        }
        else
        {
            key = canvasId + "_" + string.Join("_", segments);
        }

        // Resolver duplicados
        string baseKey = key;
        int counter = 2;
        while (usedKeys.Contains(key))
        {
            key = baseKey + "_" + counter;
            counter++;
        }

        return key;
    }

    /// <summary>
    /// Normaliza un nombre de GameObject para usarlo como segmento de clave:
    /// - Quita "(Clone)", "(N)" al final
    /// - Convierte a minusculas
    /// - Reemplaza espacios/guiones/puntos por _
    /// - Quita caracteres no alfanumericos
    /// - Colapsa underscores multiples
    /// </summary>
    private static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // Quitar sufijos de Unity
        name = Regex.Replace(name, @"\s*\(Clone\)\s*$", "");
        name = Regex.Replace(name, @"\s*\(\d+\)\s*$", "");

        // Minusculas
        name = name.ToLower();

        // Reemplazar separadores comunes
        name = Regex.Replace(name, @"[\s\-\.\,\;\:\+\=]+", "_");

        // Quitar todo excepto letras, numeros, underscore
        name = Regex.Replace(name, @"[^a-z0-9_]", "");

        // Colapsar underscores
        name = Regex.Replace(name, @"_+", "_");

        // Trim
        name = name.Trim('_');

        return name;
    }

    /// <summary>
    /// Obtiene la ruta relativa desde root hasta child como "Padre/Hijo/Nieto".
    /// </summary>
    private static string GetRelativePath(Transform root, Transform child)
    {
        List<string> parts = new List<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return string.Join("/", parts);
    }

    // =====================================================================
    // JSON: Leer y escribir
    // =====================================================================

    /// <summary>
    /// Parsea el JSON de traducciones a un Dictionary anidado.
    /// Usa VRCJson para el parseo (mismo parser que usa el runtime).
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> ParseJsonToDictionary(string json)
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
                if (langData.TryGetValue(key, out DataToken valueToken))
                {
                    langDict[key] = valueToken.String;
                }
            }

            result[lang] = langDict;
        }

        return result;
    }

    /// <summary>
    /// Escribe el Dictionary anidado como JSON formateado con indentacion.
    /// Mantiene las claves ordenadas alfabeticamente para diffs limpios.
    /// </summary>
    private string WriteDictionaryToJson(
        Dictionary<string, Dictionary<string, string>> data)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");

        // Ordenar idiomas alfabeticamente
        List<string> langs = new List<string>(data.Keys);
        langs.Sort();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i];
            sb.AppendLine($"    \"{EscapeJson(lang)}\": {{");

            // Ordenar claves alfabeticamente dentro de cada idioma
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
    /// Escapa caracteres especiales para JSON.
    /// </summary>
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
