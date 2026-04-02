using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using System.Collections.Generic;
using System.IO;
using UnityEditorInternal;

/// <summary>
/// Herramienta de Editor para crear los componentes del sistema de Idiomas.
///
/// SOLUCION AL PROBLEMA DE UDONBEHAVIOUR:
/// En vez de crear un UdonSharpBehaviour nuevo (LanguageDropdown) que necesita
/// un UdonBehaviour que UdonSharp nunca crea desde AddComponent,
/// el dropdown se conecta directamente al UdonBehaviour del LocalizationManager,
/// que YA EXISTE porque se creo correctamente al compilar la escena.
///
/// El metodo OnLanguageDropdownChanged() vive en LocalizationManager.cs,
/// asi que no se necesita ningun UdonBehaviour nuevo.
/// </summary>
public static class IdiomasPrefabCreator
{
    // Colores
    private static readonly Color COL_BG      = new Color(0.12f, 0.12f, 0.15f, 0.95f);
    private static readonly Color COL_HEADER   = new Color(0.18f, 0.18f, 0.22f, 1f);
    private static readonly Color COL_BTN      = new Color(0.22f, 0.22f, 0.28f, 1f);
    private static readonly Color COL_ACCENT   = new Color(0.25f, 0.55f, 0.85f, 1f);
    private static readonly Color COL_TEXT     = new Color(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color COL_DIMMED   = new Color(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Color COL_SEP      = new Color(0.3f, 0.3f, 0.35f, 0.5f);
    private static readonly Color COL_SUCCESS  = new Color(0.2f, 0.7f, 0.3f, 1f);
    private static readonly Color COL_WARNING  = new Color(0.9f, 0.7f, 0.15f, 1f);
    private static readonly Color COL_SECTION  = new Color(0.4f, 0.65f, 1f, 1f);
    private static readonly Color COL_TOG_ON   = new Color(0.2f, 0.65f, 0.45f, 1f);
    private static readonly Color COL_TOG_OFF  = new Color(0.45f, 0.2f, 0.2f, 1f);

    // Idiomas centralizados en IdiomasLanguages.cs + entrada "Auto" para dropdown
    private static readonly string[,] LANGS = BuildLangsArray();

    private static string[,] BuildLangsArray()
    {
        int count = IdiomasLanguages.Codes.Length;
        string[,] result = new string[count + 1, 2];
        result[0, 0] = "";
        result[0, 1] = "Auto (Detect)";
        for (int i = 0; i < count; i++)
        {
            result[i + 1, 0] = IdiomasLanguages.Codes[i];
            result[i + 1, 1] = IdiomasLanguages.LatinNames[i];
        }
        return result;
    }

    // =====================================================================
    // Menu: Crear Selector de Idioma
    // =====================================================================

    [MenuItem("GameObject/Idiomas/Crear Selector de Idioma", false, 10)]
    public static void CreateLanguageSelector()
    {
        // Buscar o crear LocalizationManager
        LocalizationManager manager = Object.FindObjectOfType<LocalizationManager>();
        bool createdManager = false;

        if (manager == null)
        {
            GameObject mgrGO = new GameObject("LocalizationManager");
            Undo.RegisterCreatedObjectUndo(mgrGO, "Crear LocalizationManager");
            manager = mgrGO.AddComponent<LocalizationManager>();
            TextAsset tf = FindTranslationFile();
            if (tf != null) SetField(manager, "translationFile", tf);
            createdManager = true;
        }

        // Crear canvas del selector
        GameObject selectorCanvas = CreateSelectorCanvas(manager);
        Undo.RegisterCreatedObjectUndo(selectorCanvas, "Crear Selector de Idioma");
        Selection.activeGameObject = selectorCanvas;

        // Cablear OnValueChanged al UdonBehaviour del LocalizationManager
        // (que ya existe porque manager fue creado por AddComponent + compilacion)
        _pendingManager = manager;
        _pendingDropdownCanvas = selectorCanvas;
        _wireAttempts = 0;
        EditorApplication.update += TryWireDropdown;

        string msg = createdManager
            ? "[Idiomas] LocalizationManager + Selector creados. Cableando dropdown..."
            : "[Idiomas] Selector creado. Cableando dropdown al LocalizationManager existente...";
        Debug.Log(msg);
    }

    // =====================================================================
    // Menu: Crear Canvas de Ejemplo
    // =====================================================================

    [MenuItem("GameObject/Idiomas/Crear Canvas de Ejemplo", false, 11)]
    public static void CreateDemoExample()
    {
        LocalizationManager manager = Object.FindObjectOfType<LocalizationManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Sin Manager",
                "No hay LocalizationManager en la escena.\n" +
                "Usa primero 'Crear Selector de Idioma'.", "OK");
            return;
        }

        GameObject demo = CreateDemoCanvas(manager);
        Undo.RegisterCreatedObjectUndo(demo, "Crear Canvas de Ejemplo");
        Selection.activeGameObject = demo;

        Debug.Log("[Idiomas] Canvas de ejemplo creado.\n" +
                  "Seleccionalo > Inspector > Escanear Canvas > Exportar al JSON y Aplicar.");
    }

    // =====================================================================
    // Menu: Solo Manager
    // =====================================================================

    [MenuItem("GameObject/Idiomas/Crear LocalizationManager (sin UI)", false, 20)]
    public static void CreateManagerOnly()
    {
        GameObject go = new GameObject("LocalizationManager");
        Undo.RegisterCreatedObjectUndo(go, "Crear LocalizationManager");
        LocalizationManager mgr = go.AddComponent<LocalizationManager>();
        TextAsset tf = FindTranslationFile();
        if (tf != null) SetField(mgr, "translationFile", tf);
        Selection.activeGameObject = go;
        Debug.Log("[Idiomas] LocalizationManager creado.");
    }

    // =====================================================================
    // Cableado del dropdown → LocalizationManager.OnLanguageDropdownChanged
    // =====================================================================

    private static LocalizationManager _pendingManager;
    private static GameObject _pendingDropdownCanvas;
    private static int _wireAttempts;

    private static void TryWireDropdown()
    {
        _wireAttempts++;

        if (_pendingManager == null || _pendingDropdownCanvas == null)
        {
            EditorApplication.update -= TryWireDropdown;
            return;
        }

        if (_wireAttempts > 600)
        {
            EditorApplication.update -= TryWireDropdown;
            Debug.LogError("[Idiomas] Timeout buscando UdonBehaviour del LocalizationManager.\n" +
                "Selecciona el LocalizationManager en la jerarquia, luego usa 'Conectar Dropdown'.");
            return;
        }

        // En el frame 5, forzar que UdonSharp procese el componente
        // seleccionando el objeto y marcandolo como dirty.
        // UdonSharp crea el UdonBehaviour cuando el Inspector lo dibuja.
        if (_wireAttempts == 5)
        {
            Selection.activeGameObject = _pendingManager.gameObject;
            EditorUtility.SetDirty(_pendingManager);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // Buscar el UdonBehaviour del LocalizationManager
        UdonBehaviour mgrUdon = FindUdonBehaviourFor(_pendingManager);
        if (mgrUdon == null) return; // Reintentar

        // Encontrado!
        EditorApplication.update -= TryWireDropdown;

        TMP_Dropdown dropdown = _pendingDropdownCanvas.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown == null)
        {
            Debug.LogError("[Idiomas] TMP_Dropdown no encontrado en el selector.");
            return;
        }

        // Cablear: Dropdown.OnValueChanged → mgrUdon.SendCustomEvent("OnLanguageDropdownChanged")
        WireEvent(dropdown, mgrUdon, "OnLanguageDropdownChanged");

        // Configurar referencia al dropdown y codigos en el manager
        SerializedObject mgrSO = new SerializedObject(_pendingManager);
        SerializedProperty ddProp = mgrSO.FindProperty("_languageDropdown");
        if (ddProp != null) ddProp.objectReferenceValue = dropdown;

        int langCount = LANGS.GetLength(0);
        SerializedProperty codesProp = mgrSO.FindProperty("_dropdownLanguageCodes");
        if (codesProp != null)
        {
            codesProp.arraySize = langCount;
            for (int i = 0; i < langCount; i++)
                codesProp.GetArrayElementAtIndex(i).stringValue = LANGS[i, 0];
        }
        mgrSO.ApplyModifiedProperties();

        Debug.Log($"[Idiomas] Dropdown conectado al LocalizationManager (intento {_wireAttempts}). Listo para Play!");

        _pendingManager = null;
        _pendingDropdownCanvas = null;
    }

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

    private static void WireEvent(TMP_Dropdown dropdown,
        UdonBehaviour udon, string eventName)
    {
        SerializedObject ddSO = new SerializedObject(dropdown);
        SerializedProperty onVal = ddSO.FindProperty("m_OnValueChanged");
        SerializedProperty calls = onVal.FindPropertyRelative("m_PersistentCalls.m_Calls");

        calls.ClearArray();
        calls.arraySize = 1;
        SerializedProperty entry = calls.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("m_Target").objectReferenceValue = udon;
        entry.FindPropertyRelative("m_MethodName").stringValue = "SendCustomEvent";
        entry.FindPropertyRelative("m_Mode").intValue = 5;
        entry.FindPropertyRelative("m_Arguments")
            .FindPropertyRelative("m_StringArgument").stringValue = eventName;
        entry.FindPropertyRelative("m_CallState").intValue = 2;
        ddSO.ApplyModifiedProperties();
    }

    // =====================================================================
    // Crear canvas selector con dropdown
    // =====================================================================

    private static GameObject CreateSelectorCanvas(LocalizationManager manager)
    {
        GameObject canvas = CreateWorldCanvas(null, "LanguageSelector",
            new Vector2(300, 140), Vector3.zero);
        GameObject bg = MakePanel(canvas.transform, "Bg", COL_BG,
            Vector2.zero, new Vector2(300, 140));

        GameObject header = MakePanel(bg.transform, "Header", COL_HEADER,
            new Vector2(0, 46), new Vector2(280, 36));
        MakeTMP(header.transform, "Title", "Language",
            Vector2.zero, new Vector2(260, 30), COL_TEXT, 16f,
            TextAlignmentOptions.Center);

        CreateTMPDropdown(bg.transform, "Dropdown",
            new Vector2(0, -2), new Vector2(260, 38));

        MakeTMP(bg.transform, "Info", "Select your language",
            new Vector2(0, -40), new Vector2(260, 18), COL_DIMMED, 9f,
            TextAlignmentOptions.Center);

        return canvas;
    }

    private static GameObject CreateTMPDropdown(Transform parent, string name,
        Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        Image bgImg = go.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        // Label
        TextMeshProUGUI labelTMP = MakeTMP(go.transform, "Label", LANGS[0, 1],
            Vector2.zero, Vector2.zero, COL_TEXT, 14f, TextAlignmentOptions.MidlineLeft);
        RectTransform lrt = labelTMP.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(10, 2); lrt.offsetMax = new Vector2(-30, -2);

        // Arrow
        TextMeshProUGUI arrowTMP = MakeTMP(go.transform, "Arrow", "v",
            Vector2.zero, Vector2.zero, COL_DIMMED, 14f, TextAlignmentOptions.Center);
        RectTransform art = arrowTMP.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(1, 0); art.anchorMax = new Vector2(1, 1);
        art.offsetMin = new Vector2(-28, 2); art.offsetMax = new Vector2(-8, -2);

        // Template
        GameObject templateGO = new GameObject("Template");
        templateGO.transform.SetParent(go.transform, false);
        RectTransform trt = templateGO.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0);
        trt.pivot = new Vector2(0.5f, 1f); trt.sizeDelta = new Vector2(0, 200);
        templateGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 0.98f);
        ScrollRect scroll = templateGO.AddComponent<ScrollRect>();
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // Viewport
        GameObject vpGO = new GameObject("Viewport");
        vpGO.transform.SetParent(templateGO.transform, false);
        RectTransform vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = Vector2.zero; vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<Image>().color = new Color(1, 1, 1, 0.003f);
        vpGO.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = vprt;

        // Content
        GameObject cGO = new GameObject("Content");
        cGO.transform.SetParent(vpGO.transform, false);
        RectTransform crt = cGO.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1f); crt.sizeDelta = Vector2.zero;
        scroll.content = crt;

        // Item
        GameObject itemGO = new GameObject("Item");
        itemGO.transform.SetParent(cGO.transform, false);
        RectTransform irt = itemGO.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f); irt.anchorMax = new Vector2(1, 0.5f);
        irt.sizeDelta = new Vector2(0, 30);
        Toggle tog = itemGO.AddComponent<Toggle>();
        Image itemBg = itemGO.AddComponent<Image>();
        itemBg.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        tog.targetGraphic = itemBg;

        GameObject chkGO = new GameObject("Item Checkmark");
        chkGO.transform.SetParent(itemGO.transform, false);
        RectTransform chkrt = chkGO.AddComponent<RectTransform>();
        chkrt.anchorMin = Vector2.zero; chkrt.anchorMax = Vector2.one;
        chkrt.offsetMin = Vector2.zero; chkrt.offsetMax = Vector2.zero;
        Image chkImg = chkGO.AddComponent<Image>();
        chkImg.color = new Color(0.25f, 0.55f, 0.85f, 0.3f);
        tog.graphic = chkImg;

        TextMeshProUGUI itemLabelTMP = MakeTMP(itemGO.transform, "Item Label", "Option",
            Vector2.zero, Vector2.zero, COL_TEXT, 12f, TextAlignmentOptions.MidlineLeft);
        RectTransform ilrt = itemLabelTMP.GetComponent<RectTransform>();
        ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
        ilrt.offsetMin = new Vector2(10, 2); ilrt.offsetMax = new Vector2(-10, -2);

        // TMP_Dropdown
        TMP_Dropdown dd = go.AddComponent<TMP_Dropdown>();
        dd.template = trt; dd.captionText = labelTMP; dd.itemText = itemLabelTMP;
        dd.targetGraphic = bgImg;
        ColorBlock c = dd.colors;
        c.normalColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        c.highlightedColor = new Color(0.28f, 0.28f, 0.35f, 1f);
        c.pressedColor = COL_ACCENT;
        c.selectedColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        dd.colors = c;
        Navigation nav = dd.navigation; nav.mode = Navigation.Mode.None; dd.navigation = nav;

        dd.ClearOptions();
        List<string> opts = new List<string>();
        for (int i = 0; i < LANGS.GetLength(0); i++) opts.Add(LANGS[i, 1]);
        dd.AddOptions(opts);
        dd.value = 0; dd.RefreshShownValue();
        templateGO.SetActive(false);

        return go;
    }

    // =====================================================================
    // Canvas demo
    // =====================================================================

    private static GameObject CreateDemoCanvas(LocalizationManager manager)
    {
        GameObject canvas = CreateWorldCanvas(null, "Canvas_Demo",
            new Vector2(480, 620), Vector3.zero);
        GameObject bg = MakePanel(canvas.transform, "Bg", COL_BG,
            Vector2.zero, new Vector2(480, 620));
        Transform p = bg.transform;
        float y = 288f;

        GameObject hdr = MakePanel(p, "Header", COL_HEADER, new Vector2(0, y), new Vector2(460, 48));
        MakeTMP(hdr.transform, "Title", "World Settings", new Vector2(-10, 0), new Vector2(300, 38), COL_TEXT, 22f, TextAlignmentOptions.MidlineLeft);
        MakeTMP(hdr.transform, "Version", "v1.2.0", new Vector2(10, 0), new Vector2(100, 24), COL_DIMMED, 11f, TextAlignmentOptions.MidlineRight);
        y -= 42f;

        Sec(p, "SectionGeneral", "General", ref y);
        Row(p, "RowLanguage", "Language", "English", ref y);
        Row(p, "RowRegion", "Region", "North America", ref y);
        Tog(p, "ToggleNotifications", "Notifications", true, ref y);
        Tog(p, "ToggleWelcome", "Show Welcome Message", true, ref y);
        y -= 4f;
        MakeTMP(p, "DescWelcome", "This message is shown to every player when they join the world.",
            new Vector2(0, y), new Vector2(420, 20), COL_DIMMED, 10f, TextAlignmentOptions.MidlineLeft);
        y -= 22f; Sep(p, "Sep1", ref y);

        Sec(p, "SectionAudio", "Audio", ref y);
        Row(p, "RowVolume", "Master Volume", "80%", ref y);
        Tog(p, "ToggleMusic", "Background Music", true, ref y);
        Tog(p, "ToggleSFX", "Sound Effects", false, ref y);
        Sep(p, "Sep2", ref y);

        Sec(p, "SectionDisplay", "Display", ref y);
        Row(p, "RowBrightness", "Brightness", "100%", ref y);
        Row(p, "RowQuality", "Mirror Quality", "High", ref y);
        Tog(p, "ToggleNames", "Show Player Names", true, ref y);
        Sep(p, "Sep3", ref y);

        Sec(p, "SectionPermissions", "Permissions", ref y);
        y -= 2f;
        GameObject wb = MakePanel(p, "WarningBox", new Color(0.9f, 0.7f, 0.15f, 0.12f), new Vector2(0, y), new Vector2(440, 36));
        MakeTMP(wb.transform, "WarningText", "Only the world owner can modify these settings.",
            Vector2.zero, new Vector2(420, 30), COL_WARNING, 11f, TextAlignmentOptions.MidlineLeft);
        y -= 40f; Sep(p, "Sep4", ref y);

        y -= 4f;
        BtnV(p, "BtnSave", "Save Changes", new Vector2(-140, y), COL_ACCENT);
        BtnV(p, "BtnReset", "Reset", new Vector2(0, y), COL_BTN);
        BtnV(p, "BtnClose", "Close", new Vector2(140, y), COL_BTN);
        y -= 44f;

        GameObject sb = MakePanel(p, "StatusBar", new Color(0.1f, 0.1f, 0.12f, 0.9f), new Vector2(0, y), new Vector2(460, 28));
        MakeTMP(sb.transform, "StatusText", "All changes saved successfully.", new Vector2(8, 0), new Vector2(300, 22), COL_SUCCESS, 10f, TextAlignmentOptions.MidlineLeft);
        MakeTMP(sb.transform, "PlayerCount", "Players: 12", new Vector2(-8, 0), new Vector2(120, 22), COL_DIMMED, 10f, TextAlignmentOptions.MidlineRight);
        y -= 28f;
        MakeTMP(p, "Tooltip", "Hover over any option for more details.", new Vector2(0, y - 6f), new Vector2(420, 18), COL_DIMMED, 9f, TextAlignmentOptions.Midline);

        CanvasLocalizer cl = canvas.AddComponent<CanvasLocalizer>();
        SerializedObject clSO = new SerializedObject(cl);
        SetPropObj(clSO, "manager", manager);
        SetPropStr(clSO, "canvasId", "demo");
        SetPropStr(clSO, "baseLanguage", "en");
        clSO.ApplyModifiedProperties();

        SerializedObject mgrSO = new SerializedObject(manager);
        SerializedProperty arr = mgrSO.FindProperty("canvasLocalizers");
        if (arr != null) { arr.arraySize++; arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = cl; }
        mgrSO.ApplyModifiedProperties();

        return canvas;
    }

    // Demo helpers
    private static void Sec(Transform p, string n, string t, ref float y) { MakeTMP(p, n, t, new Vector2(-110, y), new Vector2(220, 22), COL_SECTION, 13f, TextAlignmentOptions.MidlineLeft); y -= 26f; }
    private static void Row(Transform p, string n, string l, string v, ref float y) {
        GameObject r = new GameObject(n); r.transform.SetParent(p, false);
        r.AddComponent<RectTransform>().anchoredPosition = new Vector2(0, y); r.GetComponent<RectTransform>().sizeDelta = new Vector2(440, 26);
        MakeTMP(r.transform, "Label", l, new Vector2(-60, 0), new Vector2(220, 22), COL_TEXT, 12f, TextAlignmentOptions.MidlineLeft);
        MakeTMP(r.transform, "Value", v, new Vector2(60, 0), new Vector2(200, 22), COL_DIMMED, 12f, TextAlignmentOptions.MidlineRight); y -= 28f; }
    private static void Tog(Transform p, string n, string l, bool on, ref float y) {
        GameObject r = new GameObject(n); r.transform.SetParent(p, false);
        r.AddComponent<RectTransform>().anchoredPosition = new Vector2(0, y); r.GetComponent<RectTransform>().sizeDelta = new Vector2(440, 26);
        MakeTMP(r.transform, "Label", l, new Vector2(-60, 0), new Vector2(220, 22), COL_TEXT, 12f, TextAlignmentOptions.MidlineLeft);
        GameObject tb = MakePanel(r.transform, "ToggleBg", on ? COL_TOG_ON : COL_TOG_OFF, new Vector2(160, 0), new Vector2(48, 22));
        MakeTMP(tb.transform, "ToggleText", on ? "On" : "Off", Vector2.zero, new Vector2(44, 20), COL_TEXT, 11f, TextAlignmentOptions.Center); y -= 28f; }
    private static void BtnV(Transform p, string n, string l, Vector2 pos, Color c) {
        GameObject g = MakePanel(p, n, c, pos, new Vector2(120, 36));
        MakeTMP(g.transform, "Label", l, Vector2.zero, new Vector2(112, 32), COL_TEXT, 13f, TextAlignmentOptions.Center); }
    private static void Sep(Transform p, string n, ref float y) { y -= 6f; MakePanel(p, n, COL_SEP, new Vector2(0, y), new Vector2(430, 1)); y -= 10f; }

    // =====================================================================
    // Base helpers
    // =====================================================================

    private static GameObject MakePanel(Transform parent, string name, Color color, Vector2 pos, Vector2 size) {
        GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
        RectTransform r = go.AddComponent<RectTransform>(); r.anchoredPosition = pos; r.sizeDelta = size;
        go.AddComponent<Image>().color = color; return go; }

    private static TextMeshProUGUI MakeTMP(Transform parent, string name, string text,
        Vector2 pos, Vector2 size, Color color, float fs, TextAlignmentOptions align) {
        GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
        RectTransform r = go.AddComponent<RectTransform>(); r.anchoredPosition = pos; r.sizeDelta = size;
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.color = color; tmp.fontSize = fs; tmp.alignment = align;
        tmp.enableWordWrapping = true; tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false; return tmp; }

    private static GameObject CreateWorldCanvas(Transform parent, string name, Vector2 size, Vector3 pos) {
        GameObject go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.layer = LayerMask.NameToLayer("Default");
        go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();
        System.Type uiShape = FindType("VRC.SDK3.Components.VRCUiShape");
        if (uiShape != null) go.AddComponent(uiShape);
        RectTransform r = go.GetComponent<RectTransform>();
        r.sizeDelta = size; r.localScale = Vector3.one * 0.001f; return go; }

    private static void SetField(Object target, string field, Object value) {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.objectReferenceValue = value; so.ApplyModifiedProperties(); } }
    private static void SetPropObj(SerializedObject so, string f, Object v) {
        SerializedProperty p = so.FindProperty(f); if (p != null) p.objectReferenceValue = v; }
    private static void SetPropStr(SerializedObject so, string f, string v) {
        SerializedProperty p = so.FindProperty(f); if (p != null) p.stringValue = v; }

    private static TextAsset FindTranslationFile() {
        string[] g = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Idiomas/Data" });
        for (int i = 0; i < g.Length; i++) { string p = AssetDatabase.GUIDToAssetPath(g[i]);
            if (p.EndsWith(".json")) return AssetDatabase.LoadAssetAtPath<TextAsset>(p); } return null; }

    private static System.Type FindType(string fullName) {
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
            System.Type t = a.GetType(fullName); if (t != null) return t; } return null; }
}
