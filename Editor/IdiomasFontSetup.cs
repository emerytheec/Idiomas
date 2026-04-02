using UnityEditor;
using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Utilidad para configurar automaticamente las fallback fonts de TextMeshPro.
/// Agrega NotoSansJP SDF y NotoSansKR SDF a TMP_Settings.fallbackFontAssets
/// para soportar caracteres CJK (chino, japones, coreano) y cirilico (ruso).
/// </summary>
public static class IdiomasFontSetup
{
    // Nombres de las fonts que buscamos en el proyecto
    private static readonly string[] FONT_NAMES = {
        "NotoSansJP SDF",
        "NotoSansKR SDF"
    };

    /// <summary>
    /// Verifica si las fallback fonts de Idiomas estan configuradas en TMP Settings.
    /// </summary>
    public static bool AreFontsConfigured()
    {
        TMP_Settings settings = GetTMPSettings();
        if (settings == null) return false;

        List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
        if (fallbacks == null) return false;

        for (int i = 0; i < FONT_NAMES.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < fallbacks.Count; j++)
            {
                if (fallbacks[j] != null && fallbacks[j].name == FONT_NAMES[i])
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }

        return true;
    }

    /// <summary>
    /// Agrega las fallback fonts de Idiomas al TMP Settings.
    /// Devuelve el numero de fonts agregadas (0 si ya estaban todas).
    /// </summary>
    public static int SetupFonts()
    {
        TMP_Settings settings = GetTMPSettings();
        if (settings == null)
        {
            Debug.LogWarning("[Idiomas] No se encontro TMP Settings. " +
                "Abre Window > TextMeshPro > Import TMP Essential Resources primero.");
            return -1;
        }

        // Acceder via SerializedObject para Undo y persistencia correcta
        SerializedObject so = new SerializedObject(settings);
        SerializedProperty fallbackProp = so.FindProperty("m_fallbackFontAssets");

        if (fallbackProp == null)
        {
            Debug.LogWarning("[Idiomas] No se encontro la propiedad m_fallbackFontAssets en TMP Settings.");
            return -1;
        }

        // Buscar las fonts en el proyecto
        TMP_FontAsset[] fontsToAdd = FindFonts();
        if (fontsToAdd.Length == 0)
        {
            Debug.LogWarning("[Idiomas] No se encontraron NotoSansJP SDF / NotoSansKR SDF en el proyecto.");
            return -1;
        }

        // Verificar cuales ya estan en la lista
        int added = 0;
        for (int f = 0; f < fontsToAdd.Length; f++)
        {
            TMP_FontAsset font = fontsToAdd[f];
            bool alreadyExists = false;

            for (int i = 0; i < fallbackProp.arraySize; i++)
            {
                if (fallbackProp.GetArrayElementAtIndex(i).objectReferenceValue == font)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists)
            {
                int idx = fallbackProp.arraySize;
                fallbackProp.arraySize = idx + 1;
                fallbackProp.GetArrayElementAtIndex(idx).objectReferenceValue = font;
                added++;
            }
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            Debug.Log($"[Idiomas] {added} font(s) agregada(s) a TMP Settings fallback: " +
                string.Join(", ", FONT_NAMES));
        }

        return added;
    }

    /// <summary>
    /// Busca las font assets de Idiomas en el proyecto.
    /// </summary>
    private static TMP_FontAsset[] FindFonts()
    {
        List<TMP_FontAsset> found = new List<TMP_FontAsset>();

        for (int i = 0; i < FONT_NAMES.Length; i++)
        {
            string[] guids = AssetDatabase.FindAssets($"t:TMP_FontAsset {FONT_NAMES[i]}");
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null && font.name == FONT_NAMES[i])
                {
                    found.Add(font);
                    break;
                }
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Obtiene la instancia de TMP_Settings del proyecto.
    /// </summary>
    private static TMP_Settings GetTMPSettings()
    {
        // TMP_Settings.instance puede no funcionar fuera de Play Mode en todas las versiones
        // Buscar directamente el asset
        string[] guids = AssetDatabase.FindAssets("t:TMP_Settings");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<TMP_Settings>(path);
        }
        return null;
    }

    // =====================================================================
    // Menu manual
    // =====================================================================

    [MenuItem("GameObject/Idiomas/Configurar Fonts TMP", false, 30)]
    public static void MenuSetupFonts()
    {
        int result = SetupFonts();
        if (result > 0)
        {
            EditorUtility.DisplayDialog("Fonts Configuradas",
                $"Se agregaron {result} font(s) de fallback a TMP Settings:\n\n" +
                "- NotoSansJP SDF (japones, chino)\n" +
                "- NotoSansKR SDF (coreano, cirilico)\n\n" +
                "Los caracteres CJK y cirilicos ahora se mostraran correctamente.",
                "OK");
        }
        else if (result == 0)
        {
            EditorUtility.DisplayDialog("Ya Configuradas",
                "Las fallback fonts ya estaban en TMP Settings.\n" +
                "No se necesitan cambios.", "OK");
        }
        // result == -1: el error ya se mostro en consola
    }
}
