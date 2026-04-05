using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VRC.SDK3.Data;
using VRC.Udon;

/// <summary>
/// Inspector personalizado para LocalizationManager.
/// Orden visual: Config basica > Estadisticas > Dropdown > Listeners > Localizers > Canvas > Herramientas
/// </summary>
[CustomEditor(typeof(LocalizationManager))]
public class LocalizationManagerEditor : Editor
{
    // Propiedades serializadas
    private SerializedProperty _translationFile;
    private SerializedProperty _fallbackLanguage;
    private SerializedProperty _localizers;
    private SerializedProperty _canvasLocalizers;
    private SerializedProperty _languageDropdown;
    private SerializedProperty _dropdownLanguageCodes;
    private SerializedProperty _listeners;

    // Estado del editor (foldouts)
    private bool _showCanvasSearch = true;
    private bool _showTools = false;
    private bool _showListeners = false;
    private bool _showDropdown = false;
    private bool _showPreview = false;
    private string _previewLanguage = "en";
    private static List<CanvasSearchResult> _canvasSearchResults;
    private int _quickSetupLangIndex = 0; // Default: "en" (indice en IdiomasLanguages.Codes)

    // Cache del JSON
    private DataDictionary _cachedData;
    private string _cachedJsonHash;
    private string[] _cachedLanguages;
    private string[] _cachedKeys;



    private void OnEnable()
    {
        _translationFile = serializedObject.FindProperty("translationFile");
        _fallbackLanguage = serializedObject.FindProperty("fallbackLanguage");
        _localizers = serializedObject.FindProperty("localizers");
        _canvasLocalizers = serializedObject.FindProperty("canvasLocalizers");
        _languageDropdown = serializedObject.FindProperty("_languageDropdown");
        _dropdownLanguageCodes = serializedObject.FindProperty("_dropdownLanguageCodes");
        _listeners = serializedObject.FindProperty("_listeners");

        // Limpiar resultados de escaneo anteriores para evitar
        // MissingReferenceException por GameObjects destruidos
        // (la lista es static y puede sobrevivir entre recargas)
        _canvasSearchResults = null;
    }

    public override void OnInspectorGUI()
    {
        // Cabecera estandar de UdonSharp (program asset, sync settings, etc.)
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        serializedObject.Update();

        // === TITULO ===
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Localization Manager", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        // === CONFIGURACION BASICA ===
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(_translationFile,
            new GUIContent("Archivo de Traducciones"));

        bool hasJson = _translationFile.objectReferenceValue != null;
        string createBtnLabel = hasJson ? "Crear Nuevo" : "Crear JSON";
        string createBtnTooltip = hasJson
            ? "Crea un nuevo archivo JSON vacio sin borrar el actual."
            : "Crea un archivo JSON vacio para tus traducciones.";

        if (GUILayout.Button(new GUIContent(createBtnLabel, createBtnTooltip),
            GUILayout.Width(82), GUILayout.Height(18)))
        {
            CreateTranslationJsonFile();
        }
        EditorGUILayout.EndHorizontal();

        // Idioma de Fallback como dropdown
        string currentFallback = _fallbackLanguage.stringValue;
        int fbIndex = -1;
        for (int i = 0; i < IdiomasLanguages.Codes.Length; i++)
        {
            if (IdiomasLanguages.Codes[i] == currentFallback) { fbIndex = i; break; }
        }
        int newFbIndex = EditorGUILayout.Popup(
            new GUIContent("Idioma de Fallback",
                "Si el idioma del jugador no existe en el JSON, se usa este."),
            fbIndex, IdiomasLanguages.PopupLabelsLatin);
        if (newFbIndex >= 0 && newFbIndex != fbIndex)
        {
            _fallbackLanguage.stringValue = IdiomasLanguages.Codes[newFbIndex];
        }

        RefreshCache();

        // === VERIFICACION DE FONTS TMP ===
        if (!IdiomasFontSetup.AreFontsConfigured())
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Las fallback fonts CJK/cirilico no estan configuradas en TMP Settings.\n" +
                "Los caracteres en japones, coreano, chino y ruso no se mostraran correctamente.",
                MessageType.Warning);
            if (GUILayout.Button("Configurar Fonts Automaticamente"))
            {
                int result = IdiomasFontSetup.SetupFonts();
                if (result > 0)
                {
                    EditorUtility.DisplayDialog("Fonts Configuradas",
                        $"Se agregaron {result} font(s) de fallback a TMP Settings.", "OK");
                }
            }
        }

        // === ESTADISTICAS ===
        EditorGUILayout.Space(8);
        if (_cachedLanguages != null && _cachedLanguages.Length > 0)
        {
            int listenerCount = _listeners != null ? _listeners.arraySize : 0;
            EditorGUILayout.LabelField(
                $"Idiomas: {_cachedLanguages.Length} ({string.Join(", ", _cachedLanguages)})",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField(
                $"Claves: {(_cachedKeys != null ? _cachedKeys.Length : 0)}  |  " +
                $"Canvas: {_canvasLocalizers.arraySize}  |  " +
                $"Textos: {_localizers.arraySize}  |  " +
                $"Listeners: {listenerCount}",
                EditorStyles.helpBox);
        }

        // === BUSCAR CANVAS SIN LOCALIZAR ===
        EditorGUILayout.Space(5);
        _showCanvasSearch = EditorGUILayout.Foldout(_showCanvasSearch,
            "Buscar Canvas sin Localizar", true, EditorStyles.foldoutHeader);
        if (_showCanvasSearch)
        {
            EditorGUI.indentLevel++;
            DrawCanvasSearch();
            EditorGUI.indentLevel--;
        }

        // === TRADUCCION (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showTools = EditorGUILayout.Foldout(_showTools,
            "Traduccion", true, EditorStyles.foldoutHeader);
        if (_showTools)
        {
            EditorGUI.indentLevel++;

            // Auto-Traducir
            EditorGUILayout.Space(3);
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button("Auto-Traducir Idiomas Faltantes", GUILayout.Height(24)))
            {
                OpenAutoTranslateWindow();
            }
            GUI.backgroundColor = Color.white;

            // Vista Previa de Traducciones
            EditorGUILayout.Space(3);
            _showPreview = EditorGUILayout.Foldout(_showPreview,
                "Vista Previa de Traducciones", true);
            if (_showPreview)
            {
                DrawPreview();
            }

            EditorGUI.indentLevel--;
        }

        // === LISTENERS (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showListeners = EditorGUILayout.Foldout(_showListeners,
            $"Listeners ({(_listeners != null ? _listeners.arraySize : 0)})",
            true, EditorStyles.foldoutHeader);
        if (_showListeners)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Arrastra aqui scripts de tu mundo que necesiten saber cuando cambia el idioma.\n\n" +
                "Ejemplo: un script que cambia imagenes segun el idioma, o que reproduce un audio diferente. " +
                "Cada script debe tener un metodo publico llamado \"_OnLanguageChanged()\" para recibir el aviso.\n\n" +
                "Si todos tus textos se traducen automaticamente con TextLocalizer o CanvasLocalizer, " +
                "no necesitas poner nada aqui.",
                MessageType.Info);

            EditorGUILayout.Space(3);
            EditorGUILayout.PropertyField(_listeners,
                new GUIContent("Listeners"), true);

            EditorGUI.indentLevel--;
        }

        // === SELECTOR DE IDIOMA - DROPDOWN (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showDropdown = EditorGUILayout.Foldout(_showDropdown,
            "Selector de Idioma (Dropdown)", true, EditorStyles.foldoutHeader);
        if (_showDropdown)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Esta seccion solo es necesaria si tu mundo tiene un menu desplegable (Dropdown) " +
                "para que los jugadores cambien el idioma manualmente.\n\n" +
                "Si no usas dropdown, el idioma se detecta automaticamente y puedes ignorar esta seccion.",
                MessageType.Info);

            EditorGUILayout.Space(3);
            EditorGUILayout.PropertyField(_languageDropdown,
                new GUIContent("Dropdown",
                    "Arrastra aqui el TMP_Dropdown del selector de idioma."));

            if (_languageDropdown.objectReferenceValue != null)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Codigos de Idioma: cada codigo debe coincidir con la opcion del dropdown en el mismo orden.\n" +
                    "Ejemplo: si el dropdown tiene [English, Espanol, Japanese], los codigos serian [en, es, ja].",
                    MessageType.None);

                EditorGUILayout.PropertyField(_dropdownLanguageCodes,
                    new GUIContent("Codigos de Idioma"), true);

                TMP_Dropdown dd = _languageDropdown.objectReferenceValue as TMP_Dropdown;
                if (dd != null)
                {
                    SerializedObject ddSO = new SerializedObject(dd);
                    SerializedProperty onVal = ddSO.FindProperty("m_OnValueChanged");
                    SerializedProperty calls = onVal.FindPropertyRelative("m_PersistentCalls.m_Calls");

                    if (calls.arraySize == 0)
                    {
                        EditorGUILayout.Space(3);
                        GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
                        if (GUILayout.Button("Conectar Dropdown (OnValueChanged)", GUILayout.Height(26)))
                        {
                            WireDropdown(dd);
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.HelpBox(
                            "El dropdown no esta conectado al LocalizationManager. " +
                            "Pulsa el boton para conectarlo automaticamente.",
                            MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Dropdown conectado correctamente.", MessageType.Info);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // =====================================================================
    // Conectar Dropdown
    // =====================================================================

    private void WireDropdown(TMP_Dropdown dropdown)
    {
        LocalizationManager mgr = (LocalizationManager)target;
        UdonBehaviour mgrUdon = IdiomasEditorUtils.FindUdonBehaviourFor(mgr);

        if (mgrUdon == null)
        {
            EditorUtility.DisplayDialog("UdonBehaviour no encontrado",
                "Selecciona el LocalizationManager, espera 2 segundos " +
                "(UdonSharp lo procesa), y pulsa de nuevo.", "OK");
            return;
        }

        SerializedObject ddSO = new SerializedObject(dropdown);
        SerializedProperty onVal = ddSO.FindProperty("m_OnValueChanged");
        SerializedProperty calls = onVal.FindPropertyRelative("m_PersistentCalls.m_Calls");

        calls.ClearArray();
        calls.arraySize = 1;
        SerializedProperty entry = calls.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("m_Target").objectReferenceValue = mgrUdon;
        entry.FindPropertyRelative("m_MethodName").stringValue = "SendCustomEvent";
        entry.FindPropertyRelative("m_Mode").intValue = 5;
        entry.FindPropertyRelative("m_Arguments")
            .FindPropertyRelative("m_StringArgument").stringValue = "OnLanguageDropdownChanged";
        entry.FindPropertyRelative("m_CallState").intValue = 2;
        ddSO.ApplyModifiedProperties();

        Debug.Log("[Idiomas] Dropdown conectado al LocalizationManager.");
        EditorUtility.DisplayDialog("Conectado",
            "Dropdown conectado. Prueba dando Play.", "OK");
    }

    // =====================================================================
    // Auto-buscar
    // =====================================================================

    // =====================================================================
    // Auto-Traducir
    // =====================================================================

    private void OpenAutoTranslateWindow()
    {
        TextAsset textAsset = _translationFile.objectReferenceValue as TextAsset;
        if (textAsset == null)
        {
            EditorUtility.DisplayDialog("Sin Archivo",
                "No hay archivo de traducciones. Exporta desde un CanvasLocalizer primero.", "OK");
            return;
        }
        string path = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(textAsset));
        AutoTranslateWindow.Open(path);
    }

    // =====================================================================
    // Validacion (bajo demanda, con cache de resultados)
    // =====================================================================

    // =====================================================================
    // Vista previa
    // =====================================================================

    private void DrawPreview()
    {
        if (_cachedData == null || _cachedLanguages == null || _cachedLanguages.Length == 0)
        {
            EditorGUILayout.HelpBox("Asigna un archivo JSON.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Idioma:", GUILayout.Width(50));
        for (int i = 0; i < _cachedLanguages.Length; i++)
        {
            GUI.backgroundColor = _previewLanguage == _cachedLanguages[i] ? Color.cyan : Color.white;
            if (GUILayout.Button(_cachedLanguages[i], GUILayout.Width(50)))
                _previewLanguage = _cachedLanguages[i];
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        if (_cachedData.TryGetValue(_previewLanguage, out DataToken lt) &&
            lt.TokenType == TokenType.DataDictionary)
        {
            DataDictionary ld = lt.DataDictionary;
            DataList keys = ld.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(keys[i].String, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(ld[keys[i].String].String, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    // =====================================================================
    // Buscar Canvas en la escena
    // =====================================================================

    private class CanvasSearchResult
    {
        public GameObject gameObject;
        public Canvas canvas;
        public int tmpCount;
        public int legacyCount;
        public bool hasCanvasLocalizer;
        public string hierarchyPath;
        public string parentCanvasLocalizerName;
    }

    private void ScanSceneForCanvas()
    {
        _canvasSearchResults = new List<CanvasSearchResult>();
        Canvas[] allCanvas = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < allCanvas.Length; i++)
        {
            Canvas canvas = allCanvas[i];
            GameObject go = canvas.gameObject;

            // Contar textos hijos
            TextMeshProUGUI[] tmpAll = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            Text[] legacyAll = go.GetComponentsInChildren<Text>(true);

            // Excluir textos que ya tengan TextLocalizer individual
            int tmpCount = 0;
            for (int t = 0; t < tmpAll.Length; t++)
                if (tmpAll[t].GetComponent<TextLocalizer>() == null) tmpCount++;

            int legacyCount = 0;
            for (int t = 0; t < legacyAll.Length; t++)
                if (legacyAll[t].GetComponent<TextLocalizer>() == null) legacyCount++;

            // Verificar si ya tiene CanvasLocalizer
            bool hasCL = go.GetComponent<CanvasLocalizer>() != null;

            // Verificar si es hijo de otro Canvas con CanvasLocalizer
            string parentCLName = null;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                CanvasLocalizer parentCL = parent.GetComponent<CanvasLocalizer>();
                if (parentCL != null)
                {
                    parentCLName = parent.gameObject.name;
                    break;
                }
                parent = parent.parent;
            }

            // Construir path de jerarquia
            string path = BuildHierarchyPath(go.transform);

            _canvasSearchResults.Add(new CanvasSearchResult
            {
                gameObject = go,
                canvas = canvas,
                tmpCount = tmpCount,
                legacyCount = legacyCount,
                hasCanvasLocalizer = hasCL,
                hierarchyPath = path,
                parentCanvasLocalizerName = parentCLName
            });
        }

        // Filtrar GameObjects destruidos antes de ordenar
        _canvasSearchResults.RemoveAll(r => r.gameObject == null);

        // Ordenar: primero los que NO tienen CanvasLocalizer, luego los que si
        _canvasSearchResults.Sort((a, b) =>
        {
            if (a.hasCanvasLocalizer != b.hasCanvasLocalizer)
                return a.hasCanvasLocalizer ? 1 : -1;
            return string.Compare(a.gameObject.name, b.gameObject.name);
        });
    }

    private static string BuildHierarchyPath(Transform t)
    {
        string path = t.name;
        Transform parent = t.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private void DrawCanvasSearch()
    {
        EditorGUILayout.Space(3);
        GUI.backgroundColor = new Color(0.9f, 0.8f, 0.3f);
        if (GUILayout.Button("Escanear Escena", GUILayout.Height(24)))
        {
            ScanSceneForCanvas();
        }
        GUI.backgroundColor = Color.white;

        // Filtrar elementos con GameObjects destruidos (evita MissingReferenceException)
        if (_canvasSearchResults != null)
        {
            _canvasSearchResults.RemoveAll(r => r.gameObject == null);
        }

        // === CONFIGURACION RAPIDA ===
        // Solo mostrar si hay resultados de escaneo con candidatos
        if (_canvasSearchResults != null)
        {
            int qsCandidateCount = 0;
            int qsCandidateTextCount = 0;
            for (int i = 0; i < _canvasSearchResults.Count; i++)
            {
                if (!_canvasSearchResults[i].hasCanvasLocalizer &&
                    _canvasSearchResults[i].tmpCount + _canvasSearchResults[i].legacyCount > 0)
                {
                    qsCandidateCount++;
                    qsCandidateTextCount += _canvasSearchResults[i].tmpCount
                        + _canvasSearchResults[i].legacyCount;
                }
            }

            if (qsCandidateCount > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    "Configuracion Rapida", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Procesar {qsCandidateCount} canvas (~{qsCandidateTextCount} textos) de una sola vez.\n" +
                    "Anade CanvasLocalizer, escanea textos y exporta al JSON automaticamente.\n" +
                    "Despues puedes ajustar claves y exclusiones en cada canvas.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(3);
                _quickSetupLangIndex = EditorGUILayout.Popup(
                    new GUIContent("Idioma Base",
                        "Idioma en que estan escritos los textos actuales de los canvas."),
                    _quickSetupLangIndex, IdiomasLanguages.PopupLabelsLatin);

                EditorGUILayout.Space(3);
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.5f);
                if (GUILayout.Button(
                    $"Configuracion Rapida: Localizar Todo ({qsCandidateCount})",
                    GUILayout.Height(28)))
                {
                    QuickSetupAll(qsCandidateCount, qsCandidateTextCount);
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndVertical();
            }
        }

        if (_canvasSearchResults == null)
        {
            EditorGUILayout.HelpBox(
                "Pulsa \"Escanear Escena\" para buscar Canvas que necesiten localizacion.",
                MessageType.Info);
            return;
        }

        if (_canvasSearchResults.Count == 0)
        {
            EditorGUILayout.HelpBox("No se encontraron Canvas en la escena.", MessageType.Info);
            return;
        }

        // Contar candidatos vs ya localizados
        int candidates = 0;
        int alreadyLocalized = 0;
        int noTexts = 0;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            if (_canvasSearchResults[i].hasCanvasLocalizer) alreadyLocalized++;
            else if (_canvasSearchResults[i].tmpCount + _canvasSearchResults[i].legacyCount == 0) noTexts++;
            else candidates++;
        }

        EditorGUILayout.LabelField(
            $"Encontrados: {_canvasSearchResults.Count} Canvas  |  " +
            $"{candidates} candidatos  |  {alreadyLocalized} ya localizados  |  {noTexts} sin textos",
            EditorStyles.helpBox);

        EditorGUILayout.Space(3);

        // --- Candidatos (sin CanvasLocalizer, con textos) ---
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.hasCanvasLocalizer) continue;
            int totalTexts = r.tmpCount + r.legacyCount;
            if (totalTexts == 0) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Nombre clickable para seleccionar en jerarquia
            GUIStyle linkStyle = new GUIStyle(EditorStyles.label);
            linkStyle.fontStyle = FontStyle.Bold;
            linkStyle.normal.textColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button(r.gameObject.name, linkStyle, GUILayout.ExpandWidth(false)))
            {
                EditorGUIUtility.PingObject(r.gameObject);
            }

            // Conteo de textos
            string textsInfo = "";
            if (r.tmpCount > 0) textsInfo += $"{r.tmpCount} TMP";
            if (r.legacyCount > 0)
            {
                if (textsInfo.Length > 0) textsInfo += " + ";
                textsInfo += $"{r.legacyCount} Legacy";
            }
            EditorGUILayout.LabelField(textsInfo, GUILayout.Width(120));

            // Empujar boton a la derecha
            GUILayout.FlexibleSpace();

            // Boton para anadir CanvasLocalizer
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Anadir CanvasLocalizer", GUILayout.Width(170)))
            {
                AddCanvasLocalizerTo(r);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            // Path de jerarquia (en gris, mas pequeno)
            GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel);
            pathStyle.normal.textColor = Color.gray;
            EditorGUILayout.LabelField(r.hierarchyPath, pathStyle);

            // Aviso si es hijo de otro Canvas con CanvasLocalizer
            if (r.parentCanvasLocalizerName != null)
            {
                EditorGUILayout.HelpBox(
                    $"Es hijo de \"{r.parentCanvasLocalizerName}\" que ya tiene CanvasLocalizer. " +
                    "Sus textos podrian estar cubiertos por el padre.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // --- Ya localizados (con CanvasLocalizer) ---
        bool hasLocalized = false;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (!r.hasCanvasLocalizer) continue;

            if (!hasLocalized)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Ya localizados:", EditorStyles.boldLabel);
                hasLocalized = true;
            }

            if (r.gameObject == null) continue;
            CanvasLocalizer cl = r.gameObject.GetComponent<CanvasLocalizer>();
            string clId = cl != null ? cl.GetCanvasId() : "";

            // Obtener claves del componente y contar cuantas estan en el JSON
            List<string> clKeys = GetCanvasLocalizerKeys(cl);
            int keysInJson = CountKeysInJson(clKeys);
            int totalKeys = clKeys.Count;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Linea 1: nombre + info + estado JSON
            EditorGUILayout.BeginHorizontal();

            GUIStyle greenStyle = new GUIStyle(EditorStyles.label);
            greenStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
            if (GUILayout.Button(r.gameObject.name, greenStyle, GUILayout.ExpandWidth(false)))
            {
                EditorGUIUtility.PingObject(r.gameObject);
            }

            EditorGUILayout.LabelField(
                $"canvasId: \"{clId}\"  |  {r.tmpCount + r.legacyCount} textos",
                EditorStyles.miniLabel);

            // Indicador de estado en el JSON
            GUIStyle jsonStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            jsonStatusStyle.fontStyle = FontStyle.Bold;
            string jsonStatusText;
            if (totalKeys == 0)
            {
                jsonStatusStyle.normal.textColor = new Color(0.8f, 0.5f, 0.0f);
                jsonStatusText = "Sin claves asignadas";
            }
            else if (keysInJson == 0)
            {
                jsonStatusStyle.normal.textColor = new Color(0.9f, 0.3f, 0.0f);
                jsonStatusText = $"JSON: 0/{totalKeys} (sin exportar)";
            }
            else if (keysInJson < totalKeys)
            {
                jsonStatusStyle.normal.textColor = new Color(0.8f, 0.7f, 0.0f);
                jsonStatusText = $"JSON: {keysInJson}/{totalKeys} (parcial)";
            }
            else
            {
                jsonStatusStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
                jsonStatusText = $"JSON: {keysInJson}/{totalKeys}";
            }
            EditorGUILayout.LabelField(jsonStatusText, jsonStatusStyle, GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();

            // Linea 2: botones alineados a la derecha
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Boton para limpiar claves del JSON
            EditorGUI.BeginDisabledGroup(keysInJson == 0);
            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
            if (GUILayout.Button($"Limpiar JSON ({keysInJson})", GUILayout.Width(130)))
            {
                RemoveKeysFromJson(cl, clKeys, keysInJson);
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // Boton para quitar CanvasLocalizer
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("Quitar", GUILayout.Width(60)))
            {
                RemoveCanvasLocalizerFrom(r);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // --- Sin textos ---
        bool hasEmpty = false;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.hasCanvasLocalizer) continue;
            if (r.tmpCount + r.legacyCount > 0) continue;

            if (!hasEmpty)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Sin textos (no necesitan localizacion):", EditorStyles.miniLabel);
                hasEmpty = true;
            }

            GUIStyle grayStyle = new GUIStyle(EditorStyles.miniLabel);
            grayStyle.normal.textColor = Color.gray;
            EditorGUILayout.LabelField($"  {r.gameObject.name}", grayStyle);
        }
    }

    private void RemoveCanvasLocalizerFrom(CanvasSearchResult result)
    {
        GameObject go = result.gameObject;
        CanvasLocalizer cl = go.GetComponent<CanvasLocalizer>();
        if (cl == null) return;

        string canvasId = cl.GetCanvasId();
        int textCount = cl.GetTextCount();
        if (!EditorUtility.DisplayDialog("Quitar CanvasLocalizer",
            $"Quitar CanvasLocalizer de \"{go.name}\"?\n" +
            $"(canvasId: \"{canvasId}\", {textCount} textos)\n\n" +
            "Las claves con prefijo \"" + canvasId + "_\" seguiran en el JSON.\n" +
            "Si no las necesitas, borralas manualmente del archivo de traducciones.\n\n" +
            "Puedes deshacerlo con Ctrl+Z.",
            "Quitar", "Cancelar"))
        {
            return;
        }

        // Quitar de la lista de canvasLocalizers del manager
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            if (_canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue == cl)
            {
                // En Unity, si el slot tiene referencia, el primer DeleteArrayElement solo lo pone a null
                _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue = null;
                _canvasLocalizers.DeleteArrayElementAtIndex(i);
                serializedObject.ApplyModifiedProperties();
                break;
            }
        }

        // Quitar el UdonBehaviour asociado (backing) y luego el componente C#
        UdonBehaviour udon = IdiomasEditorUtils.FindUdonBehaviourFor(cl);
        if (udon != null) Undo.DestroyObjectImmediate(udon);
        Undo.DestroyObjectImmediate(cl);

        result.hasCanvasLocalizer = false;
        EditorUtility.SetDirty(go);

        Debug.Log($"[Idiomas] CanvasLocalizer quitado de \"{go.name}\".");
    }

    private void AddCanvasLocalizerTo(CanvasSearchResult result)
    {
        LocalizationManager mgr = (LocalizationManager)target;
        GameObject go = result.gameObject;

        // Usar UdonSharpUndo para soporte de Ctrl+Z
        CanvasLocalizer newCL = UdonSharpUndo.AddComponent<CanvasLocalizer>(go);

        if (newCL == null)
        {
            EditorUtility.DisplayDialog("Error",
                "No se pudo anadir el componente CanvasLocalizer.\n" +
                "Asegurate de que UdonSharp esta correctamente instalado.",
                "OK");
            return;
        }

        // Asignar el LocalizationManager y un canvasId unico
        SerializedObject clSO = new SerializedObject(newCL);
        SerializedProperty mgrProp = clSO.FindProperty("manager");
        SerializedProperty idProp = clSO.FindProperty("canvasId");

        if (mgrProp != null) mgrProp.objectReferenceValue = mgr;

        // Generar ID unico: normalizar nombre + verificar colisiones
        string uniqueId = IdiomasEditorUtils.GenerateUniqueCanvasId(go.name, newCL);
        if (idProp != null) idProp.stringValue = uniqueId;

        clSO.ApplyModifiedProperties();

        // Registrar en el array canvasLocalizers del manager
        bool alreadyRegistered = false;
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            if (_canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue == newCL)
            {
                alreadyRegistered = true;
                break;
            }
        }
        if (!alreadyRegistered)
        {
            int idx = _canvasLocalizers.arraySize;
            _canvasLocalizers.arraySize = idx + 1;
            _canvasLocalizers.GetArrayElementAtIndex(idx).objectReferenceValue = newCL;
            serializedObject.ApplyModifiedProperties();
        }

        // Marcar resultado como ya localizado
        result.hasCanvasLocalizer = true;

        EditorUtility.SetDirty(go);

        Debug.Log($"[Idiomas] CanvasLocalizer anadido a \"{go.name}\" con canvasId=\"{uniqueId}\".");

        // Seleccionar el objeto para que el usuario vea el nuevo componente
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // =====================================================================
    // Gestion de claves JSON por CanvasLocalizer
    // =====================================================================

    /// <summary>
    /// Obtiene todas las claves (tmpKeys + legacyKeys) de un CanvasLocalizer.
    /// </summary>
    private List<string> GetCanvasLocalizerKeys(CanvasLocalizer cl)
    {
        List<string> keys = new List<string>();
        if (cl == null) return keys;

        SerializedObject clSO = new SerializedObject(cl);

        SerializedProperty tmpKeys = clSO.FindProperty("tmpKeys");
        if (tmpKeys != null)
        {
            for (int i = 0; i < tmpKeys.arraySize; i++)
            {
                string key = tmpKeys.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(key)) keys.Add(key);
            }
        }

        SerializedProperty legKeys = clSO.FindProperty("legacyKeys");
        if (legKeys != null)
        {
            for (int i = 0; i < legKeys.arraySize; i++)
            {
                string key = legKeys.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(key)) keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Cuenta cuantas claves de la lista existen en al menos un idioma del JSON cacheado.
    /// </summary>
    private int CountKeysInJson(List<string> keys)
    {
        if (_cachedData == null || _cachedLanguages == null || keys.Count == 0) return 0;

        int count = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = 0; j < _cachedLanguages.Length; j++)
            {
                if (_cachedData.TryGetValue(_cachedLanguages[j], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary &&
                    lt.DataDictionary.ContainsKey(keys[i]))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Elimina las claves de un CanvasLocalizer del archivo JSON de traducciones.
    /// </summary>
    private void RemoveKeysFromJson(CanvasLocalizer cl, List<string> keys, int keysInJson)
    {
        if (keys.Count == 0 || keysInJson == 0) return;

        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog("Sin Archivo",
                "No hay archivo de traducciones asignado.", "OK");
            return;
        }

        string canvasId = cl.GetCanvasId();
        if (!EditorUtility.DisplayDialog("Limpiar claves del JSON",
            $"Eliminar {keysInJson} clave(s) del canvas \"{canvasId}\" del archivo JSON?\n\n" +
            $"Se eliminaran de TODOS los idiomas.\n" +
            "Puedes deshacerlo con Ctrl+Z en el editor de texto, pero no en Unity.",
            "Eliminar", "Cancelar"))
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(ta);
        string fullPath = Path.GetFullPath(assetPath);
        string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);

        // Parsear JSON a diccionario editable
        var translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
        if (translations == null)
        {
            EditorUtility.DisplayDialog("Error", "No se pudo parsear el JSON.", "OK");
            return;
        }

        // Crear HashSet para busqueda rapida
        HashSet<string> keysToRemove = new HashSet<string>(keys);

        // Eliminar claves de todos los idiomas
        int totalRemoved = 0;
        foreach (var langPair in translations)
        {
            List<string> toRemove = new List<string>();
            foreach (string key in langPair.Value.Keys)
            {
                if (keysToRemove.Contains(key))
                    toRemove.Add(key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                langPair.Value.Remove(toRemove[i]);
                totalRemoved++;
            }
        }

        if (totalRemoved == 0)
        {
            EditorUtility.DisplayDialog("Sin cambios",
                "No se encontraron claves para eliminar.", "OK");
            return;
        }

        // Escribir JSON actualizado
        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        // Invalidar cache para que se recargue
        _cachedJsonHash = null;

        Debug.Log($"[Idiomas] Eliminadas {totalRemoved} entradas del JSON (canvas \"{canvasId}\").");
        EditorUtility.DisplayDialog("Listo",
            $"Se eliminaron {totalRemoved} entradas del JSON\n" +
            $"({keysInJson} claves x {totalRemoved / Mathf.Max(keysInJson, 1)} idiomas).",
            "OK");
    }

    // =====================================================================
    // Configuracion Rapida: Localizar Todo
    // =====================================================================

    private void QuickSetupAll(int candidateCount, int candidateTextCount)
    {
        string baseLang = IdiomasLanguages.Codes[_quickSetupLangIndex];
        string langName = IdiomasLanguages.GetNativeName(baseLang);

        if (!EditorUtility.DisplayDialog(
            "Configuracion Rapida: Localizar Todo",
            $"Se van a procesar {candidateCount} canvas con ~{candidateTextCount} textos.\n\n" +
            $"Idioma base: {langName} ({baseLang})\n\n" +
            "Para cada canvas se va a:\n" +
            "  1. Anadir el componente CanvasLocalizer\n" +
            "  2. Escanear todos los textos automaticamente\n" +
            "  3. Exportar las claves al archivo JSON\n\n" +
            "Puedes deshacer todo con Ctrl+Z.\n" +
            "Despues podras ajustar claves y exclusiones en cada canvas.",
            "Procesar Todo", "Cancelar"))
        {
            return;
        }

        LocalizationManager mgr = (LocalizationManager)target;

        // ============================================================
        // Preparar archivo JSON (leer o crear)
        // ============================================================
        TextAsset textAsset = _translationFile.objectReferenceValue as TextAsset;
        string assetPath;
        string fullPath;
        Dictionary<string, Dictionary<string, string>> translations;

        if (textAsset != null)
        {
            assetPath = AssetDatabase.GetAssetPath(textAsset);
            fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath, Encoding.UTF8);
                translations = IdiomasEditorUtils.ParseJsonToDictionary(json);
                if (translations == null)
                    translations = new Dictionary<string, Dictionary<string, string>>();
            }
            else
            {
                translations = new Dictionary<string, Dictionary<string, string>>();
            }
        }
        else
        {
            assetPath = "Assets/Idiomas_Data/translation.json";
            fullPath = Path.GetFullPath(assetPath);
            translations = new Dictionary<string, Dictionary<string, string>>();
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        if (!translations.ContainsKey(baseLang))
            translations[baseLang] = new Dictionary<string, string>();

        // ============================================================
        // Fase 1: Anadir CanvasLocalizer a TODOS los candidatos
        // (antes de escanear, para que IsUnderAny funcione correctamente
        //  cuando un canvas padre y su hijo son ambos candidatos)
        // ============================================================
        List<CanvasSearchResult> toProcess = new List<CanvasSearchResult>();
        List<CanvasLocalizer> newLocalizers = new List<CanvasLocalizer>();
        List<string> errors = new List<string>();

        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.hasCanvasLocalizer) continue;
            if (r.tmpCount + r.legacyCount == 0) continue;

            CanvasLocalizer newCL = UdonSharpUndo.AddComponent<CanvasLocalizer>(r.gameObject);
            if (newCL == null)
            {
                errors.Add($"No se pudo anadir CanvasLocalizer a '{r.gameObject.name}'");
                continue;
            }

            // Configurar: manager + canvasId
            SerializedObject clSO = new SerializedObject(newCL);
            SerializedProperty mgrProp = clSO.FindProperty("manager");
            SerializedProperty idProp = clSO.FindProperty("canvasId");

            if (mgrProp != null) mgrProp.objectReferenceValue = mgr;
            string uniqueId = IdiomasEditorUtils.GenerateUniqueCanvasId(r.gameObject.name, newCL);
            if (idProp != null) idProp.stringValue = uniqueId;
            clSO.ApplyModifiedProperties();

            toProcess.Add(r);
            newLocalizers.Add(newCL);
        }

        // ============================================================
        // Fase 2: Escanear y aplicar cada canvas
        // (ahora todos los CanvasLocalizer existen, IsUnderAny funciona)
        // ============================================================
        int totalTexts = 0;
        int processedCanvas = 0;

        for (int i = 0; i < toProcess.Count; i++)
        {
            CanvasSearchResult r = toProcess[i];
            CanvasLocalizer cl = newLocalizers[i];

            int textsConfigured = CanvasLocalizerEditor.QuickSetup(cl, baseLang, translations);

            if (textsConfigured < 0)
            {
                errors.Add($"Error al escanear '{r.gameObject.name}'");
            }
            else
            {
                totalTexts += textsConfigured;
                processedCanvas++;
                r.hasCanvasLocalizer = true;
            }

            EditorUtility.SetDirty(r.gameObject);
        }

        // ============================================================
        // Fase 3: Escribir JSON una sola vez (eficiente)
        // ============================================================
        if (totalTexts > 0)
        {
            string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
            File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        }

        AssetDatabase.Refresh();

        // Asignar JSON al manager si no tenia uno
        if (textAsset == null && totalTexts > 0)
        {
            TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (newAsset != null)
            {
                _translationFile.objectReferenceValue = newAsset;
                serializedObject.ApplyModifiedProperties();
            }
        }

        // ============================================================
        // Fase 4: Registrar todos los CanvasLocalizer con el manager
        // ============================================================
        SerializedObject mgrSO = new SerializedObject(mgr);
        SerializedProperty clArray = mgrSO.FindProperty("canvasLocalizers");

        // Construir set de ya registrados para busqueda rapida
        HashSet<Object> registered = new HashSet<Object>();
        for (int i = 0; i < clArray.arraySize; i++)
        {
            Object obj = clArray.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) registered.Add(obj);
        }

        // Agregar los nuevos
        for (int i = 0; i < newLocalizers.Count; i++)
        {
            if (newLocalizers[i] != null && !registered.Contains(newLocalizers[i]))
            {
                int idx = clArray.arraySize;
                clArray.arraySize = idx + 1;
                clArray.GetArrayElementAtIndex(idx).objectReferenceValue = newLocalizers[i];
            }
        }
        mgrSO.ApplyModifiedProperties();

        // Invalidar cache del JSON para que las estadisticas se actualicen
        _cachedJsonHash = null;

        // ============================================================
        // Mostrar resumen
        // ============================================================
        StringBuilder summary = new StringBuilder();
        summary.AppendLine($"Canvas procesados: {processedCanvas}/{candidateCount}");
        summary.AppendLine($"Textos configurados: {totalTexts}");
        summary.AppendLine($"Idioma base: {langName} ({baseLang})");
        summary.AppendLine($"Archivo: {assetPath}");

        if (errors.Count > 0)
        {
            summary.AppendLine($"\nErrores ({errors.Count}):");
            for (int i = 0; i < errors.Count; i++)
                summary.AppendLine($"  - {errors[i]}");
        }

        summary.AppendLine("\nPuedes ir a cada CanvasLocalizer para ajustar" +
            "\nclaves, exclusiones, o re-escanear si es necesario.");

        EditorUtility.DisplayDialog(
            "Configuracion Rapida Completada",
            summary.ToString(), "OK");

        Debug.Log($"[Idiomas] Configuracion Rapida: {processedCanvas} canvas, " +
            $"{totalTexts} textos. Idioma base: {baseLang}");
    }

    private void RefreshCache()
    {
        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null) { _cachedData = null; _cachedLanguages = null; _cachedKeys = null; _cachedJsonHash = null; return; }

        // Usar longitud + primeros/ultimos chars como hash rapido y determinístico
        string text = ta.text;
        string hash = text.Length + (text.Length > 0 ? "_" + text[0] + text[text.Length - 1] : "");
        if (hash == _cachedJsonHash) return;
        _cachedJsonHash = hash;

        if (VRCJson.TryDeserializeFromJson(ta.text, out DataToken d) &&
            d.TokenType == TokenType.DataDictionary)
        {
            _cachedData = d.DataDictionary;
            DataList k = _cachedData.GetKeys();
            _cachedLanguages = new string[k.Count];
            for (int i = 0; i < k.Count; i++) _cachedLanguages[i] = k[i].String;

            HashSet<string> all = new HashSet<string>();
            for (int i = 0; i < _cachedLanguages.Length; i++)
            {
                if (_cachedData.TryGetValue(_cachedLanguages[i], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary)
                {
                    DataList lk = lt.DataDictionary.GetKeys();
                    for (int j = 0; j < lk.Count; j++) all.Add(lk[j].String);
                }
            }
            _cachedKeys = new string[all.Count];
            all.CopyTo(_cachedKeys);
        }
    }

    // =====================================================================
    // Crear archivo JSON de traducciones
    // =====================================================================

    /// <summary>
    /// Ruta base donde se crean los archivos JSON de traducciones.
    /// Se usa Assets/ (no Packages/) porque los paquetes VPM son de solo lectura.
    /// </summary>
    private const string JSON_BASE_DIR = "Assets/Idiomas_Data";
    private const string JSON_BASE_NAME = "translation";

    private void CreateTranslationJsonFile()
    {
        // Asegurar que el directorio existe
        string fullDir = Path.GetFullPath(JSON_BASE_DIR);
        if (!Directory.Exists(fullDir))
        {
            Directory.CreateDirectory(fullDir);
        }

        // Buscar nombre disponible: translation.json, translation_2.json, translation_3.json...
        string assetPath = JSON_BASE_DIR + "/" + JSON_BASE_NAME + ".json";
        string fullPath = Path.GetFullPath(assetPath);

        if (File.Exists(fullPath))
        {
            int counter = 2;
            while (true)
            {
                assetPath = JSON_BASE_DIR + "/" + JSON_BASE_NAME + "_" + counter + ".json";
                fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) break;
                counter++;
            }
        }

        // Escribir JSON vacio
        File.WriteAllText(fullPath, "{}\n", Encoding.UTF8);
        AssetDatabase.Refresh();

        // Cargar el asset recien creado y asignarlo al campo
        TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        if (newAsset != null)
        {
            _translationFile.objectReferenceValue = newAsset;
            serializedObject.ApplyModifiedProperties();

            // Invalidar cache para que las estadisticas se actualicen
            _cachedJsonHash = null;

            Debug.Log($"[Idiomas] Archivo de traducciones creado: {assetPath}");
            EditorUtility.DisplayDialog("Archivo Creado",
                $"Se creo el archivo de traducciones:\n{assetPath}\n\n" +
                "Usa 'Escanear Escena' y 'Exportar al JSON' para llenarlo con tus textos.",
                "OK");
        }
        else
        {
            Debug.LogError($"[Idiomas] No se pudo cargar el archivo creado: {assetPath}");
        }
    }
}
