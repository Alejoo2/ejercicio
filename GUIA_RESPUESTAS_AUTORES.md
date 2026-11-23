# Guía: módulo Autores — de excepciones a respuestas con mensaje

> Objetivo: que **todas** las respuestas del módulo Autores lleguen con el mismo formato JSON y un mensaje claro, sin usar `throw` para los errores esperados (no existe, tiene libros, etc.).
>
> Esta guía está pensada para hacerla **en grupo, paso a paso**. Cada paso muestra el **código original** y, debajo, el **código nuevo**. Al final de cada paso el proyecto debe compilar (`Ctrl+Shift+B`).

---

## 1. Estructura actual del proyecto

```
BiblioTech-Esqueleto/
├── BiblioTech2.slnx                  → solución
├── BiblioTech2.slnLaunch             → perfil "Api + Web" (arranca los dos)
├── README.md
├── database/
│   └── BiblioTech.sql                → crea la BD BiblioTechManual (tablas, vista, SP, datos)
│
├── BiblioTech2.Api/                  → API REST (la ÚNICA que toca la BD)
│   ├── Program.cs                    → registro de servicios + pipeline HTTP
│   ├── appsettings.json              → cadena de conexión y CORS
│   ├── BiblioTech2.Api.http          → pruebas rápidas de endpoints
│   ├── Configuracion/                → (vacía)
│   ├── Constants/                    → (vacía)  ← aquí irán los mensajes
│   ├── Controllers/
│   │   └── AutoresController.cs      → recibe la petición HTTP y llama al servicio
│   ├── Data/
│   │   ├── BibliotecaContext.cs      → DbContext de EF Core (Database First)
│   │   └── PlurizadorEspanol.cs      → pluralizador para el scaffold
│   ├── DTOs/Autores/
│   │   ├── AutorDto.cs               → lo que la API DEVUELVE
│   │   └── AutorGuardarDto.cs        → lo que la API RECIBE (con validaciones)
│   ├── Exceptions/
│   │   ├── NoEncontradoException.cs  → se eliminará
│   │   └── ReglaNegocioException.cs  → se eliminará
│   ├── Middleware/
│   │   └── ErrorHandlingMiddleware.cs→ convierte excepciones en JSON
│   ├── Models/                       → entidades generadas desde la BD
│   │   ├── Autor.cs, Libro.cs, Prestamo.cs, Socio.cs, Usuario.cs
│   │   └── VwPrestamosVencido.cs     → vista
│   └── Services/
│       ├── Interfaces/IAutorService.cs
│       └── AutorService.cs           → lógica de negocio + acceso a datos
│
└── BiblioTech2.Web/
    ├── BiblioTech2.Web/              → servidor Blazor (entrega la app al navegador)
    └── BiblioTech2.Web.Client/       → app WebAssembly (corre en el navegador, pide datos a la API)
```

### Cómo viaja hoy una petición

```
Cliente (Scalar / Blazor)
   │  GET /api/autores/5
   ▼
[Pipeline Program.cs]  →  AutoresController.obtener(5)
                              │
                              ▼
                          AutorService.ObtenerAsync(5)
                              │  si no existe → throw NoEncontradoException
                              ▼
                 (la excepción "sube" buscando quién la atrape)
                              ▼
                 ErrorHandlingMiddleware → ProblemDetails 404
```

## 2. Problemas que tiene el código original

Conviene revisarlos con el grupo antes de cambiar nada, porque explican por qué el cambio vale la pena:

| # | Dónde | Problema |
|---|---|---|
| 1 | `ErrorHandlingMiddleware.cs` | El método se llama `InvokeAsyng` (con **g**). ASP.NET Core busca `Invoke` o `InvokeAsync`, así que **nunca funcionaría**. |
| 2 | `Program.cs` | El middleware **no está registrado** (`app.UseMiddleware<...>()` no existe). Hoy, si el servicio lanza `NoEncontradoException`, el cliente recibe un **500** en vez de un 404. |
| 3 | `AutorService.EliminarAsync` | `if (cantidadLibros > 0)` no tiene llaves ni `throw`, así que la línea siguiente queda dentro del `if`: **solo borra cuando el autor SÍ tiene libros**, al revés de lo que debería. |
| 4 | `AutoresController.obtener` | Pregunta `autor is null`, pero el servicio nunca devuelve `null` (lanza excepción). Ese `NotFound()` nunca se ejecuta. |
| 5 | Respuestas | Los éxitos devuelven el DTO "pelado" o `204` sin cuerpo; los errores devuelven `ProblemDetails`. **Dos formatos distintos** para el cliente Blazor. |
| 6 | Excepciones | Usar `throw` para algo **esperado** (un id que no existe) es costoso y esconde el flujo: leyendo el servicio no se ve qué responde la API. |

## 3. La idea nueva: *Patrón Resultado*

En lugar de **lanzar** un error, el servicio **devuelve** un objeto que dice si salió bien o mal y con qué mensaje. El controlador solo traduce ese objeto a un código HTTP.

```
AutorService                 AutoresController            Cliente
────────────                 ─────────────────            ───────
return Resultado             Responder(resultado)         {
  .NoEncontrado("...")   →   → 404 + RespuestaApi    →      "exito": false,
                                                            "mensaje": "El autor 5 no existe.",
                                                            "datos": null
                                                          }
```

Las excepciones quedan **solo** para lo inesperado (BD caída, un bug). Para eso se mantiene el middleware como red de seguridad.

### Formato único de respuesta (lo que verá Blazor/Scalar)

```json
// 200 GET /api/autores/1
{ "exito": true,  "mensaje": "Autor encontrado.", "datos": { "id": 1, "nombre": "Gabriel García Márquez", "nacionalidad": "Colombiana", "cantidadLibros": 3 }, "errores": null }

// 404 GET /api/autores/999
{ "exito": false, "mensaje": "El autor 999 no existe.", "datos": null, "errores": null }

// 409 DELETE /api/autores/1
{ "exito": false, "mensaje": "No se puede eliminar el autor porque tiene 3 libro(s) asociado(s).", "datos": null, "errores": null }

// 400 POST /api/autores  (nombre vacío)
{ "exito": false, "mensaje": "Los datos enviados no son válidos.", "datos": null, "errores": { "Nombre": ["El nombre es obligatorio."] } }
```

## 4. Clases nuevas y por qué crearlas

| Archivo nuevo | Carpeta | ¿Para qué sirve? | ¿Por qué no basta con lo que ya hay? |
|---|---|---|---|
| `TipoResultado.cs` (enum) | `Respuestas/` | Dice **qué pasó**: Exito, NoEncontrado, ReglaNegocio, Validacion. | Reemplaza a "qué tipo de excepción fue". El controlador lo usa para escoger 200/404/409/400. |
| `Resultado.cs` (`Resultado` y `Resultado<T>`) | `Respuestas/` | Lo que **devuelve el servicio**: éxito, mensaje, tipo y (si aplica) datos. | Sustituye al `throw`. `Resultado` sin datos para Actualizar/Eliminar; `Resultado<T>` con datos para Listar/Obtener/Crear. |
| `RespuestaApi.cs` (`RespuestaApi<T>`) | `Respuestas/` | La **forma del JSON** que sale por HTTP: `exito`, `mensaje`, `datos`, `errores`. | Hoy éxito y error tienen formatos distintos. Con esta clase el cliente Blazor deserializa siempre lo mismo. Se separa de `Resultado` porque `Resultado` es interno (tiene `Tipo`, que al cliente no le importa) y `RespuestaApi` es el contrato público. |
| `MensajesAutor.cs` | `Constants/` | Todos los textos del módulo en un solo sitio. | Evita textos repetidos/escritos distinto en cada método ("El autor {id} no existe." aparecía 3 veces). Para corregir una falta de ortografía se toca un solo archivo. |
| `ApiControllerBase.cs` | `Controllers/` | Controlador base con el método `Responder(...)` que convierte `Resultado` → código HTTP + `RespuestaApi`. | Si cada controlador (Autores, Libros, Socios…) hiciera su propio `switch` de códigos, se repetiría el mismo código en todos. Se escribe una vez y se hereda. |

Y se **eliminan**: `Exceptions/NoEncontradoException.cs` y `Exceptions/ReglaNegocioException.cs`.

### Diagrama de cómo se relacionan

```
             (servicio)                       (controlador)                 (HTTP)
MensajesAutor ──texto──►  Resultado / Resultado<T>  ──►  ApiControllerBase.Responder ──►  RespuestaApi<T>
                              │ Tipo                         │ switch Tipo → 200/404/409/400
                              ▼                              ▼
                        TipoResultado                  StatusCode(codigo, cuerpo)
```

---

## 5. Paso a paso

> Orden recomendado: primero se crean las clases nuevas (pasos 1–6, el proyecto sigue compilando porque nadie las usa todavía), después se cambia interfaz + servicio + controlador **juntos** (pasos 7–9), y al final middleware, Program.cs y limpieza (pasos 10–12).

### Paso 1 — Crear la carpeta `Respuestas`

Clic derecho en `BiblioTech2.Api` → **Agregar** → **Nueva carpeta** → `Respuestas`.

### Paso 2 — `Respuestas/TipoResultado.cs` (NUEVO)

```csharp
namespace BiblioTech2.Api.Respuestas;

/// <summary>Qué pasó en la operación. El controlador lo traduce a un código HTTP.</summary>
public enum TipoResultado
{
    Exito,          // 200 / 201
    NoEncontrado,   // 404
    ReglaNegocio,   // 409
    Validacion      // 400
}
```

**Explicación:** es el equivalente a "qué excepción se lanzó", pero como un valor. Usar un `enum` evita escribir textos como `"404"` a mano y errores de tipeo.

### Paso 3 — `Respuestas/Resultado.cs` (NUEVO)

```csharp
namespace BiblioTech2.Api.Respuestas;

/// <summary>Resultado de una operación del servicio que NO devuelve datos (Actualizar, Eliminar).</summary>
public class Resultado
{
    public bool Exito { get; }
    public string Mensaje { get; }
    public TipoResultado Tipo { get; }

    protected Resultado(bool exito, string mensaje, TipoResultado tipo)
    {
        Exito = exito;
        Mensaje = mensaje;
        Tipo = tipo;
    }

    public static Resultado Ok(string mensaje) => new(true, mensaje, TipoResultado.Exito);
    public static Resultado NoEncontrado(string mensaje) => new(false, mensaje, TipoResultado.NoEncontrado);
    public static Resultado ReglaNegocio(string mensaje) => new(false, mensaje, TipoResultado.ReglaNegocio);
}

/// <summary>Resultado de una operación del servicio que SÍ devuelve datos (Listar, Obtener, Crear).</summary>
public class Resultado<T> : Resultado
{
    public T? Datos { get; }

    private Resultado(bool exito, string mensaje, TipoResultado tipo, T? datos)
        : base(exito, mensaje, tipo)
    {
        Datos = datos;
    }

    public static Resultado<T> Ok(T datos, string mensaje) => new(true, mensaje, TipoResultado.Exito, datos);
    public static new Resultado<T> NoEncontrado(string mensaje) => new(false, mensaje, TipoResultado.NoEncontrado, default);
    public static new Resultado<T> ReglaNegocio(string mensaje) => new(false, mensaje, TipoResultado.ReglaNegocio, default);
}
```

**Puntos para explicar al grupo:**
- Los constructores son `protected`/`private` y las propiedades solo tienen `get`: **nadie puede armar un Resultado incoherente** (por ejemplo `Exito = true` con `Tipo = NoEncontrado`). Solo se crean con los métodos de fábrica `Ok`, `NoEncontrado`, `ReglaNegocio`.
- `Resultado<T>` **hereda** de `Resultado`. Así el controlador base puede leer `Exito`, `Mensaje` y `Tipo` de cualquiera de los dos.
- La palabra `new` en `NoEncontrado`/`ReglaNegocio` de la clase genérica indica que **ocultan** a los de la clase base para devolver el tipo genérico `Resultado<T>`.

### Paso 4 — `Respuestas/RespuestaApi.cs` (NUEVO)

```csharp
namespace BiblioTech2.Api.Respuestas;

/// <summary>Forma ÚNICA del JSON que la API devuelve siempre (éxito o error).</summary>
public class RespuestaApi<T>
{
    public bool Exito { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public T? Datos { get; set; }
    public Dictionary<string, string[]>? Errores { get; set; }

    public static RespuestaApi<T> Desde(Resultado<T> resultado) => new()
    {
        Exito = resultado.Exito,
        Mensaje = resultado.Mensaje,
        Datos = resultado.Datos
    };

    public static RespuestaApi<T> Desde(Resultado resultado) => new()
    {
        Exito = resultado.Exito,
        Mensaje = resultado.Mensaje
    };
}
```

**Explicación:** `Errores` solo se llena cuando fallan las validaciones del DTO (paso 10). Esta misma clase la podrá copiar el proyecto `Web.Client` para leer las respuestas.

### Paso 5 — `Constants/MensajesAutor.cs` (NUEVO)

```csharp
namespace BiblioTech2.Api.Constants;

/// <summary>Todos los textos del módulo Autores en un solo lugar.</summary>
public static class MensajesAutor
{
    public const string Listado = "Autores consultados correctamente.";
    public const string Encontrado = "Autor encontrado.";
    public const string Creado = "Autor creado correctamente.";
    public const string Actualizado = "Autor actualizado correctamente.";
    public const string Eliminado = "Autor eliminado correctamente.";

    public static string NoExiste(int id) => $"El autor {id} no existe.";
    public static string TieneLibros(int cantidad) =>
        $"No se puede eliminar el autor porque tiene {cantidad} libro(s) asociado(s).";
}
```

**Explicación:** los textos fijos son `const`; los que llevan datos (el id, la cantidad) son métodos.

### Paso 6 — `Controllers/ApiControllerBase.cs` (NUEVO)

```csharp
using BiblioTech2.Api.Respuestas;
using Microsoft.AspNetCore.Mvc;

namespace BiblioTech2.Api.Controllers;

/// <summary>Controlador base: convierte un Resultado del servicio en una respuesta HTTP.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult Responder<T>(Resultado<T> resultado)
        => StatusCode(CodigoHttp(resultado), RespuestaApi<T>.Desde(resultado));

    protected ActionResult Responder(Resultado resultado)
        => StatusCode(CodigoHttp(resultado), RespuestaApi<object>.Desde(resultado));

    private static int CodigoHttp(Resultado resultado) => resultado.Tipo switch
    {
        TipoResultado.Exito => StatusCodes.Status200OK,
        TipoResultado.NoEncontrado => StatusCodes.Status404NotFound,
        TipoResultado.ReglaNegocio => StatusCodes.Status409Conflict,
        TipoResultado.Validacion => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
}
```

**Explicación:** es `abstract` porque no es un controlador real (no tiene rutas). El `[ApiController]` se pone aquí una vez y lo heredan todos. Este `switch` es el que **antes estaba en el middleware** para las excepciones; ahora vive aquí.

✅ **Compilar.** Hasta aquí nada se rompe porque nadie usa las clases nuevas.

---

### Paso 7 — `Services/Interfaces/IAutorService.cs` (MODIFICAR)

**ORIGINAL**
```csharp
using BiblioTech.Api.DTOs.Autores;

namespace BiblioTech.Api.Services.Interfaces;

public interface IAutorService
{
    Task<List<AutorDto>> ListarAsync();
    Task<AutorDto> ObtenerAsync(int id);
    Task<AutorDto> CrearAsync(AutorGuardarDto dto);
    Task ActualizarAsync(int id, AutorGuardarDto dto);
    Task EliminarAsync(int id);
}
```

**NUEVO**
```csharp
using BiblioTech.Api.DTOs.Autores;
using BiblioTech2.Api.Respuestas;

namespace BiblioTech.Api.Services.Interfaces;

public interface IAutorService
{
    Task<Resultado<List<AutorDto>>> ListarAsync();
    Task<Resultado<AutorDto>> ObtenerAsync(int id);
    Task<Resultado<AutorDto>> CrearAsync(AutorGuardarDto dto);
    Task<Resultado> ActualizarAsync(int id, AutorGuardarDto dto);
    Task<Resultado> EliminarAsync(int id);
}
```

**Qué cambió:** cada método ahora **promete** devolver un `Resultado`. `Actualizar` y `Eliminar` pasan de `Task` (no devolvía nada) a `Task<Resultado>` (devuelve el mensaje).

> ⚠️ Después de este paso el proyecto **no compila** hasta terminar los pasos 8 y 9. Es normal.

### Paso 8 — `Services/AutorService.cs` (MODIFICAR)

**ORIGINAL**
```csharp
using BiblioTech.Api.DTOs.Autores;
using BiblioTech.Api.Services.Interfaces;
using BiblioTech2.Api.Data;
using BiblioTech2.Api.Exceptions;
using BiblioTech2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BiblioTech.Api.Services;

public  class AutorService(BibliotecaContext db) : IAutorService
{
    public async Task<List<AutorDto>> ListarAsync()
    {
        return await db.Autores
            .OrderBy(a => a.Nombre)
            .Select(a => new AutorDto
            {
                Id = a.Id,
                Nombre = a.Nombre,
                Nacionalidad = a.Nacionalidad,
                CantidadLibros = a.Libros.Count
            })
            .ToListAsync();
    }
    public async Task<AutorDto> ObtenerAsync(int id){
        return await db.Autores
            .Where(a => a.Id == id)
            .Select(a => new AutorDto
            {
                Id = a.Id,
                Nombre = a.Nombre,
                Nacionalidad = a.Nacionalidad,
                CantidadLibros = a.Libros.Count
            })
            .FirstOrDefaultAsync()
            ?? throw new NoEncontradoException($"El autor {id} no existe.");
    }

    public async Task<AutorDto> CrearAsync(AutorGuardarDto dto)
    {
        var autor = new Autor
        {
            Nombre = dto.Nombre.Trim(),
            Nacionalidad = string.IsNullOrWhiteSpace(dto.Nacionalidad) ? null : dto.Nacionalidad.Trim()
        };
        db.Autores.Add(autor);
        await db.SaveChangesAsync();
        return new AutorDto { Id = autor.Id, Nombre = autor.Nombre, Nacionalidad = autor.Nacionalidad };
    }

    public async Task  ActualizarAsync(int id, AutorGuardarDto dto)
    {
        var autor = await db.Autores.FindAsync(id)
            ?? throw new NoEncontradoException($"El autor {id} no existe.");

        autor.Nombre = dto.Nombre.Trim();
        autor.Nacionalidad = string.IsNullOrWhiteSpace(dto.Nacionalidad) ? null: dto.Nacionalidad.Trim();

        await db.SaveChangesAsync();
    }

    public async Task  EliminarAsync(int id)
    {
        var autor = await db.Autores.FindAsync(id)
            ?? throw new NoEncontradoException($"El autor {id} no existe.");

        var cantidadLibros = await db.Libros.CountAsync(l => l.Autores.Any(a => a.Id == id));
        if (cantidadLibros > 0)

            db.Autores.Remove(autor);        // ← BUG: queda DENTRO del if
        await db.SaveChangesAsync();
    }
}
```

**NUEVO**
```csharp
using BiblioTech.Api.DTOs.Autores;
using BiblioTech.Api.Services.Interfaces;
using BiblioTech2.Api.Constants;
using BiblioTech2.Api.Data;
using BiblioTech2.Api.Models;
using BiblioTech2.Api.Respuestas;
using Microsoft.EntityFrameworkCore;

namespace BiblioTech.Api.Services;

public class AutorService(BibliotecaContext db) : IAutorService
{
    public async Task<Resultado<List<AutorDto>>> ListarAsync()
    {
        var autores = await db.Autores
            .OrderBy(a => a.Nombre)
            .Select(a => new AutorDto
            {
                Id = a.Id,
                Nombre = a.Nombre,
                Nacionalidad = a.Nacionalidad,
                CantidadLibros = a.Libros.Count
            })
            .ToListAsync();

        return Resultado<List<AutorDto>>.Ok(autores, MensajesAutor.Listado);
    }

    public async Task<Resultado<AutorDto>> ObtenerAsync(int id)
    {
        var autor = await db.Autores
            .Where(a => a.Id == id)
            .Select(a => new AutorDto
            {
                Id = a.Id,
                Nombre = a.Nombre,
                Nacionalidad = a.Nacionalidad,
                CantidadLibros = a.Libros.Count
            })
            .FirstOrDefaultAsync();

        if (autor is null)
            return Resultado<AutorDto>.NoEncontrado(MensajesAutor.NoExiste(id));

        return Resultado<AutorDto>.Ok(autor, MensajesAutor.Encontrado);
    }

    public async Task<Resultado<AutorDto>> CrearAsync(AutorGuardarDto dto)
    {
        var autor = new Autor
        {
            Nombre = dto.Nombre.Trim(),
            Nacionalidad = string.IsNullOrWhiteSpace(dto.Nacionalidad) ? null : dto.Nacionalidad.Trim()
        };
        db.Autores.Add(autor);
        await db.SaveChangesAsync();

        var creado = new AutorDto { Id = autor.Id, Nombre = autor.Nombre, Nacionalidad = autor.Nacionalidad };
        return Resultado<AutorDto>.Ok(creado, MensajesAutor.Creado);
    }

    public async Task<Resultado> ActualizarAsync(int id, AutorGuardarDto dto)
    {
        var autor = await db.Autores.FindAsync(id);
        if (autor is null)
            return Resultado.NoEncontrado(MensajesAutor.NoExiste(id));

        autor.Nombre = dto.Nombre.Trim();
        autor.Nacionalidad = string.IsNullOrWhiteSpace(dto.Nacionalidad) ? null : dto.Nacionalidad.Trim();
        await db.SaveChangesAsync();

        return Resultado.Ok(MensajesAutor.Actualizado);
    }

    public async Task<Resultado> EliminarAsync(int id)
    {
        var autor = await db.Autores.FindAsync(id);
        if (autor is null)
            return Resultado.NoEncontrado(MensajesAutor.NoExiste(id));

        var cantidadLibros = await db.Libros.CountAsync(l => l.Autores.Any(a => a.Id == id));
        if (cantidadLibros > 0)
            return Resultado.ReglaNegocio(MensajesAutor.TieneLibros(cantidadLibros));

        db.Autores.Remove(autor);
        await db.SaveChangesAsync();

        return Resultado.Ok(MensajesAutor.Eliminado);
    }
}
```

**Qué cambió, método por método:**

| Método | Antes | Ahora |
|---|---|---|
| `ListarAsync` | `return await ...ToListAsync();` | guarda la lista en `autores` y devuelve `Resultado.Ok(autores, mensaje)` |
| `ObtenerAsync` | `?? throw new NoEncontradoException(...)` | `if (autor is null) return Resultado<AutorDto>.NoEncontrado(...)` |
| `CrearAsync` | devuelve el DTO | devuelve `Resultado<AutorDto>.Ok(creado, MensajesAutor.Creado)` |
| `ActualizarAsync` | `throw` si no existe, no devolvía nada | `return Resultado.NoEncontrado(...)` / `Resultado.Ok(Actualizado)` |
| `EliminarAsync` | `throw` si no existe + **bug del if** | `NoEncontrado` → `ReglaNegocio` si tiene libros → borra → `Ok(Eliminado)` |

Se quita `using BiblioTech2.Api.Exceptions;` y se agregan `using BiblioTech2.Api.Constants;` y `using BiblioTech2.Api.Respuestas;`.

### Paso 9 — `Controllers/AutoresController.cs` (MODIFICAR)

**ORIGINAL**
```csharp
using BiblioTech.Api.DTOs.Autores;
using BiblioTech.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiblioTech2.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutoresController(IAutorService servicio) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AutorDto>>>Listar()
        => Ok(await servicio.ListarAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AutorDto>> obtener (int id)
    {
        var autor = await servicio.ObtenerAsync(id);
        return autor is null ? NotFound() : Ok(autor);
    }

    [HttpPost]
    public async Task<ActionResult<AutorDto>> Crear(AutorGuardarDto dto)
    {
        var creado = await servicio.CrearAsync(dto);
        return CreatedAtAction(nameof(obtener), new { id = creado.Id }, creado);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, AutorGuardarDto dto)
    {
        await servicio.ActualizarAsync(id, dto);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        await servicio.EliminarAsync(id);
        return NoContent();
    }
}
```

**NUEVO**
```csharp
using BiblioTech.Api.DTOs.Autores;
using BiblioTech.Api.Services.Interfaces;
using BiblioTech2.Api.Respuestas;
using Microsoft.AspNetCore.Mvc;

namespace BiblioTech2.Api.Controllers;

[Route("api/[controller]")]
public class AutoresController(IAutorService servicio) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RespuestaApi<List<AutorDto>>>> Listar()
        => Responder(await servicio.ListarAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RespuestaApi<AutorDto>>> Obtener(int id)
        => Responder(await servicio.ObtenerAsync(id));

    [HttpPost]
    public async Task<ActionResult<RespuestaApi<AutorDto>>> Crear(AutorGuardarDto dto)
    {
        var resultado = await servicio.CrearAsync(dto);
        if (!resultado.Exito)
            return Responder(resultado);

        return CreatedAtAction(nameof(Obtener), new { id = resultado.Datos!.Id },
            RespuestaApi<AutorDto>.Desde(resultado));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RespuestaApi<object>>> Actualizar(int id, AutorGuardarDto dto)
        => Responder(await servicio.ActualizarAsync(id, dto));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<RespuestaApi<object>>> Eliminar(int id)
        => Responder(await servicio.EliminarAsync(id));
}
```

**Qué cambió:**
- Hereda de `ApiControllerBase` en vez de `ControllerBase` (y el `[ApiController]` ya viene de la base).
- `obtener` → `Obtener` (convención PascalCase de C#; actualizar también el `nameof`).
- El controlador ya **no decide** si es 404 o 200: llama a `Responder(...)`.
- `Crear` es especial: en éxito usa `CreatedAtAction` para devolver **201** con el encabezado `Location`.
- **Actualizar y Eliminar pasan de `204 No Content` a `200 OK`**, porque un 204 por definición **no puede llevar cuerpo** y ahora queremos enviar el mensaje. Avisar a quien haga el Blazor.

✅ **Compilar.** Con los pasos 7, 8 y 9 hechos, vuelve a compilar.

---

### Paso 10 — `Middleware/ErrorHandlingMiddleware.cs` (MODIFICAR)

El middleware **no desaparece**: queda como red de seguridad para lo **inesperado** (BD caída, una restricción de la BD que se nos pasó, un bug). Lo que se le quita es el manejo de nuestras excepciones propias.

**ORIGINAL (partes que cambian)**
```csharp
using BiblioTech.Api.Exceptions;
using BiblioTech2.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
...
    public async Task InvokeAsyng(HttpContext contexto)     // ← mal escrito
...
        var (codigo, titulo, detalle) = ex switch
        {
            NoEncontradoException e => (StatusCodes.Status404NotFound, "Recurso no encontrado", e.Message),
            ReglaNegocioException e => (StatusCodes.Status409Conflict, "Regla de negocio", e.Message),
            DbUpdateException e => (StatusCodes.Status409Conflict, "Conflicto con los datos", TraducirErrorBaseDatos(e)),
            _ => (StatusCodes.Status500InternalServerError, "Error interno",
                  "Ocurrió un error inesperado. Intenta de nuevo más tarde.")
        };
...
        var problema = new ProblemDetails { Status = codigo, Title = titulo, Detail = detalle, Instance = contexto.Request.Path };
        contexto.Response.StatusCode = codigo;
        await contexto.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
```

**NUEVO (archivo completo)**
```csharp
using BiblioTech2.Api.Respuestas;
using Microsoft.EntityFrameworkCore;

namespace BiblioTech2.Api.Middleware;

/// <summary>Red de seguridad: SOLO atrapa errores inesperados (BD caída, bug, restricción violada).</summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private static readonly Dictionary<string, string> MensajeRestricciones = new()
    {
        ["FK_LibroAutor_Autores"] = "El autor tiene libros asociados.",
        ["UQ_Socios_Documento"] = "Ya existe un socio con este documento.",
        ["UQ_Socios_Email"] = "Ya existe un socio con este correo.",
        ["FK_Prestamos_Libros"] = "El libro tiene préstamos asociados.",
        ["FK_Prestamos_Socios"] = "El socio tiene préstamos asociados.",
        ["CK_Libros_Disponibles"] = "Los ejemplares no pueden ser negativos ni mayores que los totales.",
        ["UQ_Libros_ISBN"] = "Ya existe un libro con ese ISBN.",
        ["CK_Socios_Email"] = "El correo no tiene un formato válido."
    };

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await next(contexto);
        }
        catch (Exception ex) when (!contexto.Response.HasStarted)
        {
            await ResponderAsync(contexto, ex);
        }
    }

    private async Task ResponderAsync(HttpContext contexto, Exception ex)
    {
        var (codigo, mensaje) = ex switch
        {
            DbUpdateException e => (StatusCodes.Status409Conflict, TraducirErrorBaseDatos(e)),
            _ => (StatusCodes.Status500InternalServerError,
                  "Ocurrió un error inesperado. Intenta de nuevo más tarde.")
        };

        if (codigo == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Error no controlado en {Ruta}", contexto.Request.Path);
        else
            logger.LogWarning("{Codigo} en {Ruta}: {Mensaje}", codigo, contexto.Request.Path, mensaje);

        contexto.Response.StatusCode = codigo;
        await contexto.Response.WriteAsJsonAsync(new RespuestaApi<object>
        {
            Exito = false,
            Mensaje = mensaje
        });
    }

    private static string TraducirErrorBaseDatos(DbUpdateException ex)
    {
        var mensajeSql = ex.InnerException?.Message ?? ex.Message;
        foreach (var (restriccion, mensaje) in MensajeRestricciones)
        {
            if (mensajeSql.Contains(restriccion, StringComparison.OrdinalIgnoreCase))
                return mensaje;
        }
        return "La operación viola una restricción de la base de datos.";
    }
}
```

**Qué cambió:**
- `InvokeAsyng` → `InvokeAsync` (sin esto el middleware no funciona).
- Se quitan los casos `NoEncontradoException` y `ReglaNegocioException`.
- Responde con `RespuestaApi<object>` en vez de `ProblemDetails`: **mismo formato que el resto**.
- Se corrige `FK_Prestamo_Socios` → `FK_Prestamos_Socios` (así se llama en `BibliotecaContext`) y las faltas de los textos.

### Paso 11 — `Program.cs` (MODIFICAR)

**ORIGINAL (partes que cambian)**
```csharp
builder.Services.AddControllers();
...
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

**NUEVO**
```csharp
using Scalar.AspNetCore;
using BiblioTech2.Api.Data;
using BiblioTech2.Api.Middleware;          // ← nuevo
using BiblioTech2.Api.Respuestas;          // ← nuevo
using Microsoft.AspNetCore.Mvc;            // ← nuevo
using Microsoft.EntityFrameworkCore;
using BiblioTech.Api.Services.Interfaces;
using BiblioTech.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(opciones =>
    {
        // Cuando fallan las validaciones del DTO ([Required], [StringLength]...)
        // respondemos con el mismo formato RespuestaApi en lugar del ProblemDetails por defecto.
        opciones.InvalidModelStateResponseFactory = contexto =>
        {
            var errores = contexto.ModelState
                .Where(campo => campo.Value!.Errors.Count > 0)
                .ToDictionary(
                    campo => campo.Key,
                    campo => campo.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            return new BadRequestObjectResult(new RespuestaApi<object>
            {
                Exito = false,
                Mensaje = "Los datos enviados no son válidos.",
                Errores = errores
            });
        };
    });

builder.Services.AddOpenApi();

builder.Services.AddDbContext<BibliotecaContext>(opciones =>
    opciones.UseSqlServer(builder.Configuration.GetConnectionString("BiblioTechConnection")));

builder.Services.AddScoped<IAutorService, AutorService>();

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>(); // primero: así envuelve a todo lo demás

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Qué cambió:**
1. `ConfigureApiBehaviorOptions`: `[ApiController]` valida el DTO **antes** de entrar al controlador y por defecto responde con su propio formato. Con esto, los errores de validación también salen como `RespuestaApi` con la lista en `errores`.
2. `app.UseMiddleware<ErrorHandlingMiddleware>()` **registrado y de primero**: el orden del pipeline importa; si va primero, atrapa errores de todo lo que viene después.

### Paso 12 — Limpieza

Eliminar la carpeta `Exceptions/` completa (`NoEncontradoException.cs` y `ReglaNegocioException.cs`). Ya nadie las usa.

✅ **Compilar y ejecutar.**

## 6. Estructura final

```
BiblioTech2.Api/
├── Program.cs                         (modificado)
├── Constants/
│   └── MensajesAutor.cs               (NUEVO)
├── Controllers/
│   ├── ApiControllerBase.cs           (NUEVO)
│   └── AutoresController.cs           (modificado)
├── Data/ …                            (sin cambios)
├── DTOs/Autores/ …                    (sin cambios)
├── Middleware/
│   └── ErrorHandlingMiddleware.cs     (modificado)
├── Models/ …                          (sin cambios)
├── Respuestas/                        (NUEVA carpeta)
│   ├── RespuestaApi.cs
│   ├── Resultado.cs
│   └── TipoResultado.cs
└── Services/
    ├── Interfaces/IAutorService.cs    (modificado)
    └── AutorService.cs                (modificado)
    (Exceptions/ eliminada)
```

## 7. Pruebas en Scalar (checklist para el grupo)

| Prueba | Esperado |
|---|---|
| `GET /api/autores` | 200, `exito: true`, `mensaje: "Autores consultados correctamente."`, lista en `datos` |
| `GET /api/autores/1` | 200, `"Autor encontrado."` |
| `GET /api/autores/9999` | **404**, `"El autor 9999 no existe."` |
| `POST /api/autores` con `{ "nombre": "Prueba" }` | **201**, `"Autor creado correctamente."`, encabezado `Location` |
| `POST /api/autores` con `{ "nombre": "" }` | **400**, `errores.Nombre = ["El nombre es obligatorio."]` |
| `PUT /api/autores/9999` | **404** |
| `PUT /api/autores/{id creado}` | **200**, `"Autor actualizado correctamente."` |
| `DELETE` a un autor **con** libros | **409**, `"No se puede eliminar el autor porque tiene N libro(s) asociado(s)."` |
| `DELETE /api/autores/{id creado}` (sin libros) | **200**, `"Autor eliminado correctamente."` |

## 8. Para los siguientes módulos (Libros, Socios, Préstamos)

Ya no hay que crear clases de respuesta nuevas. Por cada módulo solo:
1. `Constants/MensajesLibro.cs` (sus textos).
2. Interfaz y servicio devolviendo `Resultado` / `Resultado<T>`.
3. Controlador heredando de `ApiControllerBase` y usando `Responder(...)`.

> Nota opcional: el proyecto mezcla los espacios de nombres `BiblioTech.Api.*` y `BiblioTech2.Api.*`. Las clases nuevas usan `BiblioTech2.Api.*` (el nombre real del proyecto). Unificar los demás es un cambio aparte que conviene hacer todos a la vez para no generar conflictos en git.
