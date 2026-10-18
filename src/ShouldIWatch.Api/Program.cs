using Npgsql;
using ShouldIWatch.Api.Data;
using ShouldIWatch.Database;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Registers NpgsqlDataSource (+ health check and telemetry). In Azure it authenticates with Entra ID.
builder.AddAzureNpgsqlDataSource("appdb");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GreetingRepository>();

var app = builder.Build();

MigrateDatabase(app);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");

api.MapGet("greeting", async (GreetingRepository repository, CancellationToken cancellationToken) =>
    await repository.GetLatestAsync(cancellationToken) is { } greeting
        ? Results.Ok(greeting)
        : Results.NotFound())
    .WithName("GetGreeting");

app.MapDefaultEndpoints();

// Serves the React build (copied into wwwroot at publish time) with client-side routing fallback.
app.UseFileServer();
app.MapFallbackToFile("index.html");

app.Run();

static void MigrateDatabase(WebApplication app)
{
    var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DbUp");

    var result = DatabaseMigrator.Migrate(dataSource, new DbUpLogger(logger));
    if (!result.Successful)
    {
        throw new InvalidOperationException("Database migration failed.", result.Error);
    }
}
