# Should I watch it live?

A personal-development playground: a .NET 10 + Aspire app with a React front end and PostgreSQL, deployed to Azure Container Apps with `azd` and GitHub Actions.

## Stack

| Concern | Choice |
| --- | --- |
| Orchestration | [Aspire](https://aspire.dev) 13 (`src/ShouldIWatch.AppHost`) |
| API | ASP.NET Core minimal API (`src/ShouldIWatch.Api`) |
| Front end | React + Vite + TypeScript (`src/web`) |
| Database | PostgreSQL: a container locally, Azure Database for PostgreSQL Flexible Server in Azure |
| Data access | [Dapper](https://github.com/DapperLib/Dapper) over the Aspire-provided `NpgsqlDataSource` |
| Migrations | [DbUp](https://dbup.readthedocs.io): SQL scripts in `src/ShouldIWatch.Database/Scripts`, applied on API startup |
| Hosting | Azure Container Apps (one container: the API serves the built React app from `wwwroot`) |
| CI/CD | GitHub Actions: `ci.yml` on PRs, `azure-dev.yml` deploys on push to `main` |

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- Node.js 22+
- Docker Desktop (running)
- For Azure: [Azure Developer CLI](https://aka.ms/azd) (`winget install microsoft.azd`) and an Azure subscription

## Run locally

```bash
dotnet run --project src/ShouldIWatch.AppHost
```

The Aspire dashboard opens and starts Postgres (+ pgAdmin), the API and the Vite dev server. Open the `web` endpoint from the dashboard.

## Test

```bash
dotnet test --solution ShouldIWatch.slnx
```

```bash
npm --prefix src/web test
```

The .NET integration test starts the whole AppHost, so Docker must be running.

## Database changes

Add a new script to `src/ShouldIWatch.Database/Scripts` named `NNNN_description.sql` (next number, lowercase snake case). Scripts are embedded and run once each, in order, journaled in the `schemaversions` table. Never edit a script that has already been deployed; add a new one instead.

## Deploy to Azure

One-time setup:

```bash
azd auth login
```

```bash
azd up
```

`azd up` asks for an environment name, subscription and region, then provisions a resource group with Container Apps, a container registry, Log Analytics and a Postgres Flexible Server, and deploys the app.

Then connect GitHub Actions (creates federated OIDC credentials and repo variables, no secrets):

```bash
azd pipeline config --provider github
```

After that, every push to `main` runs `azd provision` + `azd deploy`.

## Next: OpenSpec

Feature work will be driven by [OpenSpec](https://github.com/Fission-AI/OpenSpec) (`npm i -g @fission-ai/openspec`, then `openspec init`).
