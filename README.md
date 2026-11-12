# LogiTrack

LogiTrack is an ASP.NET Core API for managing inventory and orders. It uses SQLite and Entity Framework Core for persistence, ASP.NET Core Identity and JWTs for authentication, and a 30-second in-memory inventory-list cache.

## Prerequisites

- .NET 10 SDK.
- The `dotnet-ef` command-line tool compatible with EF Core 10. If it is not installed, run `dotnet tool install --global dotnet-ef --version 10.0.13`; if already installed, use `dotnet tool update --global dotnet-ef --version 10.0.13` instead.
- Internet access for the development Swagger UI assets, which are loaded from a CDN.

## Run locally

From the repository root, run these PowerShell commands:

```powershell
Set-Location LogiTrack
dotnet restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$bytes = New-Object byte[] 32
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $generator.GetBytes($bytes)
    dotnet user-secrets set 'Jwt:SigningKey' ([Convert]::ToBase64String($bytes))
} finally {
    $generator.Dispose()
    [Array]::Clear($bytes, 0, $bytes.Length)
}
dotnet ef database update
dotnet run --launch-profile http
```

Generate the signing key once for local development; generating a new one invalidates existing tokens. User secrets are stored outside the repository and are not a production secret store. For production, provide a Base64-encoded random key of at least 32 bytes as `Jwt__SigningKey` using your deployment's secret management. The issuer and audience are configured in [appsettings.json](LogiTrack/appsettings.json).

The SQLite path is relative to the process working directory. Run the application and migrations from `LogiTrack` so both use the same local `logitrack.db`. Use persistent storage and backups in production. See [state persistence](docs/state-persistence.md).

Open the development Swagger UI at <http://localhost:5275/swagger>. The HTTP profile is for local development; use HTTPS in production.

## Authentication and authorization

Register with `POST /api/auth/register`, then log in with `POST /api/auth/login` to receive a JWT. In Swagger, select **Authorize** and enter the token. API clients send it as `Authorization: Bearer <token>`.

Any authenticated user can read inventory and orders. Creating and deleting items or orders requires the `Manager` role. To grant it to an existing user, run from the `LogiTrack` directory:

```powershell
dotnet run --launch-profile http -- --grant-manager "username"
```

The user must log in again to get a token containing the new role. Orders are not currently restricted to their creator or tenant.

## Endpoints

| Resource | Methods |
| --- | --- |
| `/api/auth/register`, `/api/auth/login` | POST |
| `/api/inventory`, `/api/orders` | GET, POST |
| `/api/inventory/{id}`, `/api/orders/{id}` | GET, DELETE |

Example requests are in [LogiTrack.http](LogiTrack/LogiTrack.http). The inventory-list cache can return an older snapshot for up to 30 seconds after a change.

## Performance and Review

Run the isolated cache benchmark from the repository root:

```powershell
dotnet run --configuration Release --project tools/InventoryCacheBenchmark/InventoryCacheBenchmark.csproj
```

See [performance measurements](docs/performance.md) and the [production-readiness review](docs/production-readiness-review.md) for results, limitations, and outstanding work.