using BankingService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddStorage(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddVersionedSwagger();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseStorageInitialisation();
app.UseVersionedSwagger();
app.UseApplicationMiddleware();

app.Run();

// Expose for WebApplicationFactory in integration tests.
public partial class Program { }