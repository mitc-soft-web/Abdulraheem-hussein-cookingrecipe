using System.Net;
using CookingRecipe.Conntext;
using StackExchange.Redis;
using CookingRecipe.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

namespace cookingrecipe
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();

            var runningOnRender = string.Equals(builder.Configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase);
            var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(defaultConnection))
            {
                var dbPath = builder.Environment.IsDevelopment()
                    ? "cookingrecipe.db"
                    : Path.Combine(Path.GetTempPath(), "cookingrecipe.db");
                defaultConnection = $"Data Source={dbPath}";
            }

            builder.Services.AddDbContext<CookingRecipeContext>(options =>
                options.UseSqlite(defaultConnection));

            
            var redisConn = builder.Configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConn))
            {
                builder.Services.AddScoped<IRedisStore, DatabaseRecipeStore>();
            }
            else
            {
                try
                {
                    var normalizedRedisConn = NormalizeRedisConnectionString(redisConn);
                    var multiplexer = ConnectionMultiplexer.Connect($"{normalizedRedisConn},abortConnect=false");
                    if (multiplexer.IsConnected)
                    {
                        builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
                        builder.Services.AddScoped<IRedisStore>(sp =>
                            new RedisRecipeStore(sp.GetRequiredService<IConnectionMultiplexer>()));
                    }
                    else
                    {
                        multiplexer.Dispose();
                        builder.Services.AddScoped<IRedisStore, DatabaseRecipeStore>();
                    }
                }
                catch
                {
                    builder.Services.AddScoped<IRedisStore, DatabaseRecipeStore>();
                }
            }

            // Register HttpClient and Spoonacular service
            builder.Services.AddHttpClient<ISpoonacularService, SpoonacularService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            builder.Services.AddHttpClient<IYouTubeService, YouTubeService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            builder.Services.AddSingleton<INigerianRecipeDatasetService, NigerianRecipeDatasetService>();

            builder.Services.AddControllers();
            builder.Services.AddHealthChecks();
            if (runningOnRender)
            {
                builder.Services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                    options.KnownNetworks.Clear();
                    options.KnownProxies.Clear();
                });
            }
            // Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

            // Add CORS policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowReactApp",
                    policy =>
                    {
                        if (builder.Environment.IsDevelopment())
                        {
                            policy.SetIsOriginAllowed(origin =>
                            {
                                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                                {
                                    return false;
                                }

                                return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
                            })
                                  .AllowCredentials();
                        }
                        else if (allowedOrigins.Length > 0)
                        {
                            policy.WithOrigins(allowedOrigins)
                                  .AllowCredentials();
                        }
                        else
                        {
                            // Safe default for production if no explicit origins are configured
                            policy.WithOrigins("http://localhost:5173")
                                  .AllowCredentials();
                        }

                        policy.AllowAnyHeader()
                              .AllowAnyMethod();
                    });
            });

            var app = builder.Build();
            EnsureDatabaseMigrated(app);

            if (runningOnRender)
            {
                app.UseForwardedHeaders();
            }

            app.UseCors("AllowReactApp");
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://httpstatuses.com/500",
                        title = "An unexpected error occurred.",
                        status = 500
                    });
                });
            });

            // Configure the HTTP request pipeline.
            app.UseSwagger();
            app.UseSwaggerUI();


            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            // ensure anonymous device id cookie is present
            app.UseMiddleware<CookingRecipe.Middleware.DeviceIdMiddleware>();

            // Serve local files from wwwroot (e.g., /images/*.jpg).
            app.UseStaticFiles();

            app.UseAuthorization();


            app.MapGet("/", () => Results.Redirect("/swagger"));
            app.MapHealthChecks("/health");
            app.MapControllers();

            app.Run();
        }

        private static void EnsureDatabaseMigrated(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CookingRecipeContext>();
            context.Database.Migrate();
            EnsureFavoriteRecipesTable(context);
        }

        private static void EnsureFavoriteRecipesTable(CookingRecipeContext context)
        {
            context.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "FavoriteRecipes" (
                    "DeviceId" TEXT NOT NULL,
                    "RecipeId" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "PK_FavoriteRecipes" PRIMARY KEY ("DeviceId", "RecipeId")
                );
                """);

            context.Database.ExecuteSqlRaw(
                """
                CREATE INDEX IF NOT EXISTS "IX_FavoriteRecipes_DeviceId"
                ON "FavoriteRecipes" ("DeviceId");
                """);
        }

        private static string NormalizeRedisConnectionString(string connectionString)
        {
            if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                return connectionString;
            }

            if (!string.Equals(uri.Scheme, "redis", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase))
            {
                return connectionString;
            }

            var user = string.Empty;
            var password = string.Empty;
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                user = Uri.UnescapeDataString(parts[0]);
                if (parts.Length > 1)
                {
                    password = Uri.UnescapeDataString(parts[1]);
                }
            }

            var list = new List<string> { $"{uri.Host}:{uri.Port}" };
            if (!string.IsNullOrWhiteSpace(user))
            {
                list.Add($"user={user}");
            }
            if (!string.IsNullOrWhiteSpace(password))
            {
                list.Add($"password={password}");
            }
            if (string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase))
            {
                list.Add("ssl=true");
            }

            return string.Join(",", list);
        }

        private static string DescribeRedisConnection(string connectionString)
        {
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) &&
                (string.Equals(uri.Scheme, "redis", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase)))
            {
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }

            var endpoint = connectionString.Split(',', 2)[0];
            return string.IsNullOrWhiteSpace(endpoint) ? "configured Redis endpoint" : endpoint;
        }
    }
}
