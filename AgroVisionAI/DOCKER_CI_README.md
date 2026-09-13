# AgroVisionAI - Docker + CI setup

This task productionizes the existing three-service architecture without changing the AI models or user-facing prediction flow.

## Services

- `web` - ASP.NET Core MVC on container port `8080`
- `ai-service` - FastAPI/TensorFlow on container port `8000`
- `sqlserver` - SQL Server 2022 Developer on container port `1433`

The ASP.NET application talks to FastAPI by Docker service name (`http://ai-service:8000/`) and to SQL Server by service name (`sqlserver`). The existing local Visual Studio settings are untouched; Docker overrides them using environment variables.

## First Docker run

From the solution folder (the folder containing `AgroVisionAI.sln` and `docker-compose.yml`):

1. Copy `.env.example` to `.env`.
2. Change `SQL_SA_PASSWORD` to your own strong local password.
3. Change `JWT_KEY` to a long random secret (at least 32 characters).
4. Set `AI_ADMIN_KEY` to a separate random secret for model management. If enabling admin seeding, also set `ADMIN_PASSWORD` and your admin email.
5. Confirm the trained `.keras` files still exist in `AgroVisionAI/AIService/models/`.
6. Run:

```powershell
docker compose up --build
```

Then open:

- Web: `http://localhost:8080`
- FastAPI Swagger: `http://localhost:8000/docs`
- FastAPI health: `http://localhost:8000/health`
- ASP.NET health: `http://localhost:8080/health`

The web container applies Entity Framework migrations automatically only when `Database__ApplyMigrationsOnStartup=true` (set by Docker Compose). Normal Visual Studio runs do not automatically change the database.

Stop the stack with:

```powershell
docker compose down
```

To also delete Docker database/image-history volumes (destructive):

```powershell
docker compose down -v
```

## Why model files are mounted

Large `.keras` weights are intentionally excluded from the FastAPI Docker build context and Git history. `docker-compose.yml` mounts the existing local `AIService/models` directory into `/app/models` as read-only. This keeps image rebuilds smaller and avoids accidentally committing large model binaries.

## CI workflow

`.github/workflows/ci.yml` runs automatically on configured branches and pull requests:

1. Restores, builds and publishes the .NET 9 application.
2. Runs FastAPI unit tests on Python 3.11 without requiring trained weights.
3. After non-PR pushes, validates that both Docker images can be built.
4. Stores the published ASP.NET application as a short-lived GitHub Actions artifact.

The workflow does **not** publish images to a public registry or deploy to a cloud account, so no cloud credentials are required. Registry/cloud deployment can be added later as a separate task.
