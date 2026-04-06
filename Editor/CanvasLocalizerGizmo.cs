using UnityEditor;
using UnityEngine;
using BenderDios.Idiomas;

/// <summary>
/// Dibuja un label en el Scene View para cada CanvasLocalizer,
/// mostrando su canvasId y el numero de textos gestionados.
/// Facilita identificar visualmente que canvas esta localizado.
/// </summary>
public static class CanvasLocalizerGizmo
{
    // GUIStyles cacheados para evitar crear nuevos cada frame
    private static GUIStyle _selectedStyle;
    private static GUIStyle _normalStyle;

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawGizmo(CanvasLocalizer localizer, GizmoType gizmoType)
    {
        if (localizer == null) return;

        string canvasId = localizer.GetCanvasId();
        int textCount = localizer.GetTextCount();

        if (string.IsNullOrEmpty(canvasId) && textCount == 0) return;

        string label = string.IsNullOrEmpty(canvasId)
            ? $"[Idiomas] {textCount} textos"
            : $"[Idiomas:{canvasId}] {textCount} textos";

        // Posicion del label: encima del GameObject
        Vector3 pos = localizer.transform.position;

        bool isSelected = (gizmoType & GizmoType.Selected) != 0;
        GUIStyle style = isSelected ? GetSelectedStyle() : GetNormalStyle();

        Handles.Label(pos, label, style);
    }

    private static GUIStyle GetSelectedStyle()
    {
        if (_selectedStyle == null)
        {
            _selectedStyle = new GUIStyle(EditorStyles.boldLabel);
            _selectedStyle.normal.textColor = new Color(0.3f, 0.85f, 1f);
            _selectedStyle.fontSize = 12;
            _selectedStyle.alignment = TextAnchor.MiddleCenter;
        }
        return _selectedStyle;
    }

    private static GUIStyle GetNormalStyle()
    {
        if (_normalStyle == null)
        {
            _normalStyle = new GUIStyle(EditorStyles.boldLabel);
            _normalStyle.normal.textColor = new Color(0.5f, 0.75f, 1f, 0.7f);
            _normalStyle.fontSize = 10;
            _normalStyle.alignment = TextAnchor.MiddleCenter;
        }
        return _normalStyle;
    }
}
