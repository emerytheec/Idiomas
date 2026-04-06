using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using System.Collections.Generic;
using BenderDios.Idiomas;

/// <summary>
/// Herramienta de Editor para crear componentes de demostracion del sistema de Idiomas.
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

    // =====================================================================
    // Menu: Crear Canvas de Ejemplo
    // =====================================================================

    [MenuItem("Tools/Idiomas/Crear Canvas de Ejemplo", false, 10)]
    public static void CreateDemoExample()
    {
        LocalizationManager manager = Object.FindFirstObjectByType<LocalizationManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Sin Manager",
                "No hay LocalizationManager en la escena.\n" +
                "Arrastra el prefab LocalizationManager a la escena primero.", "OK");
            return;
        }

        GameObject demo = CreateDemoCanvas(manager);
        Undo.RegisterCreatedObjectUndo(demo, "Crear Canvas de Ejemplo");
        Selection.activeGameObject = demo;

        Debug.Log("[Idiomas] Canvas de ejemplo creado.\n" +
                  "Seleccionalo > Inspector > Escanear Canvas > Exportar al JSON y Aplicar.");
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

        CanvasLocalizer cl = UdonSharpUndo.AddComponent<CanvasLocalizer>(canvas);
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

    private static void SetPropObj(SerializedObject so, string f, Object v) {
        SerializedProperty p = so.FindProperty(f); if (p != null) p.objectReferenceValue = v; }
    private static void SetPropStr(SerializedObject so, string f, string v) {
        SerializedProperty p = so.FindProperty(f); if (p != null) p.stringValue = v; }

    private static System.Type FindType(string fullName) {
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
            System.Type t = a.GetType(fullName); if (t != null) return t; } return null; }
}
