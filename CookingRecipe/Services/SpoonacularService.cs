using System.Net.Http.Json;
using System.Text.Json;
using CookingRecipe.Entities;
using System.Net;

namespace CookingRecipe.Services
{
    public interface ISpoonacularService
    {
        bool IsConfigured { get; }
        Task<List<Recipe>> SearchByIngredientsAsync(string ingredientsCsv, int number = 10);
        Task<List<int>> SearchRecipeIdsByIncludedIngredientsAsync(string ingredientsCsv, int number = 10);
        Task<List<int>> SearchRecipeIdsByTitleAsync(string query, int number = 10);
        Task<List<Recipe>> SearchOpenRecipesAsync(string query, IEnumerable<string> requiredTerms, int number = 10);
        Task<Recipe?> GetRecipeDetailAsync(int id);
    }

    public class SpoonacularService : ISpoonacularService
    {
        private const string DefaultFoodImage = "https://images.unsplash.com/photo-1498837167922-ddd27525d352?auto=format&fit=crop&w=1200&q=80";
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        public SpoonacularService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["Spoonacular:ApiKey"] ?? string.Empty;
        }

        public async Task<List<Recipe>> SearchByIngredientsAsync(string ingredientsCsv, int number = 10)
        {
            var url = $"https://api.spoonacular.com/recipes/findByIngredients?ingredients={Uri.EscapeDataString(ingredientsCsv)}&number={number}&ranking=2&ignorePantry=true&apiKey={_apiKey}";
            var res = await _http.GetAsync(url);
            await EnsureSuccessAsync(res, "findByIngredients");

            using var stream = await res.Content.ReadAsStreamAsync();
            var docs = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, _json);
            var list = new List<Recipe>();
            if (docs == null) return list;

            foreach (var el in docs)
            {
                try
                {
                    var id = el.GetProperty("id").GetInt32();
                    var title = el.GetProperty("title").GetString() ?? string.Empty;
                    var image = el.GetProperty("image").GetString() ?? string.Empty;

                    var ingredients = new List<RecipeIngredient>();
                    if (el.TryGetProperty("usedIngredients", out var used))
                    {
                        foreach (var u in used.EnumerateArray())
                        {
                            var name = u.GetProperty("name").GetString() ?? string.Empty;
                            ingredients.Add(new RecipeIngredient
                            {
                                Ingredient = new Ingredient { Name = name },
                                Quantity = string.Empty
                            });
                        }
                    }

                    if (el.TryGetProperty("missedIngredients", out var missed))
                    {
                        foreach (var u in missed.EnumerateArray())
                        {
                            var name = u.GetProperty("name").GetString() ?? string.Empty;
                            ingredients.Add(new RecipeIngredient
                            {
                                Ingredient = new Ingredient { Name = name },
                                Quantity = string.Empty
                            });
                        }
                    }

                    var recipe = new Recipe
                    {
                        Id = id,
                        Title = title,
                        ImageUrl = EnsureImageUrl(image),
                        Ingredients = ingredients
                    };

                    list.Add(recipe);
                }
                catch
                {
                    // ignore malformed entries
                }
            }

            return list;
        }

        public async Task<List<int>> SearchRecipeIdsByIncludedIngredientsAsync(string ingredientsCsv, int number = 10)
        {
            var url = $"https://api.spoonacular.com/recipes/complexSearch?includeIngredients={Uri.EscapeDataString(ingredientsCsv)}&number={number}&sort=popularity&sortDirection=desc&apiKey={_apiKey}";
            var res = await _http.GetAsync(url);
            await EnsureSuccessAsync(res, "complexSearch");

            using var stream = await res.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, _json);
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new List<int>();
            }

            var ids = new List<int>();
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public async Task<List<int>> SearchRecipeIdsByTitleAsync(string query, int number = 10)
        {
            var url = $"https://api.spoonacular.com/recipes/complexSearch?query={Uri.EscapeDataString(query)}&number={number}&sort=popularity&sortDirection=desc&apiKey={_apiKey}";
            var res = await _http.GetAsync(url);
            await EnsureSuccessAsync(res, "complexSearch title query");

            using var stream = await res.Content.ReadAsStreamAsync();
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, _json);
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new List<int>();
            }

            var ids = new List<int>();
            foreach (var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public async Task<Recipe?> GetRecipeDetailAsync(int id)
        {
            var url = $"https://api.spoonacular.com/recipes/{id}/information?includeNutrition=true&apiKey={_apiKey}";
            var res = await _http.GetAsync(url);
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(res, "recipe information");

            using var stream = await res.Content.ReadAsStreamAsync();
            var el = await JsonSerializer.DeserializeAsync<JsonElement>(stream, _json);
            if (el.ValueKind == JsonValueKind.Undefined) return null;

            try
            {
                var recipe = new Recipe();
                recipe.Id = el.GetProperty("id").GetInt32();
                recipe.Title = el.GetProperty("title").GetString() ?? string.Empty;
                recipe.ImageUrl = EnsureImageUrl(el.GetProperty("image").GetString());
                recipe.Summary = el.GetProperty("summary").GetString() ?? string.Empty;

                if (el.TryGetProperty("extendedIngredients", out var ext))
                {
                    var list = new List<RecipeIngredient>();
                    foreach (var item in ext.EnumerateArray())
                    {
                        var name = item.GetProperty("name").GetString() ?? string.Empty;
                        var original = item.TryGetProperty("original", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                        list.Add(new RecipeIngredient
                        {
                            Ingredient = new Ingredient { Name = name },
                            Quantity = original
                        });
                    }
                    recipe.Ingredients = list;
                }

                recipe.Instructions = el.TryGetProperty("instructions", out var instr) ? instr.GetString() ?? string.Empty : string.Empty;
                recipe.ReadyInMinutes = el.TryGetProperty("readyInMinutes", out var r) ? r.GetInt32() : 0;
                recipe.Servings = el.TryGetProperty("servings", out var s) ? s.GetInt32() : 0;
                recipe.SourceUrl = el.TryGetProperty("sourceUrl", out var su) ? su.GetString() ?? string.Empty : string.Empty;

                // nutrition simplified
                if (el.TryGetProperty("nutrition", out var nut) && nut.TryGetProperty("nutrients", out var nutrients))
                {
                    var ni = new NutritionInfo();
                    foreach (var n in nutrients.EnumerateArray())
                    {
                        var name = n.GetProperty("name").GetString() ?? string.Empty;
                        var amount = n.TryGetProperty("amount", out var a) ? a.GetDouble() : 0.0;
                        if (string.Equals(name, "Calories", StringComparison.OrdinalIgnoreCase)) ni.Calories = (int)amount;
                        if (string.Equals(name, "Fat", StringComparison.OrdinalIgnoreCase)) ni.FatGrams = amount;
                        if (string.Equals(name, "Carbohydrates", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Carbs", StringComparison.OrdinalIgnoreCase)) ni.CarbsGrams = amount;
                        if (string.Equals(name, "Protein", StringComparison.OrdinalIgnoreCase)) ni.ProteinGrams = amount;
                    }
                    recipe.Nutrition = ni;
                }

                return recipe;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<Recipe>> SearchOpenRecipesAsync(string query, IEnumerable<string> requiredTerms, int number = 10)
        {
            var terms = requiredTerms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();

            var candidateQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(query))
            {
                candidateQueries.Add(query.Trim());
            }
            foreach (var term in terms)
            {
                candidateQueries.Add(term);
            }
            if (candidateQueries.Count == 0)
            {
                return new List<Recipe>();
            }

            var candidates = new Dictionary<int, Recipe>();
            foreach (var q in candidateQueries)
            {
                var url = $"https://www.themealdb.com/api/json/v1/1/search.php?s={Uri.EscapeDataString(q)}";
                var res = await _http.GetAsync(url);
                if (!res.IsSuccessStatusCode) continue;

                using var stream = await res.Content.ReadAsStreamAsync();
                var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, _json);
                if (!root.TryGetProperty("meals", out var meals) || meals.ValueKind != JsonValueKind.Array) continue;

                foreach (var meal in meals.EnumerateArray())
                {
                    var recipe = TryMapMealDbRecipe(meal);
                    if (recipe == null) continue;
                    candidates[recipe.Id] = recipe;
                }
            }

            var filtered = candidates.Values
                .Where(r => terms.Length == 0 || terms.All(term => MealMatchesTerm(r, term)))
                .Take(Math.Clamp(number, 1, 50))
                .ToList();

            return filtered;
        }

        private static Recipe? TryMapMealDbRecipe(JsonElement meal)
        {
            if (!meal.TryGetProperty("idMeal", out var idProp)) return null;
            var idText = idProp.GetString();
            if (!int.TryParse(idText, out var id)) return null;

            var recipe = new Recipe
            {
                Id = id,
                Title = meal.TryGetProperty("strMeal", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                Summary = meal.TryGetProperty("strCategory", out var category) ? category.GetString() ?? string.Empty : string.Empty,
                Category = meal.TryGetProperty("strCategory", out var cat) ? cat.GetString() ?? string.Empty : string.Empty,
                Instructions = meal.TryGetProperty("strInstructions", out var instructions) ? instructions.GetString() ?? string.Empty : string.Empty,
                ImageUrl = EnsureImageUrl(meal.TryGetProperty("strMealThumb", out var image) ? image.GetString() ?? string.Empty : string.Empty),
                SourceUrl = meal.TryGetProperty("strSource", out var source) ? source.GetString() ?? string.Empty : string.Empty
            };

            var ingredients = new List<RecipeIngredient>();
            for (var i = 1; i <= 20; i++)
            {
                var ingredientName = GetString(meal, $"strIngredient{i}");
                if (string.IsNullOrWhiteSpace(ingredientName)) continue;

                ingredients.Add(new RecipeIngredient
                {
                    Ingredient = new Ingredient { Name = ingredientName.Trim() },
                    Quantity = GetString(meal, $"strMeasure{i}") ?? string.Empty
                });
            }
            recipe.Ingredients = ingredients;

            return recipe;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return prop.GetString();
        }

        private static bool MealMatchesTerm(Recipe recipe, string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return false;
            var normalized = term.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(recipe.Title) && recipe.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return recipe.Ingredients.Any(i =>
                !string.IsNullOrWhiteSpace(i.Ingredient?.Name) &&
                i.Ingredient!.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string EnsureImageUrl(string? imageUrl)
        {
            return string.IsNullOrWhiteSpace(imageUrl) ? DefaultFoodImage : imageUrl;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            if (response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            var shortBody = string.IsNullOrWhiteSpace(body)
                ? "No details from provider."
                : body[..Math.Min(250, body.Length)];

            throw new HttpRequestException(
                $"Spoonacular {operation} failed with {(int)response.StatusCode} ({response.StatusCode}). {shortBody}",
                null,
                response.StatusCode);
        }
    }
}
