using ShouldIWatch.Api.Data;
using ShouldIWatch.Database;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Registers SqlConnection (+ health check and telemetry). In Azure it authenticates with Entra ID.
builder.AddSqlServerClient("appdb");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddScoped<GreetingRepository>();

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
    var connectionString = app.Configuration.GetConnectionString("appdb")
        ?? throw new InvalidOperationException("Connection string 'appdb' is not configured.");
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DbUp");

    var result = DatabaseMigrator.Migrate(connectionString, new DbUpLogger(logger));
    if (!result.Successful)
    {
        throw new InvalidOperationException("Database migration failed.", result.Error);
    }
}
