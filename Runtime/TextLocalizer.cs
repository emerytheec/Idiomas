using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

/// <summary>
/// Componente de localizacion que se coloca en un GameObject con Text o TextMeshProUGUI.
/// Lee la traduccion de su 'translationKey' desde el LocalizationManager y la aplica al texto.
///
/// Uso:
///   1. Agregar este componente al mismo GameObject que tiene Text o TextMeshProUGUI.
///   2. Asignar el LocalizationManager de la escena.
///   3. Escribir la clave de traduccion (ej: "btn_start", "title_welcome").
///   4. El texto se actualiza automaticamente al iniciar y cuando cambia el idioma.
///
/// Soporta:
///   - UnityEngine.UI.Text (legacy)
///   - TMPro.TextMeshProUGUI (recomendado por VRChat)
///   - Formato con prefijo/sufijo (ej: ">> {traduccion} <<")
///   - Multiples claves no soportadas (usar un TextLocalizer por texto)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TextLocalizer : UdonSharpBehaviour
{
    // =====================================================================
    // Configuracion (Inspector)
    // =====================================================================

    [Header("Referencia al Manager")]
    [Tooltip("El LocalizationManager de la escena. Obligatorio.")]
    [SerializeField] private LocalizationManager manager;

    [Header("Clave de Traduccion")]
    [Tooltip("Clave que existe en el JSON de traducciones (ej: 'btn_start', 'title_welcome').")]
    [SerializeField] private string translationKey;

    [Header("Formato (Opcional)")]
    [Tooltip("Texto que va ANTES de la traduccion. Ej: '>> ' produciria '>> Inicio'.")]
    [SerializeField] private string prefix = "";

    [Tooltip("Texto que va DESPUES de la traduccion. Ej: ' <<' produciria 'Inicio <<'.")]
    [SerializeField] private string suffix = "";

    [Header("Rich Text (Opcional)")]
    [Tooltip("Envolver la traduccion en tags de rich text. Ej: '<b>{0}</b>' donde {0} es la traduccion.")]
    [SerializeField] private string richTextFormat = "";

    [Header("Componentes de Texto (Auto-detectados)")]
    [Tooltip("Si no se asignan, se detectan automaticamente del mismo GameObject.")]
    [SerializeField] private Text uiText;
    [SerializeField] private TextMeshProUGUI tmpText;

    // =====================================================================
    // Estado interno
    // =====================================================================

    private bool _initialized;

    // =====================================================================
    // Inicializacion
    // =====================================================================

    private void Start()
    {
        Initialize();
        UpdateText();
    }

    private void Initialize()
    {
        if (_initialized) return;

        // Auto-detectar componentes de texto si no estan asignados
        if (!Utilities.IsValid(uiText))
        {
            uiText = GetComponent<Text>();
        }
        if (!Utilities.IsValid(tmpText))
        {
            tmpText = GetComponent<TextMeshProUGUI>();
        }

        // Validaciones
        if (!Utilities.IsValid(manager))
        {
            Debug.LogWarning($"[TextLocalizer] '{gameObject.name}': No se asigno LocalizationManager.");
        }

        if (string.IsNullOrEmpty(translationKey))
        {
            Debug.LogWarning($"[TextLocalizer] '{gameObject.name}': translationKey esta vacio.");
        }

        if (!Utilities.IsValid(uiText) && !Utilities.IsValid(tmpText))
        {
            Debug.LogWarning($"[TextLocalizer] '{gameObject.name}': No se encontro Text ni TextMeshProUGUI.");
        }

        _initialized = true;
    }

    // =====================================================================
    // Actualizacion de texto
    // =====================================================================

    /// <summary>
    /// Obtiene la traduccion del Manager y la aplica al componente de texto.
    /// Llamado por el LocalizationManager cuando cambia el idioma.
    /// Tambien puede llamarse manualmente.
    /// </summary>
    public void UpdateText()
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(manager)) return;
        if (string.IsNullOrEmpty(translationKey)) return;

        // Obtener traduccion
        string translated = manager.GetValue(translationKey);

        // Aplicar formato rich text si esta configurado
        if (!string.IsNullOrEmpty(richTextFormat) && richTextFormat.Contains("{0}"))
        {
            // UdonSharp no soporta string.Format con facilidad,
            // asi que hacemos reemplazo manual
            translated = richTextFormat.Replace("{0}", translated);
        }

        // Aplicar prefijo y sufijo
        string final_text = string.Concat(prefix, translated, suffix);

        // Asignar al componente de texto que exista
        if (Utilities.IsValid(tmpText))
        {
            tmpText.text = final_text;
        }
        else if (Utilities.IsValid(uiText))
        {
            uiText.text = final_text;
        }
    }

    // =====================================================================
    // Utilidades publicas
    // =====================================================================

    /// <summary>Devuelve la clave de traduccion asignada.</summary>
    public string GetTranslationKey()
    {
        return translationKey;
    }

    /// <summary>Cambia la clave de traduccion en runtime y actualiza el texto.</summary>
    public void SetTranslationKey(string newKey)
    {
        translationKey = newKey;
        UpdateText();
    }

    /// <summary>Devuelve el LocalizationManager asignado.</summary>
    public LocalizationManager GetManager()
    {
        return manager;
    }

    /// <summary>Asigna un LocalizationManager en runtime.</summary>
    public void SetManager(LocalizationManager newManager)
    {
        manager = newManager;
        UpdateText();
    }
}
