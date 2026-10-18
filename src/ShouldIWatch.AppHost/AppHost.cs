var builder = DistributedApplication.CreateBuilder(args);

// Deployment target: Azure Container Apps (used by `azd` / `aspire publish`).
builder.AddAzureContainerAppEnvironment("env");

// Azure Database for PostgreSQL Flexible Server in Azure; a local Postgres container in dev.
var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(pg => pg
        .WithDataVolume()
        .WithPgAdmin());

var db = postgres.AddDatabase("appdb");

var api = builder.AddProject<Projects.ShouldIWatch_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    // HTTP endpoint so the check does not depend on a trusted dev certificate (e.g. in CI).
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithExternalHttpEndpoints();

var web = builder.AddViteApp("web", "../web")
    .WithReference(api)
    .WaitFor(api);

// In Azure the built React app is copied into the API container and served from wwwroot.
api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
