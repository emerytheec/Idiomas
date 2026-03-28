using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

/// <summary>
/// Sistema de localizacion standalone para VRChat.
/// Carga un JSON de traducciones, detecta el idioma del jugador automaticamente,
/// y notifica a todos los TextLocalizer registrados cuando cambia el idioma.
///
/// Uso:
///   1. Colocar este componente en un GameObject de la escena.
///   2. Asignar el TextAsset con el JSON de traducciones.
///   3. Registrar los TextLocalizer en el array 'localizers' (o usar el boton del Editor).
///
/// Formato del JSON:
///   {
///     "en": { "clave": "valor", ... },
///     "es": { "clave": "valor", ... },
///     "ja": { "clave": "valor", ... }
///   }
///
/// Independiente de YamaPlayer. No requiere Controller, UIController, ni ningun otro sistema.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LocalizationManager : UdonSharpBehaviour
{
    // =====================================================================
    // Configuracion (Inspector)
    // =====================================================================

    [Header("Datos de Traduccion")]
    [Tooltip("Archivo JSON con las traducciones. Formato: { \"en\": { \"key\": \"value\" }, ... }")]
    [SerializeField] private TextAsset translationFile;

    [Header("Idioma por Defecto")]
    [Tooltip("Idioma de fallback si el idioma del jugador no existe en el JSON. Normalmente 'en'.")]
    [SerializeField] private string fallbackLanguage = "en";

    [Header("Componentes Localizados")]
    [Tooltip("Todos los TextLocalizer de la escena. Usar el boton 'Auto-buscar' del Inspector.")]
    [SerializeField] private TextLocalizer[] localizers = new TextLocalizer[0];

    [Header("Canvas Localizados")]
    [Tooltip("CanvasLocalizer que gestionan canvas completos. Usar el boton 'Auto-buscar' del Inspector.")]
    [SerializeField] private CanvasLocalizer[] canvasLocalizers = new CanvasLocalizer[0];

    [Header("Selector de Idioma (Dropdown)")]
    [Tooltip("TMP_Dropdown para cambiar idioma. Opcional. Se configura automaticamente al crear el selector.")]
    [SerializeField] private TMPro.TMP_Dropdown _languageDropdown;

    [Tooltip("Codigos de idioma en el MISMO ORDEN que las opciones del dropdown. Vacio = auto-detectar.")]
    [SerializeField] private string[] _dropdownLanguageCodes = new string[0];

    // =====================================================================
    // Estado interno
    // =====================================================================

    private DataDictionary _translationData;
    private string _currentLanguage;
    private bool _initialized;
    private string[] _availableLanguages = new string[0];

    // =====================================================================
    // Propiedades publicas (solo lectura desde otros scripts)
    // =====================================================================

    /// <summary>Idioma actualmente activo.</summary>
    [HideInInspector] public string currentLanguage;

    /// <summary>Numero de idiomas disponibles en el JSON.</summary>
    [HideInInspector] public int languageCount;

    /// <summary>True cuando el sistema esta listo para usar.</summary>
    [HideInInspector] public bool isReady;

    // =====================================================================
    // Inicializacion
    // =====================================================================

    private void Start()
    {
        Initialize();
        SyncDropdownToCurrentLanguage();
        ApplyToAll();
    }

    private void Initialize()
    {
        if (_initialized) return;

        // --- Cargar JSON ---
        if (!Utilities.IsValid(translationFile))
        {
            Debug.LogError("[LocalizationManager] No se asigno translationFile. Asigna un TextAsset JSON.");
            _initialized = true;
            isReady = false;
            return;
        }

        if (!VRCJson.TryDeserializeFromJson(translationFile.text, out DataToken data))
        {
            Debug.LogError("[LocalizationManager] Error al parsear el JSON de traducciones.");
            _initialized = true;
            isReady = false;
            return;
        }

        if (data.TokenType != TokenType.DataDictionary)
        {
            Debug.LogError("[LocalizationManager] El JSON de traducciones debe ser un objeto raiz {}.");
            _initialized = true;
            isReady = false;
            return;
        }

        _translationData = data.DataDictionary;

        // --- Extraer lista de idiomas disponibles ---
        DataList keys = _translationData.GetKeys();
        _availableLanguages = new string[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            _availableLanguages[i] = keys[i].String;
        }
        languageCount = _availableLanguages.Length;

        // --- Detectar idioma del jugador ---
        _currentLanguage = DetectLanguage();
        currentLanguage = _currentLanguage;

        _initialized = true;
        isReady = true;

        Debug.Log($"[LocalizationManager] Inicializado. Idioma: {_currentLanguage}, Idiomas disponibles: {languageCount}");
    }

    // =====================================================================
    // Deteccion automatica de idioma
    // =====================================================================

    /// <summary>
    /// Detecta el idioma del jugador usando la API de VRChat.
    /// Si el idioma no existe en el JSON, intenta variantes (es-CL -> es).
    /// Si no encuentra nada, usa zona horaria como fallback.
    /// </summary>
    private string DetectLanguage()
    {
        // 1. Obtener idioma de VRChat
        string vrcLang = VRCPlayerApi.GetCurrentLanguage();

        // 2. Verificar si existe directamente en el JSON
        if (!string.IsNullOrEmpty(vrcLang) && HasLanguage(vrcLang))
        {
            return vrcLang;
        }

        // 3. Intentar variantes del idioma
        //    VRChat puede devolver "es-CL" pero el JSON tiene "es", o viceversa
        if (!string.IsNullOrEmpty(vrcLang))
        {
            // Intentar sin region: "es-CL" -> "es"
            if (vrcLang.Contains("-"))
            {
                string baseLang = vrcLang.Substring(0, vrcLang.IndexOf('-'));
                if (HasLanguage(baseLang)) return baseLang;
            }

            // Intentar buscar cualquier variante que empiece con el mismo codigo base
            for (int i = 0; i < _availableLanguages.Length; i++)
            {
                if (_availableLanguages[i].StartsWith(vrcLang) ||
                    (vrcLang.Contains("-") && _availableLanguages[i].StartsWith(vrcLang.Substring(0, vrcLang.IndexOf('-')))))
                {
                    return _availableLanguages[i];
                }
            }
        }

        // 4. Fallback por zona horaria
        string tzLang = GetLanguageByTimeZone();
        if (HasLanguage(tzLang)) return tzLang;

        // 5. Fallback final
        if (HasLanguage(fallbackLanguage)) return fallbackLanguage;

        // 6. Si nada funciona, usar el primer idioma disponible
        if (_availableLanguages.Length > 0) return _availableLanguages[0];

        return "en";
    }

    /// <summary>
    /// Detecta idioma por zona horaria del sistema (fallback para cuando VRChat
    /// no devuelve un idioma valido o no existe en el JSON).
    /// </summary>
    private string GetLanguageByTimeZone()
    {
        TimeZoneInfo tz = TimeZoneInfo.Local;
        switch (tz.Id)
        {
            case "Tokyo Standard Time":
                return "ja";
            case "Taipei Standard Time":
                return "zh-TW";
            case "China Standard Time":
                return "zh-CN";
            case "Korea Standard Time":
            case "North Korea Standard Time":
                return "ko";
            case "Romance Standard Time":
            case "W. Europe Standard Time":
                return "es";
            case "Russian Standard Time":
            case "Moscow Standard Time":
                return "ru";
            default:
                return "en";
        }
    }

    // =====================================================================
    // Cambio de idioma
    // =====================================================================

    /// <summary>
    /// Cambia el idioma activo. Si el idioma no existe, no hace nada.
    /// Notifica a todos los TextLocalizer registrados.
    /// </summary>
    public void SetLanguage(string language)
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(_translationData)) return;

        if (string.IsNullOrEmpty(language))
        {
            // Auto-detectar
            language = DetectLanguage();
        }

        // Verificar que el idioma existe (con variantes)
        string resolved = ResolveLanguage(language);
        if (string.IsNullOrEmpty(resolved))
        {
            Debug.LogWarning($"[LocalizationManager] Idioma '{language}' no encontrado. Disponibles: {string.Join(", ", _availableLanguages)}");
            return;
        }

        if (_currentLanguage == resolved) return; // Sin cambio

        _currentLanguage = resolved;
        currentLanguage = resolved;

        Debug.Log($"[LocalizationManager] Idioma cambiado a: {resolved}");
        ApplyToAll();
    }

    /// <summary>
    /// Resuelve un codigo de idioma a uno que exista en el JSON.
    /// Maneja variantes como "es-CL" -> "es".
    /// </summary>
    private string ResolveLanguage(string language)
    {
        // Directo
        if (HasLanguage(language)) return language;

        // Sin region
        if (language.Contains("-"))
        {
            string baseLang = language.Substring(0, language.IndexOf('-'));
            if (HasLanguage(baseLang)) return baseLang;
        }

        // Buscar variante
        for (int i = 0; i < _availableLanguages.Length; i++)
        {
            if (_availableLanguages[i].StartsWith(language)) return _availableLanguages[i];
        }

        return null;
    }

    // =====================================================================
    // Dropdown de idioma
    // =====================================================================

    /// <summary>
    /// Llamado por TMP_Dropdown.OnValueChanged via SendCustomEvent("OnLanguageDropdownChanged").
    /// Lee el indice seleccionado y cambia el idioma.
    /// </summary>
    public void OnLanguageDropdownChanged()
    {
        if (!Utilities.IsValid(_languageDropdown)) return;
        if (_dropdownLanguageCodes == null) return;

        int index = _languageDropdown.value;
        if (index < 0 || index >= _dropdownLanguageCodes.Length) return;

        string code = _dropdownLanguageCodes[index];
        if (string.IsNullOrEmpty(code))
        {
            SetLanguage(null); // Auto-detectar
        }
        else
        {
            SetLanguage(code);
        }
    }

    /// <summary>
    /// Sincroniza el dropdown para que muestre el idioma actual.
    /// Se llama automaticamente en Start() si hay dropdown asignado.
    /// </summary>
    private void SyncDropdownToCurrentLanguage()
    {
        if (!Utilities.IsValid(_languageDropdown)) return;
        if (_dropdownLanguageCodes == null) return;

        for (int i = 0; i < _dropdownLanguageCodes.Length; i++)
        {
            if (_dropdownLanguageCodes[i] == _currentLanguage)
            {
                _languageDropdown.SetValueWithoutNotify(i);
                return;
            }
        }
    }

    // =====================================================================
    // Metodos de conveniencia para botones de UI
    // (SendCustomEvent solo acepta metodos sin parametros)
    // =====================================================================

    public void SetLanguageAuto() => SetLanguage(null);
    public void SetLanguageJapanese() => SetLanguage("ja");
    public void SetLanguageEnglish() => SetLanguage("en");
    public void SetLanguageSpanish() => SetLanguage("es");
    public void SetLanguageKorean() => SetLanguage("ko");
    public void SetLanguageChineseSimplified() => SetLanguage("zh-CN");
    public void SetLanguageChineseTraditional() => SetLanguage("zh-TW");
    public void SetLanguageRussian() => SetLanguage("ru");
    public void SetLanguagePortuguese() => SetLanguage("pt-BR");
    public void SetLanguageCatalan() => SetLanguage("ca");
    public void SetLanguageFrench() => SetLanguage("fr");
    public void SetLanguageGerman() => SetLanguage("de");

    // =====================================================================
    // Obtencion de traducciones
    // =====================================================================

    /// <summary>
    /// Obtiene la traduccion de una clave en el idioma actual.
    /// Si no existe en el idioma actual, busca en el fallback (normalmente ingles).
    /// Si tampoco existe en el fallback, devuelve la propia clave entre corchetes: [clave].
    /// </summary>
    public string GetValue(string key)
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(_translationData)) return $"[{key}]";
        if (string.IsNullOrEmpty(key)) return string.Empty;

        // 1. Buscar en idioma actual
        string result = GetValueForLanguage(_currentLanguage, key);
        if (!string.IsNullOrEmpty(result)) return result;

        // 2. Buscar en idioma de fallback
        if (_currentLanguage != fallbackLanguage)
        {
            result = GetValueForLanguage(fallbackLanguage, key);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        // 3. Buscar en cualquier idioma que tenga la clave (ultimo recurso)
        for (int i = 0; i < _availableLanguages.Length; i++)
        {
            if (_availableLanguages[i] == _currentLanguage) continue;
            if (_availableLanguages[i] == fallbackLanguage) continue;
            result = GetValueForLanguage(_availableLanguages[i], key);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        // 4. No se encontro en ningun idioma
        return $"[{key}]";
    }

    /// <summary>
    /// Busca una clave en un idioma especifico.
    /// </summary>
    private string GetValueForLanguage(string language, string key)
    {
        if (!Utilities.IsValid(_translationData)) return null;
        if (string.IsNullOrEmpty(language)) return null;

        if (_translationData.TryGetValue(language, out DataToken langToken))
        {
            if (langToken.TokenType == TokenType.DataDictionary)
            {
                if (langToken.DataDictionary.TryGetValue(key, out DataToken valueToken))
                {
                    return valueToken.String;
                }
            }
        }

        return null;
    }

    // =====================================================================
    // Notificacion a TextLocalizers
    // =====================================================================

    /// <summary>
    /// Recorre todos los TextLocalizer y CanvasLocalizer registrados y les ordena actualizarse.
    /// </summary>
    public void ApplyToAll()
    {
        // Actualizar TextLocalizers individuales
        if (localizers != null)
        {
            for (int i = 0; i < localizers.Length; i++)
            {
                if (Utilities.IsValid(localizers[i]))
                {
                    localizers[i].UpdateText();
                }
            }
        }

        // Actualizar CanvasLocalizers (canvas completos)
        if (canvasLocalizers != null)
        {
            for (int i = 0; i < canvasLocalizers.Length; i++)
            {
                if (Utilities.IsValid(canvasLocalizers[i]))
                {
                    canvasLocalizers[i].UpdateAllTexts();
                }
            }
        }
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    /// <summary>Verifica si un idioma existe en el JSON de traducciones.</summary>
    public bool HasLanguage(string language)
    {
        if (!Utilities.IsValid(_translationData)) return false;
        if (string.IsNullOrEmpty(language)) return false;
        return _translationData.ContainsKey(language);
    }

    /// <summary>Devuelve el idioma activo actualmente.</summary>
    public string GetCurrentLanguage()
    {
        return _currentLanguage;
    }

    /// <summary>Devuelve un array con los codigos de idioma disponibles.</summary>
    public string[] GetAvailableLanguages()
    {
        return _availableLanguages;
    }

    /// <summary>
    /// Registra un TextLocalizer adicional en runtime.
    /// Util para objetos creados dinamicamente.
    /// Nota: En UdonSharp no se puede redimensionar arrays facilmente,
    /// asi que este metodo crea un nuevo array cada vez. Usar con moderacion.
    /// </summary>
    public void RegisterLocalizer(TextLocalizer localizer)
    {
        if (!Utilities.IsValid(localizer)) return;

        // Verificar que no este ya registrado
        if (localizers != null)
        {
            for (int i = 0; i < localizers.Length; i++)
            {
                if (localizers[i] == localizer) return;
            }
        }

        // Crear nuevo array con un espacio mas
        int oldLen = localizers != null ? localizers.Length : 0;
        TextLocalizer[] newArr = new TextLocalizer[oldLen + 1];
        for (int i = 0; i < oldLen; i++)
        {
            newArr[i] = localizers[i];
        }
        newArr[oldLen] = localizer;
        localizers = newArr;

        // Aplicar idioma actual al nuevo localizer
        localizer.UpdateText();
    }

    /// <summary>
    /// Registra un CanvasLocalizer adicional en runtime.
    /// Llamado automaticamente por CanvasLocalizer.Start().
    /// Nota: Crea un nuevo array cada vez. Usar con moderacion.
    /// </summary>
    public void RegisterCanvasLocalizer(CanvasLocalizer canvasLocalizer)
    {
        if (!Utilities.IsValid(canvasLocalizer)) return;

        // Verificar que no este ya registrado
        if (canvasLocalizers != null)
        {
            for (int i = 0; i < canvasLocalizers.Length; i++)
            {
                if (canvasLocalizers[i] == canvasLocalizer) return;
            }
        }

        // Crear nuevo array con un espacio mas
        int oldLen = canvasLocalizers != null ? canvasLocalizers.Length : 0;
        CanvasLocalizer[] newArr = new CanvasLocalizer[oldLen + 1];
        for (int i = 0; i < oldLen; i++)
        {
            newArr[i] = canvasLocalizers[i];
        }
        newArr[oldLen] = canvasLocalizer;
        canvasLocalizers = newArr;

        // Aplicar idioma actual al nuevo canvas localizer
        canvasLocalizer.UpdateAllTexts();
    }
}
