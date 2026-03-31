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

    // Parametros dinamicos para reemplazar {0}, {1}, {2} en la traduccion
    private string _param0 = "";
    private string _param1 = "";
    private string _param2 = "";

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

        // Reemplazar parametros dinamicos {0}, {1}, {2}
        if (!string.IsNullOrEmpty(_param0) && translated.Contains("{0}"))
            translated = translated.Replace("{0}", _param0);
        if (!string.IsNullOrEmpty(_param1) && translated.Contains("{1}"))
            translated = translated.Replace("{1}", _param1);
        if (!string.IsNullOrEmpty(_param2) && translated.Contains("{2}"))
            translated = translated.Replace("{2}", _param2);

        // Aplicar formato rich text si esta configurado
        // Soporta {t} (recomendado) y {0} (retrocompatible) como placeholder
        if (!string.IsNullOrEmpty(richTextFormat))
        {
            if (richTextFormat.Contains("{t}"))
                translated = richTextFormat.Replace("{t}", translated);
            else if (richTextFormat.Contains("{0}") && string.IsNullOrEmpty(_param0))
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

    // =====================================================================
    // Parametros dinamicos
    // =====================================================================

    /// <summary>
    /// Asigna parametros dinamicos que reemplazan {0}, {1}, {2} en la traduccion.
    /// Llama UpdateText() automaticamente despues de asignar.
    ///
    /// Ejemplo en JSON: "welcome": "Hola {0}, tienes {1} mensajes"
    /// En runtime: SetParams("Bender", "5") -> "Hola Bender, tienes 5 mensajes"
    /// </summary>
    public void SetParams(string param0)
    {
        _param0 = param0 != null ? param0 : "";
        _param1 = "";
        _param2 = "";
        UpdateText();
    }

    /// <summary>Asigna 2 parametros y actualiza el texto.</summary>
    public void SetParams2(string param0, string param1)
    {
        _param0 = param0 != null ? param0 : "";
        _param1 = param1 != null ? param1 : "";
        _param2 = "";
        UpdateText();
    }

    /// <summary>Asigna 3 parametros y actualiza el texto.</summary>
    public void SetParams3(string param0, string param1, string param2)
    {
        _param0 = param0 != null ? param0 : "";
        _param1 = param1 != null ? param1 : "";
        _param2 = param2 != null ? param2 : "";
        UpdateText();
    }
}
