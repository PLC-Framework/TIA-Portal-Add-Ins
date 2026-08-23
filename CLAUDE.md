# CLAUDE.md — tia-portal-addins

> Este archivo se lee automáticamente por Claude Code al iniciar sesión en este proyecto. Colócalo en la raíz de `tia-portal-addins\` (junto al .slnx).

## Resumen del proyecto

Solución de Visual Studio para un **Add-In de TIA Portal** (Siemens Openness) más varias **apps satélite con UI** que el Add-In lanza al disparar eventos. Todo gira en torno a un único Add-In principal — si en el futuro se crean más Add-Ins, tendrán otro nombre de proyecto, no reutilizan "AddIn".

- Carpeta/solución: `E:\PlcFramework\tia-portal-addins\`
- Archivo de solución: `tia-portal-addins.slnx` (formato XML nuevo, NO `.sln` clásico)
- Target frameworks: **.NET Framework 4.8** para todo lo relacionado con TIA Portal/Openness (WPF incluido)

## Estado actual (verificado 2026-08-23)

- [x] Solución `tia-portal-addins` creada desde Visual Studio — `tia-portal-addins.slnx` sigue **vacía** (`<Solution />`); la carpeta `src/` aún no existe
- [x] DLLs de Siemens copiados localmente en `E:\PlcFramework\.lib\Siemens\PublicAPI\...` (una carpeta por versión, ver tabla más abajo)
- [x] `Siemens.Engineering.AddIn.Publisher.exe` ya copiado (V20 y V21)
- [ ] Ningún proyecto C# existe todavía en esta solución — hay que crearlos todos

> Nota: el proyecto `AddIn` que figuraba como creado en versiones anteriores de este archivo ya no existe en disco. Se parte de cero con `AddIn.V20` / `AddIn.V21`, pero **reaprovechando el proyecto previo de referencia** (ver sección siguiente).

## Proyecto previo de referencia — `add-in-for-tia-portal`

En `E:\PlcFramework\add-in-for-tia-portal\` hay un **Add-In anterior ya funcional** (repo git propio, último toque 2026-08-22). No forma parte de esta solución, pero es la **fuente de verdad práctica**: ahí ya está resuelto el csproj, el `Config.xml`, el target de MSBuild que invoca al Publisher y el uso real de la API. Su `README.md` documenta los tropiezos ya sufridos — leerlo antes de escribir nada.

Piezas reutilizables:

| Archivo | Qué aporta |
|---|---|
| `PlcFramework.TiaAddIn.csproj` | csproj SDK-style `net48`, `x64`, referencias con `<Private>False</Private>`, target `PublishTiaAddIn` |
| `Config.xml` | `PackageConfiguration` real y validado (permisos TIA + `SecurityPermissions`) |
| `AddInProvider.cs` / `AddInController.cs` | uso real de `ProjectTreeAddInProvider` y `ContextMenuAddIn` |
| `Util\`, `UserApp\`, `RemoteRepository\`, `Actions\` | lógica de dominio candidata a migrar a `Core` |

⚠️ Su `RootNamespace` y sus namespaces son `PLC-Framework.TiaAddIn` — **con guion, que C# no admite en un namespace**. Al migrar hay que renombrarlos (p. ej. `PlcFramework.TiaAddIn`).

## Decisiones de arquitectura

1. **Capas**: `Core` (sin dependencias de Siemens) → `TiaAdapter`/Add-In → satélites. `Core` es lo único compartible sin fricción.
2. **Multi-versión TIA Portal**: V17–V20 comparten el mismo Openness API (binariamente compatibles); **V21 introduce breaking changes**. Por eso hacen falta **dos proyectos de Add-In separados**: `AddIn.V20` (válido V17–V20) y `AddIn.V21`.
   - Confirmado en disco: hasta V20 el API es monolítico (`Siemens.Engineering.dll`, `Siemens.Engineering.AddIn.dll`). En **V21 los ensamblados están troceados**: `Siemens.Engineering.Base.dll`, `.Step7.dll`, `.WinCC.dll`, `.WinCCUnified.dll`, `.Safety.dll`, `.CFC.dll`, `.DCC.dll`, `.Startdrive.dll`, `.TeamcenterGateway.dll` y, del lado Add-In, `Siemens.Engineering.AddIn.Base.dll` / `.Step7.dll` / `.Safety.dll`. **No existe `Siemens.Engineering.dll` en V21.**
3. **Satélites**: apps WPF independientes, lanzadas por el Add-In vía `Process.Start` cuando ocurre un evento (botón de ribbon, etc.).
4. **Comunicación Add-In ↔ satélites**:
   - Si el satélite solo necesita un snapshot al abrir → pasar JSON por argumento o archivo temporal.
   - Si necesita consultar TIA en vivo mientras está abierto → Named Pipes, servidor en el Add-In, contrato de mensajes en un proyecto `IPC` compartido.
5. **Evitar duplicados**: cada satélite usa un `Mutex` con nombre al iniciar para no abrir dos instancias.

## Estructura de proyectos objetivo

```
tia-portal-addins.slnx
└── src/
    ├── Core/                  (net48) — modelos e interfaces, SIN referencia a Siemens.Engineering
    ├── AddIn.V20/              (net48) — referencia PublicAPI\V20 + V20.addIn (válido V17–V20)
    ├── AddIn.V21/              (net48) — referencia PublicAPI\V21\net48
    ├── IPC/                    (net48) — contrato Add-In ↔ satélites (Named Pipes)
    ├── Satellite.Shared/       (net48, WPF, <UseWPF>true</UseWPF>) — estilos/controles comunes
    └── Satellite.<Nombre>/     (net48, WPF) — un proyecto por app satélite (ej. SnapshotDB)
```

## Rutas locales de DLLs y herramientas (ya copiadas)

Raíz: `E:\PlcFramework\.lib\Siemens\PublicAPI\` — **una carpeta por versión**, no una única "V17 to V20".

| Carpeta | Contenido |
|---|---|
| `V17\`, `V18\`, `V19\`, `V20\` | `Siemens.Engineering.dll`, `Siemens.Engineering.Hmi.dll` |
| `V17.AddIn\`, `V18.AddIn\`, `V19.AddIn\` | `Siemens.Engineering.AddIn.dll`, `.AddIn.Permissions.dll`, `.AddIn.Utilities.dll`, `Siemens.Engineering.Hmi.dll` |
| `V20.addIn\` | lo anterior **+ `Siemens.Engineering.AddIn.Publisher.exe` + `Siemens.Engineering.AddIn.DebugStarter.exe`** (ojo: la carpeta se llama `V20.addIn`, con `a` minúscula) |
| `V21\` | `Siemens.Engineering.AddIn.Publisher.exe` |
| `V21\net48\` | ensamblados troceados de V21 (`Siemens.Engineering.Base.dll`, `.Step7.dll`, `.WinCC.dll`, `.WinCCUnified.dll`, `.Safety.dll`, `.SafetyValidation.dll`, `.CFC.dll`, `.DCC.dll`, `.Startdrive.dll`, `.TeamcenterGateway.dll`, `.WinCC.Extension.dll`, `Siemens.Engineering.AddIn.Base.dll`, `.AddIn.Step7.dll`, `.AddIn.Safety.dll`, `.AddIn.Permissions.dll`, `.AddIn.Utilities.dll`) |
| `.doc\TIA-Openness\TIA Add-in Tester\` | **TIA Add-in Tester v1.1.6557.1192** (entrada 109783096) — permite probar el Add-In sin abrir TIA Portal |

Otras herramientas:

| Ruta | Contenido |
|---|---|
| `E:\PlcFramework\.lib\Siemens\Support\TIA_Portal_Add-In_Tools\Development\` | `.nupkg` + `.vsix` — plantilla oficial de VS para Add-Ins (válida desde TIA V18+) |
| `E:\PlcFramework\.lib\Siemens\Support\TIA_Portal_Add-In_Tools\Trusted_Add-Ins_Certification_Tool\` | `Company_Trusted_Add-In_Certification_Tool.exe` — firma el `.addin` como "confiable" |

## Mecánica de build/deploy de un Add-In (proceso oficial Siemens)

1. Compilar el proyecto como `.dll` normal.
2. Invocar el Publisher. **La sintaxis real es con flags**, no posicional (verificado en el proyecto previo):

   ```
   Siemens.Engineering.AddIn.Publisher.exe --configuration <Config.xml> --outfile <salida.addin> --console
   ```

   Usar el Publisher **de la versión correspondiente**:

   | Proyecto | Ruta del Publisher |
   |---|---|
   | `AddIn.V20` (V17–V20) | `E:\PlcFramework\.lib\Siemens\PublicAPI\V20.addIn\Siemens.Engineering.AddIn.Publisher.exe` |
   | `AddIn.V21` | `E:\PlcFramework\.lib\Siemens\PublicAPI\V21\Siemens.Engineering.AddIn.Publisher.exe` — **suelto en `V21\`, no dentro de `V21\net48\`** |

   ⚠️ **Gotcha ya sufrido**: el Publisher resuelve el `<Assembly>` del config **relativo a la ubicación del propio archivo de config**, no al working directory. Solución del proyecto previo: un target `AfterTargets="Build"` que escribe una copia con ruta absoluta en `obj/…/Config.generated.xml` y se la pasa al Publisher, dejando `bin/` limpio.

3. Cada Add-In necesita su propio `Config.xml` (metadatos, permisos), validado contra `Siemens.Engineering.AddIn.Publisher.xsd` — presente en `V20.addIn\` y en `V21\`. El `xmlns` del `PackageConfiguration` es **por versión**: `http://www.siemens.com/automation/Openness/AddIn/Publisher/V20`.
4. Copiar el `.addin` resultante a: `%AppData%\Siemens\Automation\Portal VXX\UserAddIns` — **ojo: es `UserAddIns`, no `AddIns`**. La carpeta no existe hasta que se crea a mano.
5. Opcional: firmar con `Company_Trusted_Add-In_Certification_Tool.exe` para marcarlo como "confiable" (TIA Portal distingue 3 niveles: confiable / sin firmar-inválido / revocado-modificado).

## Requisitos de entorno (verificado 2026-08-23 en `YF-CONTROLS-C5`)

- **TIA Portal NO está instalado en esta máquina.** `C:\Program Files\Siemens\Automation\Portal V20\` solo contiene `PublicAPI\V20.AddIn` (restos de la copia de DLLs), no el producto.
- Consecuencia: **no existe el grupo local "Siemens TIA Openness"** (comprobado con `net localgroup`) y no existe `%AppData%\Siemens\Automation\Portal V20\UserAddIns`.
- Impacto real: **compilar sí funciona** (las referencias se resuelven contra los DLLs de `.lib\`), pero **ejecutar/depurar el Add-In no** — eso requiere TIA Portal instalado y pertenecer al grupo.
- Visual Studio: workload **".NET desktop development"** + componente **".NET Framework 4.8 targeting pack"**.
- Instalar el `.vsix` oficial en Visual Studio para obtener la plantilla de proyecto correcta de Siemens, en vez de partir de una Class Library manual (pendiente de probar).

## Pendiente / próximos pasos

- [ ] **Decidir**: migrar el código de `add-in-for-tia-portal` a esta solución, o empezar limpio usando solo su csproj/Config.xml como plantilla
- [ ] Instalar y probar la plantilla oficial (`.vsix`) de Siemens para Add-Ins **antes** de crear proyectos a mano
- [ ] Crear proyecto `Core` y añadirlo al `.slnx` (la solución está vacía)
- [ ] Crear `AddIn.V20` y `AddIn.V21`
- [ ] Crear `Config.xml` por cada Add-In (V21 con su propio `xmlns` `.../Publisher/V21`)
- [ ] Configurar el target de post-build: Publisher.exe (el de su versión) → copiar a `UserAddIns`
- [ ] Instalar TIA Portal + añadirse al grupo "Siemens TIA Openness" para poder ejecutar/depurar
- [ ] Probar el ciclo de desarrollo con el **TIA Add-in Tester** y/o `Siemens.Engineering.AddIn.DebugStarter.exe`
- [ ] Definir cuántas apps satélite habrá y si necesitan datos de TIA en vivo (Named Pipes) o solo snapshot
- [ ] Crear proyecto `IPC` y `Satellite.Shared`

## Notas técnicas importantes

- El código de ejemplo tipo `AddInBase` / `SessionInitialize` usado en borradores anteriores **no es la API real** de Siemens para Add-Ins. La API real usa providers concretos como `ProjectTreeAddInProvider`, `ProjectLibraryTreeAddInProvider` y `ContextMenuAddin.GetContextMenuAddIns`. Usar la plantilla oficial (`.vsix`) o la documentación de Siemens como fuente de verdad antes de escribir el código real del Add-In.
- Los nombres de proyecto en C# no admiten guiones. El nombre de la solución (`tia-portal-addins`) sí puede llevarlos porque es solo un nombre de archivo/carpeta — los proyectos van en PascalCase sin guiones (`AddIn.V20`, `Core`, etc.).
- `Siemens.Engineering.dll` y compañía se referencian con `Copy Local = False` (o `<Private>False</Private>` en el `.csproj`) porque deben resolverse contra la instalación local de TIA Portal, no copiarse junto al Add-In.
- **`PlatformTarget` debe ser `x64`** — TIA Portal V20 es solo 64 bits.

### Gotchas de referencias ya verificados (proyecto previo)

- `Siemens.Engineering.AddIn.dll` **ya lleva embebido su propio object model core** (`TiaPortal`, `Project`, `IEngineeringObject`, transacciones…) bajo el namespace `Siemens.Engineering`. Para un Add-In básico **no hace falta referenciar `Siemens.Engineering.dll`**.
- Referenciar **ambos** (`Siemens.Engineering.dll` + `Siemens.Engineering.AddIn.dll`) rompe la compilación: definen copias **distintas e incompatibles** de `Siemens.Engineering.IEngineeringObject`, que pasa a ser ambiguo. Hace falta cuando se necesitan namespaces no embebidos (`Siemens.Engineering.HW.*`, `Siemens.Engineering.SW.Blocks.*`).
- Solución: alias en la referencia del Add-In (`<Aliases>TiaAddIn</Aliases>`) y en código `extern alias TiaAddIn;` + `using AddInEngineering = TiaAddIn::Siemens.Engineering;`. Los genéricos de menú/selección (`ContextMenuAddIn`, `MenuSelectionProvider<T>`, `ChildItemFactory.AddActionItem<T>`) deben usar `AddInEngineering.IEngineeringObject`, no el `IEngineeringObject` plano.
- `Siemens.Engineering.AddIn.dll`, `.AddIn.Permissions.dll`, `.AddIn.Utilities.dll` y `Siemens.Engineering.Hmi.dll` **sí conviven sin alias** entre ellos.
- La mayoría de miembros de `MenuSelectionProvider` (p. ej. `GetSelectedObjectCount`) son `internal` en el ensamblado de Siemens → usar los públicos `GetSelection()` / `GetSelection<T>()`.
- Para que el Add-In pueda lanzar los satélites con `Process.Start`, el `Config.xml` debe declarar `<Siemens.Engineering.AddIn.Permissions.ProcessStartPermission/>` dentro de `<SecurityPermissions>`.