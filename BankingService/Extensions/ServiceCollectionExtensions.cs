using Asp.Versioning;
using BankingService.Configuration;
using BankingService.Mapping;
using BankingService.Services;
using BankingService.Validators;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

namespace BankingService.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the storage backend chosen by the "Storage" config key.
    /// "InMemory" (default) uses ConcurrentDictionary + semaphores.
    /// "EfCore" uses SQLite with serialisable transactions.
    /// </summary>
    public static IServiceCollection AddStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var backend = configuration.GetValue<string>("Storage") ?? "InMemory";

        if (backend.Equals("EfCore", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=banking.db";

            services.AddDbContext<AccountDbContext>(o => o.UseSqlite(connectionString));
            services.AddScoped<IAccountRepository, EfCoreAccountRepository>();
            services.AddScoped<IAccountService, AccountService>();
            // Metrics counters must survive across requests — always Singleton.
            services.AddSingleton<IAccountMetricsService, AccountMetricsService>();
        }
        else
        {
            services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
            services.AddSingleton<IAccountService, AccountService>();
            services.AddSingleton<IAccountMetricsService, AccountMetricsService>();
        }

        return services;
    }

    /// <summary>Registers validators, AutoMapper, IOptions, and metrics infrastructure.</summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validators are injected directly into controllers and called explicitly,
        // so auto-validation is not registered. Scanning the assembly registers all
        // three validators (CreateAccountRequest, TransferRequest, FreezeRequest) with DI.
        services.AddValidatorsFromAssemblyContaining<CreateAccountRequestValidator>();

        services.AddAutoMapper(typeof(AccountMappingProfile));

        services.AddOptions<AccountSettings>()
            .Bind(configuration.GetSection(AccountSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMetrics();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>Registers API versioning and Swagger with per-version documents.</summary>
    public static IServiceCollection AddVersionedSwagger(this IServiceCollection services)
    {
        services.AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1);
            o.AssumeDefaultVersionWhenUnspecified = true;
            o.ReportApiVersions = true;
        })
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat           = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title   = "Mini Banking Service",
                Version = "v1",
                Description = "Account management and fund transfer API."
            });

            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                o.IncludeXmlComments(xmlPath);
        });

        return services;
    }
}