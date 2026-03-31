using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;

/// <summary>
/// [OPCIONAL] Selector de idioma basado en TMP_Dropdown para VRChat.
/// NOTA: La logica principal del dropdown esta centralizada en LocalizationManager
/// (metodo OnLanguageDropdownChanged). Este componente es un wrapper adicional
/// que se puede usar si necesitas un dropdown separado del manager.
/// En la mayoria de los casos, NO necesitas este componente.
///
/// Ventajas sobre el sistema de botones:
///   - Un solo componente por selector (en vez de uno por boton).
///   - Un solo evento a cablear (OnValueChanged → OnDropdownChanged).
///   - Facil de extender: agregar idiomas es agregar opciones al dropdown.
///   - Funciona con World Space Canvas + VRCUiShape.
///
/// Uso:
///   1. Colocar un TMP_Dropdown en el canvas.
///   2. Agregar este componente al MISMO GameObject.
///   3. Asignar el LocalizationManager.
///   4. Los languageCodes[] deben coincidir con las opciones del dropdown
///      (misma cantidad, mismo orden). Dejar un codigo vacio = auto-detectar.
///   5. El OnValueChanged del dropdown debe llamar OnDropdownChanged()
///      (se configura automaticamente al crear la demo, o a mano en el Inspector).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LanguageDropdown : UdonSharpBehaviour
{
    [Header("Referencias")]
    [Tooltip("El LocalizationManager de la escena.")]
    [SerializeField] private LocalizationManager manager;

    [Tooltip("El TMP_Dropdown de este mismo GameObject (se auto-detecta si no se asigna).")]
    [SerializeField] private TMP_Dropdown dropdown;

    [Header("Configuracion")]
    [Tooltip("Codigos de idioma en el MISMO ORDEN que las opciones del dropdown.\n" +
             "Dejar vacio = auto-detectar idioma del jugador.\n" +
             "Ejemplo: [\"\", \"en\", \"es\", \"ja\", \"ko\"]")]
    [SerializeField] private string[] languageCodes = new string[0];

    private bool _initialized;

    private void Start()
    {
        Initialize();
        SyncDropdownToCurrentLanguage();
    }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        if (!Utilities.IsValid(dropdown))
        {
            dropdown = GetComponent<TMP_Dropdown>();
        }

        // Auto-buscar LocalizationManager si no esta asignado
        if (!Utilities.IsValid(manager))
        {
            GameObject mgrGO = GameObject.Find("LocalizationManager");
            if (Utilities.IsValid(mgrGO))
            {
                manager = mgrGO.GetComponent<LocalizationManager>();
            }
        }

        if (!Utilities.IsValid(manager))
        {
            Debug.LogWarning($"[LanguageDropdown] '{gameObject.name}': No se encontro LocalizationManager.");
        }

        if (!Utilities.IsValid(dropdown))
        {
            Debug.LogWarning($"[LanguageDropdown] '{gameObject.name}': No se encontro TMP_Dropdown.");
        }
    }

    /// <summary>
    /// Llamado por TMP_Dropdown.OnValueChanged via UdonBehaviour.SendCustomEvent("OnDropdownChanged").
    /// Lee el indice seleccionado del dropdown y cambia el idioma en el manager.
    /// </summary>
    public void OnDropdownChanged()
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(manager)) return;
        if (!Utilities.IsValid(dropdown)) return;

        int index = dropdown.value;
        if (languageCodes == null || index < 0 || index >= languageCodes.Length)
        {
            Debug.LogWarning($"[LanguageDropdown] Indice {index} fuera de rango (codigos: {(languageCodes != null ? languageCodes.Length : 0)}).");
            return;
        }

        string code = languageCodes[index];

        if (string.IsNullOrEmpty(code))
        {
            // Auto-detectar
            manager.SetLanguage(null);
            Debug.Log($"[LanguageDropdown] Idioma: Auto ({manager.GetCurrentLanguage()})");
        }
        else
        {
            manager.SetLanguage(code);
            Debug.Log($"[LanguageDropdown] Idioma: {code}");
        }
    }

    /// <summary>
    /// Sincroniza el dropdown para que muestre el idioma actualmente activo.
    /// Se llama en Start() y puede llamarse manualmente.
    /// </summary>
    public void SyncDropdownToCurrentLanguage()
    {
        if (!Utilities.IsValid(manager)) return;
        if (!Utilities.IsValid(dropdown)) return;
        if (languageCodes == null) return;

        string current = manager.GetCurrentLanguage();
        if (string.IsNullOrEmpty(current)) return;

        for (int i = 0; i < languageCodes.Length; i++)
        {
            if (languageCodes[i] == current)
            {
                dropdown.SetValueWithoutNotify(i);
                return;
            }
        }
    }

    /// <summary>Devuelve el LocalizationManager asignado.</summary>
    public LocalizationManager GetManager()
    {
        return manager;
    }
}
