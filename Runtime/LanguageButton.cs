using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// [OPCIONAL/LEGACY] Boton de idioma para VRChat.
/// Este componente es una alternativa al dropdown. No es necesario si usas
/// el TMP_Dropdown integrado en LocalizationManager.
///
/// Usa este componente si prefieres tener UN BOTON POR IDIOMA en vez de un dropdown.
/// Coloca este componente en un GameObject con Button.
/// Al hacer clic (OnClick), cambia el idioma del LocalizationManager.
///
/// Cada boton conoce a todos los demas botones (hermanos) para
/// actualizar los indicadores visuales al cambiar de idioma.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LanguageButton : UdonSharpBehaviour
{
    [Header("Referencia al Manager")]
    [Tooltip("El LocalizationManager de la escena.")]
    [SerializeField] private LocalizationManager manager;

    [Header("Codigo de Idioma")]
    [Tooltip("Codigo del idioma que este boton activa (ej: 'es', 'en', 'ja'). " +
             "Dejar vacio para auto-detectar.")]
    [SerializeField] private string languageCode = "";

    [Header("Indicador Visual (Opcional)")]
    [Tooltip("GameObject que se activa cuando este idioma esta seleccionado.")]
    [SerializeField] private GameObject activeIndicator;

    [Header("Otros Botones de Idioma")]
    [Tooltip("Todos los LanguageButton de la escena (incluyendo este). " +
             "Se asigna automaticamente al crear la demo.")]
    [SerializeField] private LanguageButton[] allButtons;

    /// <summary>
    /// Llamado por Button.OnClick via UdonBehaviour.SendCustomEvent("OnClick")
    /// </summary>
    public void OnClick()
    {
        if (!Utilities.IsValid(manager))
        {
            Debug.LogWarning("[LanguageButton] Manager no asignado!");
            return;
        }

        if (string.IsNullOrEmpty(languageCode))
        {
            manager.SetLanguage(null);
        }
        else
        {
            manager.SetLanguage(languageCode);
        }

        Debug.Log($"[LanguageButton] Idioma cambiado a: {manager.GetCurrentLanguage()}");

        // Actualizar indicadores de todos los botones
        UpdateAllIndicators();
    }

    /// <summary>
    /// Actualiza los indicadores visuales de todos los botones hermanos.
    /// </summary>
    private void UpdateAllIndicators()
    {
        if (allButtons == null) return;
        for (int i = 0; i < allButtons.Length; i++)
        {
            if (Utilities.IsValid(allButtons[i]))
            {
                allButtons[i]._RefreshIndicator();
            }
        }
    }

    /// <summary>
    /// Actualiza el indicador visual de ESTE boton.
    /// </summary>
    public void _RefreshIndicator()
    {
        if (!Utilities.IsValid(activeIndicator)) return;
        if (!Utilities.IsValid(manager))
        {
            activeIndicator.SetActive(false);
            return;
        }

        string current = manager.GetCurrentLanguage();
        if (string.IsNullOrEmpty(languageCode))
        {
            // Boton Auto: nunca indicador
            activeIndicator.SetActive(false);
        }
        else
        {
            activeIndicator.SetActive(current == languageCode);
        }
    }
}
