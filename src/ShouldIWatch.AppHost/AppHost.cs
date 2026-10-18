var builder = DistributedApplication.CreateBuilder(args);

// Deployment target: Azure Container Apps (used by `azd` / `aspire publish`).
builder.AddAzureContainerAppEnvironment("env");

// Azure SQL Database (free offer, auto-pauses) in Azure; a local SQL Server container in dev.
var sql = builder.AddAzureSqlServer("sql")
    .RunAsContainer(container => container
        .WithDataVolume());

var db = sql.AddDatabase("appdb");

var api = builder.AddProject<Projects.ShouldIWatch_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    // HTTP endpoint so the check does not depend on a trusted dev certificate (e.g. in CI).
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithExternalHttpEndpoints()
    // Personal site: scale to zero when idle so compute costs ~nothing.
    // Trade-off: the first request after idle waits a few seconds for a cold start.
    .PublishAsAzureContainerApp((_, app) => app.Template.Scale.MinReplicas = 0);

var web = builder.AddViteApp("web", "../web")
    .WithReference(api)
    .WaitFor(api);

// In Azure the built React app is copied into the API container and served from wwwroot.
api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
