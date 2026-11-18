# BiblioTech


## Tecnologías

- .NET 10 (SDK 10.0.x)
- ASP.NET Core Web API + Scalar
- Blazor Web App (WebAssembly)
- Entity Framework Core 10 (Database First)
- SQL Server

## Estructura

```
BiblioTech2/
├── database/                  → script de la base de datos
├── BiblioTech2.Api/           → API REST (la única que toca la BD)
└── BiblioTech2.Web/
    ├── BiblioTech2.Web/        → servidor: entrega la app al navegador
    └── BiblioTech2.Web.Client/ → la app que corre en el navegador
```

“El Proyecto .Web arma una caja con nuestra app en C#, ya compilada, y se la envía al navegador. El navegador la abre y ejecuta el C# ahí mismo, 
en el PC del usuario, sin instalar nada, porque el navegador ya sabe correr WebAssembly.
La caja corre en una zona segura y, cuando necesita datos, se los pide a la API.  
Queda guardada en el cache del navegador asi la segunda vez no se tiene que descargar de nuevo.”

## Requisitos

- Visual Studio 2026 con la carga "ASP.NET y desarrollo web"
- SDK .NET 10 → verificar con: `dotnet --list-sdks`
- SQL Server + SSMS

## Cómo ejecutarlo (primera vez)

1. Hacer fork y clonar el repositorio.
2. Abrir `database/BiblioTech.sql` en SSMS y ejecutarlo (F5).
   Crea la BD `BiblioTechManual` con tablas, vista, procedimiento y datos.
3. Abrir `BiblioTech2.slnx` en Visual Studio.
4. Revisar los puertos (ver sección siguiente).
5. Elegir el perfil **Api + Web** arriba junto al ▶ y ejecutar (F5).
   - La API abre Scalar.
   - La Web abre la página de Blazor.

## Puertos (si cambian, hay que actualizarlos)

| Archivo | Lleva el puerto de |
|---|---|
| `Api/Properties/launchSettings.json` | la API (7180) |
| `Web/Properties/launchSettings.json` | la Web (7093 / 5113) |
| `Web.Client/wwwroot/appsettings.json` → `ApiUrl` | la API |
| `Api/appsettings.json` → `Cors` | la Web |
| `Api/BiblioTech2.Api.http` → `@host` | la API |



## Etiquetas (puntos de avance)

| Tag | Qué contiene |
|---|---|
| `esqueleto` | Solución creada, paquetes instalados, carpetas vacías |

