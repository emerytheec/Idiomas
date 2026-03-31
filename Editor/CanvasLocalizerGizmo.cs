using UnityEditor;
using UnityEngine;

/// <summary>
/// Dibuja un label en el Scene View para cada CanvasLocalizer,
/// mostrando su canvasId y el numero de textos gestionados.
/// Facilita identificar visualmente que canvas esta localizado.
/// </summary>
public static class CanvasLocalizerGizmo
{
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

        // Color segun estado
        bool isSelected = (gizmoType & GizmoType.Selected) != 0;
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = isSelected
            ? new Color(0.3f, 0.85f, 1f)
            : new Color(0.5f, 0.75f, 1f, 0.7f);
        style.fontSize = isSelected ? 12 : 10;
        style.alignment = TextAnchor.MiddleCenter;

        Handles.Label(pos, label, style);
    }
}
