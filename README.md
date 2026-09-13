# AgroVisionAI

ASP.NET Core MVC crop diagnosis application with a Python/FastAPI inference service
for cotton, wheat and rice. Includes care guidance, reports, feedback, analytics,
weather risk and administrator model management.

## What belongs in GitHub

| Commit | Keep local / outside Git |
| --- | --- |
| C#, Razor, Python, CSS and JavaScript source | Passwords, JWT keys, API keys and private certificates |
| `.sln`, `.csproj`, requirements and EF migrations | `appsettings.Local.json`, `.env` and private overrides |
| Tests, CI workflows, Dockerfiles and Compose | `bin`, `obj`, `.vs`, `.venv`, caches and logs |
| Public `appsettings*.json` with empty secrets; example settings | Database files/backups, user uploads, exported reports and heatmaps |
| Treatment catalogue JSON, disease labels and model configuration | Trained model binaries and private training datasets |
| Website images, vendored frontend libraries and their licences | Local task summaries and generated validation reports |

Do not ignore all `App_Data` or `wwwroot`: treatment data and public website assets
are required by the application. Existing frontend libraries remain versioned because
the project currently has no package restore step for those assets.

## Local setup (Windows)

Prerequisites: .NET 9 SDK, SQL Server/LocalDB and Python 3.11. Use the versions
declared by the project and CI; review supported runtime versions before deployment.

1. Copy `AgroVisionAI/appsettings.Local.example.json` to
   `AgroVisionAI/appsettings.Local.json`. Do not overwrite an existing local file.
2. Set `ConnectionStrings:DefaultSQLConnection` for your SQL Server instance.
3. Generate separate random values for `Jwt:Key` and `AgroVisionApi:AdminKey`.
   For each key, run this PowerShell command and paste its output into the local file:

   ```powershell
   $bytes = New-Object byte[] 48
   $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
   $rng.GetBytes($bytes)
   [Convert]::ToBase64String($bytes)
   $rng.Dispose()
   ```

4. To create an administrator, temporarily set `IdentitySeed:Enabled` to `true`
   with your chosen email and a strong password in the local file. Disable seeding
   after the first successful run. Existing accounts are not reset by this setting.
5. Restore/build and apply migrations. In Visual Studio's Package Manager Console,
   select `AgroVisionAI` as the default project and run `Update-Database`.

   ```powershell
   dotnet restore AgroVisionAI/AgroVisionAI.csproj
   dotnet build AgroVisionAI/AgroVisionAI.csproj --no-restore
   ```

6. Follow [AI setup](AgroVisionAI/AIService/README.md) and
   [model requirements](AgroVisionAI/AIService/models/README.md). Model binaries
   are supplied separately; a clone does not include trained weights or user data.
7. Run `AgroVisionAI/AIService/start_api.bat`. Its local launcher reads the shared
   admin key from `appsettings.Local.json`; `AGROVISION_ADMIN_KEY` overrides it.
8. Start the web project in Visual Studio or run:

   ```powershell
   dotnet run --project AgroVisionAI/AgroVisionAI.csproj --launch-profile http
   ```

`appsettings.Local.json` loads only in Development. Environment variables and
command-line settings override it. It is excluded from Git, publishing and Docker.
Normal `uvicorn`/Docker startup uses environment variables and does not load it.

## Before each push

From the repository root:

```powershell
py -3.11 scripts/check_repository.py
dotnet build AgroVisionAI/AgroVisionAI.csproj --no-restore
dotnet run --project Verification/TreatmentPk.Tests.csproj
git status --short
git diff --stat
git add .
py -3.11 scripts/check_repository.py --staged
git diff --cached --stat
git commit -m "Prepare project for GitHub and improve responsive navigation"
git push
```

Review the staged diff locally before committing. The staged check reads the exact
index: editing a file after staging it does not sanitize the staged version.
The checker rejects tracked ignored files, private runtime paths, model binaries,
files over our 50 MiB repository limit, populated configuration secrets and selected
credential signatures. CI runs the same check. It does not detect every secret.
Never use `git add -f` for credentials, uploads or models.

For optional local commit enforcement, configure a Git hook to run the staged check;
CI is the shared check and does not depend on each developer's hook setup.

## Deployment and history

Copy `.env.example` to `.env` for Docker and fill the blank secret values before
following [Docker instructions](AgroVisionAI/DOCKER_CI_README.md). Docker Compose
reads `.env`; `dotnet run` does not. Use environment variables/your deployment's
secret store in production. Keep SQL Server, uploaded images and model files backed
up separately. Keep a model version, checksum, source and class order with each
externally stored model release.

Removing a file from tracking preserves the local file but **does not erase older
commits**. Uploaded crop images were present in earlier commits of this repository.
Do not publish the existing history if those images must remain private. Review and
clean that history or publish an independently prepared clean repository first.
Rotate any credential that has actually been exposed; ignoring a file is not rotation.
No history rewriting or remote push is performed by these preparation changes.
