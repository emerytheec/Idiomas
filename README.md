# Idiomas - Sistema de Localizacion para VRChat

Sistema de localizacion standalone para mundos de VRChat usando UdonSharp.
Traduce automaticamente todos los textos de un Canvas a multiples idiomas.

**100% independiente.** No requiere YamaPlayer ni ningun otro sistema externo.

---

## Instalacion

### Paso 1: Importar al proyecto

Copia la carpeta `Assets/Idiomas/` completa a tu proyecto de Unity.

### Paso 2: Fuentes CJK (japones, coreano, chino, ruso)

El sistema soporta 11 idiomas incluyendo japones, chino, coreano y ruso.
Estos caracteres se renderizan automaticamente con las fuentes internas
del cliente de VRChat (Noto Sans CJK) en runtime.

**No necesitas configurar fuentes adicionales.**

Para que las fuentes de VRChat funcionen, la lista de **Fallback Font Assets**
en TMP Settings (`Edit > Project Settings > TextMeshPro Settings`) debe estar
**vacia**. Si hay fuentes custom ahi, VRChat las usa en vez de las suyas.

**Nota:** En el editor de Unity (Play mode local), los caracteres CJK pueden
aparecer como cuadrados. Esto es normal — al subir el mundo a VRChat se
renderizan correctamente.

### Paso 3: Agregar el prefab a tu escena

1. En el Project, ve a `Assets/Idiomas/Prefabs/`
2. Arrastra **`LocalizationManager`** a tu escena (Hierarchy)
3. Esto incluye el LocalizationManager + el selector de idioma (dropdown)
4. Posiciona el LanguageSelector (canvas) donde quieras en tu mundo

### Paso 4: Conectar el dropdown (primera vez)

1. Selecciona el **LocalizationManager** en la Hierarchy
2. En el Inspector, verifica que **Dropdown** esta asignado
3. Verifica que **Codigos de Idioma** tiene 12 elementos
4. Si aparece el boton amarillo **"Conectar Dropdown"**, haz clic en el
5. Si no aparece, ya esta conectado

---

## Traducir un Canvas

### Metodo rapido: CanvasLocalizer (un componente por canvas)

1. Selecciona el **GameObject raiz** de tu Canvas
2. **Add Component** → busca **CanvasLocalizer**
3. Configura:
   - **Manager**: arrastra el LocalizationManager de la escena
   - **ID del Canvas**: un nombre unico (ej: `settings`, `lobby`, `hud`)
   - **Idioma Base**: el idioma en que estan escritos los textos actuales (ej: `en`)
4. En el Inspector, clic en **"Escanear Canvas"**
   - Detecta automaticamente todos los textos (TextMeshPro y Text legacy)
   - Muestra una tabla con las claves generadas
   - Puedes excluir textos que no deben traducirse (numeros, iconos)
   - Puedes editar las claves manualmente
5. Clic en **"Exportar al JSON y Aplicar"**
   - Guarda los textos originales al archivo JSON bajo el idioma base
   - Si no existe archivo JSON, lo crea automaticamente
   - Llena los arrays del componente

### Metodo individual: TextLocalizer (un componente por texto)

Para textos sueltos que no estan en un canvas:

1. Selecciona el GameObject con Text o TextMeshProUGUI
2. **Add Component** → busca **TextLocalizer**
3. Asigna el **LocalizationManager**
4. Escribe la **clave de traduccion** (ej: `btn_start`)

---

## Traducir a otros idiomas

### Automatico: MyMemory API (gratis)

1. Selecciona el **LocalizationManager** en la Hierarchy
2. En el Inspector, clic en **"Auto-Traducir Idiomas Faltantes"**
3. Se abre una ventana:
   - Selecciona el idioma origen (el que tiene mas claves)
   - Marca los idiomas destino
   - Clic en **"Traducir"**
4. Revisa las traducciones en la vista previa
5. Clic en **"Guardar al JSON"**

API MyMemory: gratis, sin registro, 5000 palabras por dia.
Las traducciones marcadas [TODO:xx] fallaron y necesitan traduccion manual.

### Manual

Edita directamente el archivo JSON en `Assets/Idiomas/Data/translation.json`.
Formato:

```json
{
    "en": {
        "mi_clave": "English text"
    },
    "es": {
        "mi_clave": "Texto en espanol"
    },
    "ja": {
        "mi_clave": "日本語テキスト"
    }
}
```

---

## Idiomas soportados

| Codigo | Idioma | Caracteres especiales |
|--------|--------|----------------------|
| `en` | English | No |
| `es` | Espanol | No |
| `ja` | Japanese | Si (requiere fallback fonts) |
| `ko` | Korean | Si (requiere fallback fonts) |
| `zh-CN` | Chinese Simplified | Si (requiere fallback fonts) |
| `zh-TW` | Chinese Traditional | Si (requiere fallback fonts) |
| `ru` | Russian | Si (requiere fallback fonts) |
| `pt-BR` | Portuguese | No |
| `fr` | French | No |
| `de` | German | No |
| `ca` | Catalan | No |

Puedes agregar cualquier idioma editando el JSON. El sistema detecta
automaticamente los idiomas disponibles.

---

## Componentes

### LocalizationManager
El cerebro del sistema. Solo necesitas **uno por escena**.

- Carga el JSON de traducciones
- Detecta automaticamente el idioma del jugador (VRChat API)
- Fallback inteligente: idioma del jugador → variantes → zona horaria → fallback
- Gestiona el dropdown de seleccion de idioma
- Notifica a todos los CanvasLocalizer y TextLocalizer cuando cambia el idioma

### CanvasLocalizer
Se coloca en el **raiz de un Canvas** y traduce todos los textos hijos.

- Escanea automaticamente TMP_Text y Text legacy
- Genera claves de traduccion desde la jerarquia de GameObjects
- Exporta textos al JSON automaticamente
- Un solo componente por canvas (no uno por texto)

### TextLocalizer
Se coloca en **un solo texto** para traduccion individual.

- Para textos sueltos fuera de canvas
- Soporta prefijo, sufijo y rich text
- Se puede cambiar la clave en runtime

---

## Deteccion Automatica de Idioma

Al iniciar, el sistema detecta el idioma del jugador:

1. **API de VRChat**: `VRCPlayerApi.GetCurrentLanguage()`
2. **Variantes**: `es-CL` → busca `es` si no existe `es-CL`
3. **Zona horaria**: Tokyo → `ja`, Madrid → `es`, etc.
4. **Fallback**: usa el idioma por defecto (`en`)

---

## Herramientas del Editor

### Inspector del LocalizationManager
- **Dropdown**: configuracion del selector de idioma
- **Auto-buscar**: encuentra todos los localizers de la escena
- **Auto-Traducir**: traduce idiomas faltantes con MyMemory API
- **Validacion**: detecta claves faltantes en algun idioma
- **Vista Previa**: muestra traducciones sin entrar en Play Mode

### Inspector del CanvasLocalizer
- **Escanear Canvas**: detecta todos los textos automaticamente
- **Tabla de resultados**: editar claves, excluir textos
- **Exportar al JSON**: guarda textos y crea el archivo si no existe

---

## Estructura de Archivos

```
Assets/Idiomas/
├── Runtime/
│   ├── LocalizationManager.cs    # Cerebro del sistema
│   ├── CanvasLocalizer.cs        # Localizador de canvas completo
│   ├── TextLocalizer.cs          # Localizador de texto individual
│   ├── LanguageDropdown.cs       # Dropdown de idioma (opcional)
│   ├── LanguageSelector.cs       # Selector legacy (botones)
│   └── LanguageButton.cs         # Boton de idioma legacy
├── Editor/
│   ├── LocalizationManagerEditor.cs   # Inspector del Manager
│   ├── CanvasLocalizerEditor.cs       # Inspector del CanvasLocalizer
│   ├── AutoTranslateWindow.cs         # Ventana de auto-traduccion
│   └── IdiomasPrefabCreator.cs        # Creador de demos
├── Data/
│   └── translation.json               # Traducciones (se crea automatico)
├── Prefabs/
│   └── LocalizationManager.prefab     # Prefab listo para usar
└── README.md
```

---

## API Publica (para otros scripts UdonSharp)

### LocalizationManager

| Metodo | Descripcion |
|--------|-------------|
| `SetLanguage(string lang)` | Cambia el idioma activo. `null` = auto-detectar. |
| `GetValue(string key)` | Obtiene la traduccion de una clave. Fallback en cascada. |
| `GetPluralValue(string key, int count)` | Traduccion con pluralizacion (`_zero`, `_one`, `_other`). `{n}` se reemplaza por el numero. |
| `GetCurrentLanguage()` | Devuelve el codigo del idioma activo (ej: `"es"`). |
| `GetLanguageCount()` | Numero de idiomas disponibles. |
| `GetAvailableLanguages()` | Array de codigos de idioma. |
| `HasLanguage(string lang)` | `true` si el idioma existe en el JSON. |
| `IsReady()` | `true` cuando el sistema esta inicializado. |
| `RegisterListener(UdonSharpBehaviour)` | Registra un listener que recibe `_OnLanguageChanged` al cambiar idioma. |
| `RegisterLocalizer(TextLocalizer)` | Registra un TextLocalizer en runtime. |
| `RegisterCanvasLocalizer(CanvasLocalizer)` | Registra un CanvasLocalizer en runtime. |

### TextLocalizer

| Metodo | Descripcion |
|--------|-------------|
| `UpdateText()` | Actualiza el texto con la traduccion actual. |
| `SetTranslationKey(string key)` | Cambia la clave en runtime. |
| `SetParams(string p0)` | Reemplaza `{0}` en la traduccion y actualiza. |
| `SetParams2(string p0, string p1)` | Reemplaza `{0}` y `{1}`. |
| `SetParams3(string p0, string p1, string p2)` | Reemplaza `{0}`, `{1}` y `{2}`. |

### Pluralizacion

Convencion de claves en el JSON:

```json
{
    "en": {
        "players_zero": "No players",
        "players_one": "1 player",
        "players_other": "{n} players"
    },
    "es": {
        "players_zero": "Sin jugadores",
        "players_one": "1 jugador",
        "players_other": "{n} jugadores"
    }
}
```

En tu script: `manager.GetPluralValue("players", count)`

### Texto con parametros

En el JSON: `"welcome": "Hola {0}, tienes {1} mensajes"`

En tu script con TextLocalizer:
```
textLocalizer.SetParams2("Bender", "5");
// Resultado: "Hola Bender, tienes 5 mensajes"
```

### Listener de cambio de idioma

Para que tu script reaccione cuando el jugador cambia de idioma:

1. Agrega tu UdonSharpBehaviour al array "Listeners" del LocalizationManager.
2. Crea un metodo publico `_OnLanguageChanged()` en tu script.
3. Se llamara automaticamente cada vez que cambie el idioma.

### Exportar/Importar CSV

Menu: `Tools > Idiomas > Exportar-Importar CSV`

Permite exportar las traducciones a CSV para editarlas en Google Sheets o Excel,
y luego importarlas de vuelta al JSON.

---

## Persistencia de seleccion de idioma

Actualmente, si un jugador elige un idioma manualmente y vuelve a entrar al mundo,
el sistema auto-detecta de nuevo. Para persistir la eleccion:

1. Registra un listener con `manager.RegisterListener(this)`.
2. En `_OnLanguageChanged()`, guarda `manager.GetCurrentLanguage()` con tu sistema de persistencia.
3. En `Start()`, lee el idioma guardado y llama `manager.SetLanguage(idiomaGuardado)`.

VRChat ofrece PlayerData para persistencia entre sesiones.

---

## Compatibilidad

- Unity 2022.3.x (requerido por VRChat)
- VRChat SDK Worlds >= 3.8.1
- UdonSharp (incluido en VRChat SDK)
- TextMeshPro (incluido en Unity)
- Compatible con Quest y PC

---

## Licencia

Libre para uso personal y comercial. Creditos apreciados pero no requeridos.
