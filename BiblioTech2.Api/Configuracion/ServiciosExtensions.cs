using BiblioTech.Api.Services;
using BiblioTech.Api.Services.Interfaces;

namespace BiblioTech2.Api.Configuracion;

/// <summary>
/// Métodos de extensión para que Program.cs quede corto y legible:
/// cada bloque de configuración vive aquí con su nombre.
/// </summary>
public static class ServiciosExtensions
{
    public const string PoliticaCors = "PermitirWeb";

    /// <summary>Registra un servicio por módulo (interfaz → implementación).</summary>
    public static IServiceCollection AddServiciosBiblioteca(this IServiceCollection services)
    {
        services.AddScoped<IAutorService, AutorService>();
        services.AddScoped<ILibroService, LibroService>();
        services.AddScoped<ISocioService, SocioService>();
        return services;
    }

    /// <summary>
    /// CORS: el navegador solo deja que la Web (otro puerto = otro "origen") llame a la API
    /// si la API lo autoriza. Los orígenes permitidos están en appsettings.json → "Cors".
    /// </summary>
    public static IServiceCollection AddCorsWeb(this IServiceCollection services, IConfiguration configuracion)
    {
        var origenes = configuracion.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];

        services.AddCors(opciones =>
            opciones.AddPolicy(PoliticaCors, politica => politica
                .WithOrigins(origenes)
                .AllowAnyHeader()
                .AllowAnyMethod()));

        return services;
    }
}
