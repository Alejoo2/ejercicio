using Scalar.AspNetCore;
using BiblioTech2.Api.Data;
using BiblioTech2.Api.Middleware;
using BiblioTech2.Api.Respuestas;
using Microsoft.AspNetCore.Mvc;
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

app.UseMiddleware<ErrorHandlingMiddleware>(); 

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
