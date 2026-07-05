# CookingRecipe API

ASP.NET Core Web API for recipe search, Spoonacular integration, favorites, and search history.

## Requirements

- .NET SDK 9.0+

## Run

```powershell
dotnet restore
dotnet run --project CookingRecipe.csproj
```

## Build

```powershell
dotnet build cookingrecipe.sln
```

## Database (EF Core + SQLite)

Create migration:

```powershell
dotnet tool run dotnet-ef migrations add InitialCreate --context CookingRecipeContext --output-dir Migrations
```

Apply migrations:

```powershell
dotnet tool run dotnet-ef database update --context CookingRecipeContext
```

The app also runs pending migrations automatically at startup.

## Tests

```powershell
dotnet test cookingrecipe.sln
```

## Configure Secrets (Required for live Spoonacular search)

Use user-secrets in development:

```powershell
dotnet user-secrets init
dotnet user-secrets set "Spoonacular:ApiKey" "YOUR_SPOONACULAR_KEY"
dotnet user-secrets set "ConnectionStrings:Redis" "redis://username:password@host:port"
```

Notes:
- `Spoonacular:ApiKey` is required for live provider endpoints.
- Redis is optional; if not configured or unavailable, the app falls back to SQLite-backed storage.

## Deploy API on Render

Deploy the backend as a Render Web Service using the Docker runtime. The Dockerfile binds to Render's `PORT` value and starts `CookingRecipe.dll`.

Set these environment variables in Render:

```text
ASPNETCORE_ENVIRONMENT=Production
Spoonacular__ApiKey=YOUR_SPOONACULAR_KEY
ConnectionStrings__Redis=redis://username:password@host:port
Cors__AllowedOrigins__0=https://your-frontend-domain
```

Redis is optional. If Redis is deleted or unavailable, favorites and search history fall back to SQLite. SQLite needs persistent storage if you want database data to survive restarts. Add a Render disk mounted at `/var/data`, then set:

```text
ConnectionStrings__DefaultConnection=Data Source=/var/data/cookingrecipe.db
```

If you deploy the React frontend separately, set:

```text
VITE_API_BASE_URL=https://your-api-service.onrender.com
```

## API Endpoints

`Recipes`
- `GET /api/recipes/search?ingredients=rice,beans&max=10`
- `POST /api/recipes/suggest-by-ingredients`
- `GET /api/recipes`
- `GET /api/recipes/{id}`
- `GET /api/recipes/stored/search?ingredients=tomato,onion&max=50`
- `POST /api/recipes/{id}/favorite`
- `DELETE /api/recipes/{id}/favorite`
- `GET /api/recipes/favorites`

`Search History`
- `GET /api/searchhistory`
- `POST /api/searchhistory`

`Health`
- `GET /health`

## Notes

- Swagger UI is enabled at startup (`/swagger`).
- App root (`/`) redirects to Swagger.
