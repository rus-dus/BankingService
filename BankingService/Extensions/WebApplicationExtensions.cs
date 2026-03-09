using BankingService.Middleware;
using BankingService.Services;

namespace BankingService.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Applies EF Core schema on startup when the EfCore backend is selected.
    /// No-op for InMemory.
    /// </summary>
    public static WebApplication UseStorageInitialisation(this WebApplication app)
    {
        var backend = app.Configuration.GetValue<string>("Storage") ?? "InMemory";

        if (backend.Equals("EfCore", StringComparison.OrdinalIgnoreCase))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
            db.Database.EnsureCreated();

            app.Logger.LogInformation("EF Core database schema ensured");
        }

        return app;
    }

    /// <summary>Registers Swagger UI for development environments.</summary>
    public static WebApplication UseVersionedSwagger(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/swagger/v1/swagger.json", "Banking Service v1");
            });
        }

        return app;
    }

    /// <summary>Applies the full middleware pipeline in the correct order.</summary>
    public static WebApplication UseApplicationMiddleware(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseHttpsRedirection();
        app.MapControllers();

        return app;
    }
}