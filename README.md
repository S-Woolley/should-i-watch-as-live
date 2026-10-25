# Should I watch it live?

A personal-development playground: a .NET 10 + Aspire app with a React front end and Azure SQL, deployed to Azure Container Apps with `azd` and GitHub Actions.

## Stack

| Concern | Choice |
| --- | --- |
| Orchestration | [Aspire](https://aspire.dev) 13 (`src/ShouldIWatch.AppHost`) |
| API | ASP.NET Core minimal API (`src/ShouldIWatch.Api`) |
| Front end | React + Vite + TypeScript (`src/web`) |
| Database | SQL Server: a container locally, Azure SQL Database (free offer, auto-pauses) in Azure |
| Data access | [Dapper](https://github.com/DapperLib/Dapper) over the Aspire-provided `SqlConnection` |
| Migrations | [DbUp](https://dbup.readthedocs.io): T-SQL scripts in `src/ShouldIWatch.Database/Scripts`, applied on API startup |
| Hosting | Azure Container Apps (one container: the API serves the built React app from `wwwroot`) |
| CI/CD | GitHub Actions: `ci.yml` on PRs, `azure-dev.yml` deploys on push to `main` |

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- Node.js 22+
- Docker Desktop (running)
- For Azure: [Azure Developer CLI](https://aka.ms/azd) (`winget install microsoft.azd`) and an Azure subscription

## Run locally

The API needs a free [CollegeFootballData.com](https://collegefootballdata.com/key) API key. Store it once as an AppHost user secret (it's passed to the API as `Cfbd__ApiKey` and never reaches the browser):

```bash
dotnet user-secrets set Parameters:cfbd-api-key <your-key> --project src/ShouldIWatch.AppHost
```

Then start everything:

```bash
dotnet run --project src/ShouldIWatch.AppHost
```

The Aspire dashboard opens and starts SQL Server, the API and the Vite dev server. The first run pulls the SQL Server image (~1.5 GB). To browse the local database, connect the VS Code SQL Server (mssql) extension or SSMS using the connection details shown on the dashboard. Open the `web` endpoint from the dashboard.

## Test

```bash
dotnet test --solution ShouldIWatch.slnx
```

```bash
npm --prefix src/web test
```

The .NET integration tests start the whole AppHost, so Docker must be running. They use a dummy CFBD key and an unreachable CFBD URL, so they never call CollegeFootballData.com, and database tests run in a throwaway database on the same SQL Server.

## Database changes

Add a new T-SQL script to `src/ShouldIWatch.Database/Scripts` named `NNNN_description.sql` (next number, lowercase snake case). Scripts are embedded and run once each, in order, journaled in the `schemaversions` table. Never edit a script that has already been deployed; add a new one instead.

## Deploy to Azure

One-time setup:

```bash
azd auth login
```

```bash
azd up
```

`azd up` asks for an environment name, subscription and region (and the `cfbd-api-key` parameter, stored as a Container Apps secret), then provisions a resource group with Container Apps, a container registry, Log Analytics and an Azure SQL server + database on the **free offer**, and deploys the app.

The free offer includes a monthly compute and storage allowance; when it is used up the database pauses until the next month instead of billing. A paused database can take some seconds to resume on the first connection after being idle.

The API container app scales to zero when idle (`minReplicas: 0`), so compute costs are close to nothing; the first request after an idle period waits a few seconds for a cold start. The main fixed cost is the Basic container registry (~USD 5/month).

Then connect GitHub Actions (creates federated OIDC credentials and repo variables, and publishes each saved secret parameter as a repository secret named `AZURE_` plus the parameter name in upper snake case, such as `AZURE_CFBD_API_KEY`):

```bash
azd pipeline config --provider github
```

After that, every push to `main` runs `azd provision` + `azd deploy`.

The deploy workflow runs `azd provision --no-prompt`, so it can't ask for a new secret parameter. When the AppHost gains one (as with `cfbd-api-key`), save it in the azd environment before pushing by running `azd provision` locally (it prompts for the value), then re-run `azd pipeline config --provider github` to publish its secret, and pass that secret to azd in the `env` block of `.github/workflows/azure-dev.yml`.

## Next: OpenSpec

Feature work will be driven by [OpenSpec](https://github.com/Fission-AI/OpenSpec) (`npm i -g @fission-ai/openspec`, then `openspec init`).
