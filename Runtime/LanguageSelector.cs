using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// Componente de UI para seleccionar idioma.
/// Coloca este componente en un GameObject y conecta botones de idioma a sus metodos publicos.
///
/// Uso:
///   1. Crear botones de UI (uno por idioma).
///   2. En el OnClick() de cada boton, asignar este componente y el metodo correspondiente
///      (ej: SetLanguageSpanish, SetLanguageEnglish, SetLanguageJapanese).
///   3. Opcionalmente, asignar un GameObject 'activeIndicator' por idioma para resaltar el activo.
///
/// Funcionalidad extra:
///   - Resalta visualmente el boton del idioma activo.
///   - Soporta auto-deteccion (boton "Auto").
///   - Puede recibir notificaciones desde sistemas externos via SendCustomEvent.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LanguageSelector : UdonSharpBehaviour
{
    // =====================================================================
    // Configuracion (Inspector)
    // =====================================================================

    [Header("Referencia al Manager")]
    [Tooltip("El LocalizationManager de la escena.")]
    [SerializeField] private LocalizationManager manager;

    [Header("Indicadores Visuales (Opcional)")]
    [Tooltip("GameObjects que se activan/desactivan para indicar el idioma actual. " +
             "El orden debe coincidir con languageCodes.")]
    [SerializeField] private GameObject[] languageIndicators;

    [Tooltip("Codigos de idioma correspondientes a cada indicador. " +
             "Ej: 'es', 'en', 'ja'. Mismo orden que languageIndicators.")]
    [SerializeField] private string[] languageCodes;

    [Header("Color del Boton Activo (Opcional)")]
    [Tooltip("Si se asignan botones aqui, el boton del idioma activo cambiara de color.")]
    [SerializeField] private Button[] languageButtons;

    [Tooltip("Color del boton cuando su idioma esta activo.")]
    [SerializeField] private Color activeColor = new Color(0.3f, 0.7f, 1f, 1f);

    [Tooltip("Color del boton cuando su idioma NO esta activo.")]
    [SerializeField] private Color inactiveColor = new Color(1f, 1f, 1f, 1f);

    // =====================================================================
    // Metodos de cambio de idioma (para conectar desde OnClick de botones)
    // =====================================================================

    public void SetLanguageAuto()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage(null);
        UpdateIndicators();
    }

    public void SetLanguageSpanish()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("es");
        UpdateIndicators();
    }

    public void SetLanguageEnglish()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("en");
        UpdateIndicators();
    }

    public void SetLanguageJapanese()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("ja");
        UpdateIndicators();
    }

    public void SetLanguageKorean()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("ko");
        UpdateIndicators();
    }

    public void SetLanguageChineseSimplified()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("zh-CN");
        UpdateIndicators();
    }

    public void SetLanguageChineseTraditional()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("zh-TW");
        UpdateIndicators();
    }

    public void SetLanguageRussian()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("ru");
        UpdateIndicators();
    }

    public void SetLanguagePortuguese()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("pt-BR");
        UpdateIndicators();
    }

    public void SetLanguageCatalan()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("ca");
        UpdateIndicators();
    }

    public void SetLanguageFrench()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("fr");
        UpdateIndicators();
    }

    public void SetLanguageGerman()
    {
        if (!Utilities.IsValid(manager)) return;
        manager.SetLanguage("de");
        UpdateIndicators();
    }

    // =====================================================================
    // Metodos para recibir eventos desde otros sistemas (SendCustomEvent)
    // Estos permiten que un sistema externo (como YamaPlayer) notifique
    // un cambio de idioma sin tener referencia directa a este componente.
    // =====================================================================

    public void _SetLanguageJa() { if (Utilities.IsValid(manager)) { manager.SetLanguage("ja"); UpdateIndicators(); } }
    public void _SetLanguageEn() { if (Utilities.IsValid(manager)) { manager.SetLanguage("en"); UpdateIndicators(); } }
    public void _SetLanguageEs() { if (Utilities.IsValid(manager)) { manager.SetLanguage("es"); UpdateIndicators(); } }
    public void _SetLanguageKo() { if (Utilities.IsValid(manager)) { manager.SetLanguage("ko"); UpdateIndicators(); } }
    public void _SetLanguageRu() { if (Utilities.IsValid(manager)) { manager.SetLanguage("ru"); UpdateIndicators(); } }
    public void _SetLanguageCa() { if (Utilities.IsValid(manager)) { manager.SetLanguage("ca"); UpdateIndicators(); } }
    public void _SetLanguageZhCN() { if (Utilities.IsValid(manager)) { manager.SetLanguage("zh-CN"); UpdateIndicators(); } }
    public void _SetLanguageZhTW() { if (Utilities.IsValid(manager)) { manager.SetLanguage("zh-TW"); UpdateIndicators(); } }
    public void _SetLanguagePtBR() { if (Utilities.IsValid(manager)) { manager.SetLanguage("pt-BR"); UpdateIndicators(); } }
    public void _SetLanguageFr() { if (Utilities.IsValid(manager)) { manager.SetLanguage("fr"); UpdateIndicators(); } }
    public void _SetLanguageDe() { if (Utilities.IsValid(manager)) { manager.SetLanguage("de"); UpdateIndicators(); } }

    // =====================================================================
    // Indicadores visuales
    // =====================================================================

    private void Start()
    {
        UpdateIndicators();
    }

    /// <summary>
    /// Actualiza los indicadores visuales para resaltar el idioma activo.
    /// </summary>
    public void UpdateIndicators()
    {
        if (!Utilities.IsValid(manager)) return;

        string current = manager.GetCurrentLanguage();
        if (string.IsNullOrEmpty(current)) return;

        // Actualizar GameObjects indicadores
        if (languageIndicators != null && languageCodes != null)
        {
            int len = Mathf.Min(languageIndicators.Length, languageCodes.Length);
            for (int i = 0; i < len; i++)
            {
                if (Utilities.IsValid(languageIndicators[i]))
                {
                    bool isActive = languageCodes[i] == current;
                    languageIndicators[i].SetActive(isActive);
                }
            }
        }

        // Actualizar colores de botones
        if (languageButtons != null && languageCodes != null)
        {
            int len = Mathf.Min(languageButtons.Length, languageCodes.Length);
            for (int i = 0; i < len; i++)
            {
                if (Utilities.IsValid(languageButtons[i]))
                {
                    bool isActive = languageCodes[i] == current;
                    ColorBlock colors = languageButtons[i].colors;
                    colors.normalColor = isActive ? activeColor : inactiveColor;
                    languageButtons[i].colors = colors;
                }
            }
        }
    }
}
