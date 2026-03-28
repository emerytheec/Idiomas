using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using System.Collections.Generic;
using VRC.SDK3.Data;
using VRC.Udon;

/// <summary>
/// Inspector simplificado para LocalizationManager.
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

    // Estado del editor
    private bool _showLocalizers = false;
    private bool _showValidation = false;
    private bool _showPreview = false;
    private string _previewLanguage = "en";
    private Vector2 _previewScroll;
    private Vector2 _validationScroll;

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
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // === TITULO ===
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Localization Manager", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        // === CONFIGURACION BASICA ===
        EditorGUILayout.PropertyField(_translationFile,
            new GUIContent("Archivo de Traducciones"));
        EditorGUILayout.PropertyField(_fallbackLanguage,
            new GUIContent("Idioma de Fallback"));

        RefreshCache();

        // === DROPDOWN DE IDIOMA ===
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Selector de Idioma (Dropdown)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_languageDropdown,
            new GUIContent("Dropdown",
                "Arrastra aqui el TMP_Dropdown del selector de idioma."));
        EditorGUILayout.PropertyField(_dropdownLanguageCodes,
            new GUIContent("Codigos de Idioma",
                "Misma cantidad y orden que las opciones del dropdown.\n" +
                "Dejar vacio = auto-detectar."), true);

        // Boton para cablear OnValueChanged
        if (_languageDropdown.objectReferenceValue != null)
        {
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
                        "El dropdown no esta conectado. Pulsa el boton para conectarlo automaticamente.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Dropdown conectado.", MessageType.Info);
                }
            }
        }

        // === ESTADISTICAS ===
        EditorGUILayout.Space(8);
        if (_cachedLanguages != null && _cachedLanguages.Length > 0)
        {
            EditorGUILayout.LabelField(
                $"Idiomas: {_cachedLanguages.Length} ({string.Join(", ", _cachedLanguages)})  |  " +
                $"Claves: {(_cachedKeys != null ? _cachedKeys.Length : 0)}  |  " +
                $"Canvas: {_canvasLocalizers.arraySize}  |  " +
                $"Textos: {_localizers.arraySize}",
                EditorStyles.helpBox);
        }

        // === AUTO-TRADUCIR ===
        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("Auto-Traducir Idiomas Faltantes", GUILayout.Height(26)))
        {
            OpenAutoTranslateWindow();
        }
        GUI.backgroundColor = Color.white;

        // === LOCALIZERS (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showLocalizers = EditorGUILayout.Foldout(_showLocalizers,
            $"Localizers ({_localizers.arraySize} textos, {_canvasLocalizers.arraySize} canvas)",
            true, EditorStyles.foldoutHeader);
        if (_showLocalizers)
        {
            EditorGUI.indentLevel++;
            if (GUILayout.Button("Auto-buscar todo en la escena"))
            {
                AutoFindAll();
            }
            EditorGUILayout.PropertyField(_localizers,
                new GUIContent($"TextLocalizers ({_localizers.arraySize})"), true);
            EditorGUILayout.PropertyField(_canvasLocalizers,
                new GUIContent($"CanvasLocalizers ({_canvasLocalizers.arraySize})"), true);
            EditorGUI.indentLevel--;
        }

        // === VALIDACION ===
        EditorGUILayout.Space(3);
        _showValidation = EditorGUILayout.Foldout(_showValidation,
            "Validacion de Claves", true, EditorStyles.foldoutHeader);
        if (_showValidation)
        {
            EditorGUI.indentLevel++;
            DrawValidation();
            EditorGUI.indentLevel--;
        }

        // === VISTA PREVIA ===
        EditorGUILayout.Space(3);
        _showPreview = EditorGUILayout.Foldout(_showPreview,
            "Vista Previa de Traducciones", true, EditorStyles.foldoutHeader);
        if (_showPreview)
        {
            EditorGUI.indentLevel++;
            DrawPreview();
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
        UdonBehaviour mgrUdon = FindUdonBehaviourFor(mgr);

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

    private void AutoFindAll()
    {
        LocalizationManager mgr = (LocalizationManager)target;

        // TextLocalizers
        TextLocalizer[] allTL = FindObjectsOfType<TextLocalizer>();
        List<TextLocalizer> matchTL = new List<TextLocalizer>();
        for (int i = 0; i < allTL.Length; i++)
        {
            LocalizationManager tlMgr = allTL[i].GetManager();
            if (tlMgr == mgr || tlMgr == null) matchTL.Add(allTL[i]);
        }
        _localizers.arraySize = matchTL.Count;
        for (int i = 0; i < matchTL.Count; i++)
            _localizers.GetArrayElementAtIndex(i).objectReferenceValue = matchTL[i];

        // CanvasLocalizers
        CanvasLocalizer[] allCL = FindObjectsOfType<CanvasLocalizer>();
        List<CanvasLocalizer> matchCL = new List<CanvasLocalizer>();
        for (int i = 0; i < allCL.Length; i++)
        {
            LocalizationManager clMgr = allCL[i].GetManager();
            if (clMgr == mgr || clMgr == null) matchCL.Add(allCL[i]);
        }
        _canvasLocalizers.arraySize = matchCL.Count;
        for (int i = 0; i < matchCL.Count; i++)
            _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue = matchCL[i];

        serializedObject.ApplyModifiedProperties();
        Debug.Log($"[Idiomas] Encontrados: {matchTL.Count} TextLocalizer(s), {matchCL.Count} CanvasLocalizer(s).");
    }

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
    // Validacion
    // =====================================================================

    private void DrawValidation()
    {
        if (_cachedData == null)
        {
            EditorGUILayout.HelpBox("Asigna un archivo JSON.", MessageType.Info);
            return;
        }

        _validationScroll = EditorGUILayout.BeginScrollView(_validationScroll, GUILayout.MaxHeight(200));
        int warnings = 0;

        // TextLocalizers
        for (int i = 0; i < _localizers.arraySize; i++)
        {
            Object obj = _localizers.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj == null) continue;
            TextLocalizer tl = obj as TextLocalizer;
            if (tl == null) continue;
            string key = tl.GetTranslationKey();
            if (string.IsNullOrEmpty(key)) continue;
            CheckKey(tl.gameObject.name, key, ref warnings);
        }

        // CanvasLocalizers
        for (int c = 0; c < _canvasLocalizers.arraySize; c++)
        {
            Object clObj = _canvasLocalizers.GetArrayElementAtIndex(c).objectReferenceValue;
            if (clObj == null) continue;
            SerializedObject clSO = new SerializedObject(clObj);
            string clName = ((CanvasLocalizer)clObj).gameObject.name;

            SerializedProperty tmpKeys = clSO.FindProperty("tmpKeys");
            if (tmpKeys != null)
                for (int k = 0; k < tmpKeys.arraySize; k++)
                {
                    string key = tmpKeys.GetArrayElementAtIndex(k).stringValue;
                    if (!string.IsNullOrEmpty(key)) CheckKey(clName, key, ref warnings);
                }

            SerializedProperty legKeys = clSO.FindProperty("legacyKeys");
            if (legKeys != null)
                for (int k = 0; k < legKeys.arraySize; k++)
                {
                    string key = legKeys.GetArrayElementAtIndex(k).stringValue;
                    if (!string.IsNullOrEmpty(key)) CheckKey(clName, key, ref warnings);
                }
        }

        if (warnings == 0)
            EditorGUILayout.HelpBox("Todas las claves existen en todos los idiomas.", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void CheckKey(string owner, string key, ref int warnings)
    {
        for (int j = 0; j < _cachedLanguages.Length; j++)
        {
            if (_cachedData.TryGetValue(_cachedLanguages[j], out DataToken lt) &&
                lt.TokenType == TokenType.DataDictionary &&
                !lt.DataDictionary.ContainsKey(key))
            {
                EditorGUILayout.HelpBox(
                    $"'{owner}': '{key}' falta en '{_cachedLanguages[j]}'",
                    MessageType.Warning);
                warnings++;
            }
        }
    }

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
        _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MaxHeight(300));

        if (_cachedData.TryGetValue(_previewLanguage, out DataToken lt) &&
            lt.TokenType == TokenType.DataDictionary)
        {
            DataDictionary ld = lt.DataDictionary;
            DataList keys = ld.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(keys[i].String, GUILayout.Width(200));
                EditorGUILayout.LabelField(ld[keys[i].String].String, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    private static UdonBehaviour FindUdonBehaviourFor(UdonSharpBehaviour proxy)
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

    private void RefreshCache()
    {
        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null) { _cachedData = null; _cachedLanguages = null; _cachedKeys = null; return; }

        string hash = ta.text.Length.ToString();
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
}
