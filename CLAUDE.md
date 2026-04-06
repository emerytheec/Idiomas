# CLAUDE.md - Idiomas (Sistema de Localizacion para VRChat)

Este archivo proporciona orientacion a Claude Code cuando trabaja con el sistema Idiomas.

---

## IMPORTANTE: Distribucion y Repositorios

### Este es un proyecto INDEPENDIENTE

Idiomas tiene su **propio repositorio** en GitHub. NO vive dentro de YamaPlayer-Karaoke.

| Concepto | Valor |
|----------|-------|
| **Repositorio** | `emerytheec/Idiomas` |
| **Rama principal** | `master` |
| **Nombre del paquete VPM** | `com.benderdios.idiomas` |
| **VPM listing** | `emerytheec/vpm-listing` (index.json) |
| **Release workflow** | `.github/workflows/release.yml` (se dispara con tag `v*`) |
| **Assets del release** | ZIP VPM + .unitypackage (generados automaticamente) |

### En el proyecto Unity local

Los archivos viven en `Assets/Idiomas/` dentro del proyecto Unity del karaoke,
pero eso es la **copia de trabajo local**. NO se sube al repo de YamaPlayer-Karaoke.

### Como hacer un release (actualizar Companion)

```bash
# 1. Clonar el repo Idiomas
git clone https://github.com/emerytheec/Idiomas.git /tmp/Idiomas-repo

# 2. Copiar archivos mejorados desde el proyecto Unity al clon
#    (solo Runtime/, Editor/, README.md — NO copiar .meta de Unity)
#    IMPORTANTE: NO copiar translation.json — el repo lleva un JSON vacio {}
#    para que cada usuario genere sus propias traducciones con la herramienta.
cp Assets/Idiomas/Runtime/*.cs /tmp/Idiomas-repo/Runtime/
cp Assets/Idiomas/Editor/*.cs /tmp/Idiomas-repo/Editor/
cp Assets/Idiomas/README.md /tmp/Idiomas-repo/README.md

# 3. Configurar git en el clon
cd /tmp/Idiomas-repo
git config user.name "Bender_Dios"
git config user.email "emerytheec@users.noreply.github.com"

# 4. Commit y push
git add -A
git commit -m "Descripcion de los cambios"
git push origin master

# 5. Crear tag para disparar el workflow de release
git tag v1.0.X
git push origin v1.0.X

# 6. Verificar que el workflow corrio bien
gh run list --repo emerytheec/Idiomas --limit 1
gh release view v1.0.X --repo emerytheec/Idiomas
```

El workflow automaticamente:
- Actualiza la version en package.json
- Crea `com.benderdios.idiomas-X.X.X.zip` (VPM)
- Crea `Idiomas-X.X.X.unitypackage`
- Sube ambos al GitHub Release
- Actualiza `emerytheec/vpm-listing/index.json` (para que Companion lo detecte)

### NUNCA hacer esto

- **NUNCA** subir archivos de `Assets/Idiomas/` al repo `YamaPlayer-Karaoke`
- **NUNCA** mezclar commits de Idiomas con commits de YamaPlayer
- **NUNCA** cambiar el nombre del paquete (`com.benderdios.idiomas` es el correcto)
- **NUNCA** subir escenas (.unity), SerializedUdonPrograms, ClientSimStorage ni archivos de trabajo de Unity a ningun repo
- **NUNCA** copiar `translation.json` local al repo de Idiomas — el repo lleva un JSON vacio `{}` para que usuarios generen sus propias traducciones

---

## Informacion del Proyecto

**Idiomas** es un sistema de localizacion standalone para mundos de VRChat usando UdonSharp.
- **Paquete**: `com.benderdios.idiomas`
- **Autor**: Bender_Dios
- **Framework**: Unity 2022.3.6f1
- **Lenguaje**: C# con UdonSharp
- **Licencia**: MIT
- **Dependencia**: VRChat SDK Worlds >= 3.8.1
- **100% independiente**: No requiere YamaPlayer ni ningun otro sistema externo.

---

## Preferencias del Usuario

- **Idioma**: Siempre responder en espanol
- **Nivel de experiencia**: No soy programador experto, explicar de forma clara y sencilla
- **Tipo de proyecto**: Mundos para VRChat

---

## Arquitectura

### Patron: Manager-Component (Observer simplificado)

```
LocalizationManager (Cerebro - 1 por escena)
    |-- Carga translation.json via VRCJson
    |-- Detecta idioma del jugador (VRChat API -> variantes -> timezone -> fallback)
    |-- Cachea diccionario del idioma activo y fallback para rendimiento
    +-- Notifica a todos los registrados cuando cambia
        |-- TextLocalizer[] (individuales, 1 por texto, con parametros {0},{1},{2})
        |-- CanvasLocalizer[] (colectivos, 1 por canvas completo)
        |-- UdonSharpBehaviour[] _listeners (reciben "_OnLanguageChanged")
        +-- Dropdown (via SendCustomEvent -> OnLanguageDropdownChanged)
```

### BehaviourSyncMode

Todos los componentes usan `BehaviourSyncMode.None`. La localizacion es **local** para cada jugador.

### Flujo de datos en Runtime

1. `Start()` -> `Initialize()` -> carga JSON, detecta idioma, cachea diccionario
2. `SyncDropdownToCurrentLanguage()` -> actualiza UI
3. `ApplyToAll()` -> recorre TextLocalizer[] y CanvasLocalizer[]
4. Cada localizer llama a `manager.GetValue(key)` (usa cache directo)
5. Cambio de idioma -> `SetLanguage()` -> recachea -> `ApplyToAll()` -> `NotifyListeners()`

### Algoritmo de deteccion de idioma (cascada)

1. `VRCPlayerApi.GetCurrentLanguage()` - API oficial de VRChat
2. Variantes: "es-CL" -> busca "es" si no existe "es-CL"
3. Busqueda con `StartsWith` para coincidencias parciales
4. Zona horaria del sistema (`TimeZoneInfo.Local`) - soporta Windows y IANA (Quest/Android)
5. Idioma de fallback configurado (por defecto "en")
6. Primer idioma disponible en el JSON

**Cobertura de zonas horarias:** Espana, Francia, Alemania, Rusia (todas las zonas),
Japon, Corea, China, Taiwan, Brasil, y 15+ paises latinoamericanos.

---

## Estructura de Archivos

```
Idiomas/  (raiz del repo emerytheec/Idiomas)
|-- Runtime/                              # Scripts UdonSharp
|   |-- LocalizationManager.cs            # Cerebro: JSON, deteccion, cache, listeners
|   |-- CanvasLocalizer.cs                # Localiza canvas completo (arrays paralelos)
|   |-- TextLocalizer.cs                  # Localiza texto individual + parametros {0},{1},{2}
|   |-- LanguageDropdown.cs               # [OPCIONAL] Wrapper TMP_Dropdown
|   |-- LanguageSelector.cs               # Selector con indicadores visuales
|   |-- LanguageButton.cs                 # [OPCIONAL/LEGACY] Boton individual
|   +-- *.asset                           # Udon C# Program Assets compilados
|-- Editor/                               # Scripts solo para Editor de Unity
|   |-- CanvasLocalizerEditor.cs          # Inspector: escaneo, tabla, export JSON
|   |-- CanvasLocalizerGizmo.cs           # Gizmo en Scene View
|   |-- LocalizationManagerEditor.cs      # Inspector: validacion, preview, wire dropdown
|   |-- AutoTranslateWindow.cs            # Ventana: MyMemory API
|   |-- CsvExportImportWindow.cs          # Ventana: CSV export/import (Tools > Idiomas)
|   |-- IdiomasLanguages.cs               # Definiciones centralizadas de idiomas
|   |-- IdiomasEditorUtils.cs             # Utilidades compartidas (JSON, canvas ID)
|   +-- IdiomasPrefabCreator.cs           # Menus de creacion
|-- Data/
|   +-- translation.json                  # Traducciones (11 idiomas)
|-- Prefabs/
|   +-- LocalizationManager.prefab        # Prefab listo para usar
|-- .github/workflows/release.yml         # Workflow de release automatico
|-- package.json                          # Metadatos VPM (com.benderdios.idiomas)
|-- README.md                             # Documentacion de uso
+-- LICENSE                               # MIT
```

---

## Componentes Runtime - Referencia Rapida

### LocalizationManager.cs

| Campo Inspector | Tipo | Descripcion |
|----------------|------|-------------|
| `translationFile` | TextAsset | JSON con las traducciones |
| `fallbackLanguage` | string | Idioma de fallback (default: "en") |
| `localizers` | TextLocalizer[] | Todos los TextLocalizer de la escena |
| `canvasLocalizers` | CanvasLocalizer[] | Todos los CanvasLocalizer de la escena |
| `_languageDropdown` | TMP_Dropdown | Dropdown para cambiar idioma (opcional) |
| `_dropdownLanguageCodes` | string[] | Codigos en el mismo orden que las opciones del dropdown |
| `_listeners` | UdonSharpBehaviour[] | Scripts que reciben `_OnLanguageChanged` |

**Metodos publicos clave:**
- `SetLanguage(string)` - Cambia idioma, recachea, notifica todos + listeners
- `GetValue(string key)` - Traduccion con cache + fallback en cascada
- `GetPluralValue(string key, int count)` - Pluralizacion (_zero, _one, _other). {n} -> numero
- `GetCurrentLanguage()` / `GetLanguageCount()` / `IsReady()` / `HasLanguage()` / `GetAvailableLanguages()`
- `RegisterListener(UdonSharpBehaviour)` - Registra listener externo
- `RegisterLocalizer(TextLocalizer)` / `RegisterCanvasLocalizer(CanvasLocalizer)`
- `OnLanguageDropdownChanged()` - Callback del dropdown
- `SetLanguageJapanese()`, `SetLanguageEnglish()`, etc. - Para SendCustomEvent

**Nota:** Sin campos publicos duplicados. `_currentLanguage` es la unica fuente de verdad.
Cache en `_currentLangDict` y `_fallbackLangDict`.

### TextLocalizer.cs

- Soporta parametros dinamicos `{0}`, `{1}`, `{2}` via `SetParams()`, `SetParams2()`, `SetParams3()`
- Rich text usa `{t}` como placeholder (retrocompatible con `{0}` si no hay params)
- Auto-detecta Text o TextMeshProUGUI

### CanvasLocalizer.cs

- Arrays paralelos: tmpTexts[]/tmpKeys[], legacyTexts[]/legacyKeys[]
- Inspector con dropdown de idioma base (11 idiomas)
- Gizmo en Scene View: `[Idiomas:canvasId] N textos`

---

## Idiomas Soportados

| Codigo | Idioma | Fuentes CJK |
|--------|--------|-------------|
| `en` | English | No necesita |
| `es` | Espanol | No necesita |
| `ja` | Japanese | VRChat Noto Sans (runtime) |
| `ko` | Korean | VRChat Noto Sans (runtime) |
| `zh-CN` | Chinese Simplified | VRChat Noto Sans (runtime) |
| `zh-TW` | Chinese Traditional | VRChat Noto Sans (runtime) |
| `ru` | Russian | VRChat Noto Sans (runtime) |
| `pt-BR` | Portuguese | No necesita |
| `fr` | French | No necesita |
| `de` | German | No necesita |
| `ca` | Catalan | No necesita |

**Nota sobre fuentes:** Los caracteres CJK y cirilicos se renderizan con las fuentes
internas del cliente de VRChat (Noto Sans). No se incluyen fuentes SDF propias.
Para que funcione, la lista de Fallback Font Assets en TMP Settings debe estar vacia.
En el editor de Unity los caracteres CJK se ven como cuadrados — esto es normal.

---

## Restricciones Criticas de UdonSharp

- **NUNCA** `AddComponent<UdonSharpBehaviour>()` desde editor scripts
- **No** `List<T>` ni `Array.Resize` en runtime (crear array nuevo cada vez)
- **SendCustomEvent** solo acepta metodos sin parametros
- **TMP_Dropdown** en World Space: canvas 500px+ alto, sin scroll, crear en Prefab Mode

---

## Historial de Versiones

- **v1.0.5** (2026-03-29): Fix assembly references para UdonBehaviour
- **v1.0.6** (2026-03-31): 15 mejoras (cache, pluralizacion, params, listeners, CSV, gizmo, traducciones corregidas, timezone IANA, validacion mejorada, API documentada)

## Problemas Conocidos

1. **LanguageDropdown.cs** - Existe pero no se usa. Marcado [OPCIONAL].
2. **Catalan por timezone** - No distinguible de Espana por zona horaria.
