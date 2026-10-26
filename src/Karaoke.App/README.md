# Karaoke Interactivo — Daniel León

Programa de karaoke interactivo para shows en vivo (botonera tipo Jingle Palette, asistente de voz,
adivina la canción, pulsadores, calificación y marcador). **Estado: ETAPA 2 de 8 (audio real, sin probar).**

## Estado honesto de esta entrega

> **Nada de este código ha sido compilado ni ejecutado todavía.** Se escribió en un entorno sin .NET ni Windows.
> Lo primero que debes hacer es compilar y ejecutar las pruebas (ver abajo) y reportar cualquier error.

| Función | Estado |
|---|---|
| Estructura de solución y módulos (`Core` + `App` + `Tests`) | Escrito, **sin compilar** |
| Interfaz principal según la imagen (cuadrícula 9×4, equipos, encabezado, tiempo, medidores, controles) | Escrita, **sin compilar ni ver en pantalla** |
| Gestión central de audio (una reproducción por canal, doble pulsación, pausa ≠ detener, DETENER TODO con token) | Lógica escrita + 9 pruebas, **sin ejecutar** |
| Marcador rojo/azul, deshacer, reiniciar con confirmación | Escrito + 5 pruebas, **sin ejecutar** |
| Temporizador (misma ronda no reinicia, DETENER TODO lo cancela) | Escrito + 4 pruebas, **sin ejecutar** |
| Persistencia en `%AppData%\KaraokeInteractivo` (escritura atómica, respaldo si el JSON se daña) | Escrita + 3 pruebas, **sin ejecutar** |
| Pantalla completa (botón verde y F11), Salir con confirmación, cambio de paleta, guardar paleta | Escritos, **sin probar** |
| Audio real: música (NAudio) y voz (voces de Windows, sin Internet), instrucciones leídas en voz alta | Escrito, **sin compilar ni escuchar** |
| Medidores L/R con el nivel pico real de los archivos (la voz sintética no se mide) | Escrito, **sin probar** |
| Calificación, pulsadores, edición de botones, Configuración, logotipos, instrucciones en voz | **PENDIENTES** (etapas 2–6) |
| Compilación probada en Win10 x86 / x64 / Win11 | **NO probada** |

Voz: Windows necesita una voz en español instalada (Configuración > Hora e idioma > Voz). Si no hay, se usa la predeterminada.

(Obsoleto) Mientras `DemoMode = true` (en `MainWindow.xaml.cs`), los botones sin archivo asignado "suenan" en modo simulado
para poder validar el estado visual (borde dorado, ecualizador, temporizador, texto del panel).

## Requisitos

- Windows 10/11 con **.NET 10 SDK** (.NET 8 y 9 terminan su soporte el 10-nov-2026).
- Para Windows 10 de 32 bits hay que **verificar** en la documentación de .NET 10 que tu versión de Windows 10
  está soportada para x86 antes de comprometerse con esa arquitectura.

## Compilar, probar y ejecutar

```powershell
dotnet test                                   # pruebas de la lógica (no requieren Windows)
dotnet run --project src/Karaoke.App          # abrir el programa
```

## Publicar un ejecutable por arquitectura

```powershell
dotnet publish src/Karaoke.App -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x86
dotnet publish src/Karaoke.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

No declarar compatibilidad con una arquitectura hasta ejecutar su `.exe` en un Windows real de esa arquitectura.

## Estructura

```
src/Karaoke.Core   Lógica sin interfaz (multiplataforma, probable): audio, marcador, temporizador, modelos, persistencia, registros
src/Karaoke.App    Interfaz WPF (solo Windows)
tests/             Pruebas xUnit del Core
```

Datos del usuario: `%AppData%\KaraokeInteractivo\settings.json`. Registros: `...\Logs\karaoke-AAAAMMDD.log`.

## Decisiones tomadas por mí (cámbialas si no te convienen)

1. **Canales de audio independientes:** voz, música y celebración pueden sonar a la vez (una celebración sobre la música).
   Dentro de cada canal solo suena uno. Si quieres que una celebración corte la música, es un cambio pequeño en `AudioCoordinator`.
2. Cambiar de paleta durante el show **detiene todo el audio** de la paleta anterior.
3. Los botones circulares de la imagen (pausa) son por ahora rectangulares con esquinas redondeadas.
4. Los fondos del escenario, micrófonos y logotipos de la imagen **no se copiaron**: son tus imágenes.
   La ventana usa un degradado y logotipos reemplazables (se asignarán desde Configuración > Logotipos).
5. Marcador, hecho para la sesión: se reinicia al cerrar (guardarlo entre sesiones se decide en la etapa 4).

## Licencias

Pendiente de elegir la licencia del proyecto. En esta etapa no hay paquetes de terceros en la app
(las pruebas usan xUnit, Apache-2.0). NAudio (MIT) y System.Speech se añadirán en la etapa 2 con su licencia documentada.
No incluir canciones comerciales en el repositorio.
