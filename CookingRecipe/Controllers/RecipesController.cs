using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CookingRecipe.Conntext;
using CookingRecipe.Services;
using CookingRecipe.Entities;
using CookingRecipe.Dtos;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CookingRecipe.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecipesController : ControllerBase
    {
        private const string DefaultFoodImage = "https://images.unsplash.com/photo-1498837167922-ddd27525d352?auto=format&fit=crop&w=1200&q=80";
        private static readonly Regex ExcludedTitlePattern = new(
            @"^(festival|festeval|village pot|sunday(?:\s+special)?|signature|smoky)\b|\b(?:rice|yam|plantain|beans|spaghetti|potato)\s+pepper\s+mix\b|\bofensala\s+catfish\b|\b(lagos|abuja|port harcourt|ibadan|enugu|kano|benin)\s+style\b|\b(chef style|market style|home style|family pot)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Dictionary<string, string[]> IngredientAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["maggi"] = ["bouillon cube", "stock cube", "seasoning cube", "bouillon"],
            ["tomato paste"] = ["tomato puree", "tomato sauce", "concentrated tomato"],
            ["stew"] = ["stewed tomato", "tomato sauce", "sauce"],
            ["beans"] = ["bean", "kidney beans", "black beans", "pinto beans", "white beans"]
        };
        private static DateTimeOffset? _spoonacularQuotaBlockedUntil;

        private readonly ISpoonacularService _spoon;
        private readonly INigerianRecipeDatasetService _nigerianDataset;
        private readonly IRedisStore _store;
        private readonly CookingRecipeContext _context;
        private const string CookieName = "deviceId";
        private const int MaxProviderCandidates = 60;
        private const int ProviderDetailBatchSize = 6;

        public RecipesController(ISpoonacularService spoon, INigerianRecipeDatasetService nigerianDataset, IRedisStore store, CookingRecipeContext context)
        {
            _spoon = spoon;
            _nigerianDataset = nigerianDataset;
            _store = store;
            _context = context;
        }

        // Search live (Spoonacular) and save results to Redis
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string ingredients, [FromQuery] int max = 10)
        {
            if (string.IsNullOrWhiteSpace(ingredients)) return BadRequest("ingredients required");

            var parts = ParseIngredientsInput(ingredients);
            if (parts.Length == 0) return BadRequest("Provide at least one valid ingredient.");

            var sanitizedMax = Math.Clamp(max, 1, 50);
            var prioritizedNigerian = FilterDisallowedRecipeTitles(_nigerianDataset.SearchByIngredientsPriority(parts, sanitizedMax));

            if (prioritizedNigerian.Count > 0)
            {
                EnsureRecipeImages(prioritizedNigerian);
                CacheRecipesInBackground(prioritizedNigerian);
                SaveSearchHistoryInBackground(string.Join(',', parts), "nigerian-priority-search", prioritizedNigerian);
                return Ok(prioritizedNigerian);
            }

            if (IsSpoonacularQuotaBlocked())
            {
                var fallback = await BuildLocalSearchFallbackAsync(parts, sanitizedMax);
                if (fallback.Count == 0 && parts.Length == 1)
                {
                    fallback = await GetOpenSearchFallbackSuggestionsAsync(ingredients, parts, sanitizedMax);
                }
                SaveSearchHistoryInBackground(string.Join(',', parts), "local-quota-fallback-search", fallback);
                return Ok(fallback);
            }

            if (!_spoon.IsConfigured)
            {
                if (prioritizedNigerian.Count > 0)
                {
                    EnsureRecipeImages(prioritizedNigerian);
                    CacheRecipesInBackground(prioritizedNigerian);
                    SaveSearchHistoryInBackground(string.Join(',', parts), "nigerian-priority-search", prioritizedNigerian);
                    return Ok(prioritizedNigerian);
                }

                return StatusCode(StatusCodes.Status500InternalServerError, "Spoonacular API key is missing. Set Spoonacular:ApiKey in appsettings or user secrets.");
            }

            List<Recipe> list;
            try
            {
                if (parts.Length == 1)
                {
                    list = await GetNameFallbackSuggestionsAsync(ingredients, sanitizedMax);
                    if (list.Count == 0)
                    {
                        list = await GetStrictSuggestionsAsync(parts, sanitizedMax);
                    }
                }
                else
                {
                    list = await GetStrictSuggestionsAsync(parts, sanitizedMax);
                }

                if (prioritizedNigerian.Count > 0)
                {
                    list = PrioritizeLocalRecipes(prioritizedNigerian, list, sanitizedMax);
                }

                list = FilterDisallowedRecipeTitles(list);
                EnsureRecipeImages(list);
            }
            catch (HttpRequestException ex)
            {
                if (!IsSpoonacularQuotaFailure(ex))
                {
                    return StatusCode(StatusCodes.Status502BadGateway, "Recipe provider is unavailable right now. Please try again later.");
                }

                RememberSpoonacularQuotaFailure();
                list = await BuildLocalSearchFallbackAsync(parts, sanitizedMax, prioritizedNigerian);
                if (list.Count == 0 && parts.Length == 1)
                {
                    list = await GetOpenSearchFallbackSuggestionsAsync(ingredients, parts, sanitizedMax);
                }
            }

            SaveSearchHistoryInBackground(string.Join(',', parts), "ingredients-search", list);
            return Ok(list);
        }

        // Search by a JSON body so users can submit a list of ingredients directly
        [HttpPost("suggest-by-ingredients")]
        public async Task<IActionResult> SuggestByIngredients([FromBody] IngredientSuggestionRequestDto request)
        {
            if (request is null || request.Ingredients.Count == 0)
            {
                return BadRequest("Provide at least one ingredient.");
            }

            var normalized = request.Ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .SelectMany(SplitIngredientParts)
                .Select(i => i.Trim())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalized.Length == 0)
            {
                return BadRequest("Provide at least one valid ingredient.");
            }

            var sanitizedMax = Math.Clamp(request.Max, 1, 50);
            var localResults = FilterDisallowedRecipeTitles(_nigerianDataset.SearchByIngredientsPriority(normalized, sanitizedMax));

            if (localResults.Count > 0)
            {
                EnsureRecipeImages(localResults);
                CacheRecipesInBackground(localResults);
                SaveSearchHistoryInBackground(string.Join(',', normalized), "nigerian-priority-suggest", localResults);
                return Ok(localResults);
            }

            if (IsSpoonacularQuotaBlocked())
            {
                var fallback = await BuildLocalSearchFallbackAsync(normalized, sanitizedMax);
                SaveSearchHistoryInBackground(string.Join(',', normalized), "local-quota-fallback-suggest", fallback);
                return Ok(fallback);
            }

            if (!_spoon.IsConfigured)
            {
                if (localResults.Count > 0)
                {
                    EnsureRecipeImages(localResults);
                    CacheRecipesInBackground(localResults);
                    SaveSearchHistoryInBackground(string.Join(',', normalized), "nigerian-priority-suggest", localResults);
                    return Ok(localResults);
                }

                return StatusCode(StatusCodes.Status500InternalServerError, "Spoonacular API key is missing. Set Spoonacular:ApiKey in appsettings or user secrets.");
            }

            List<Recipe> enriched;
            try
            {
                enriched = await GetStrictSuggestionsAsync(normalized, sanitizedMax);
                if (localResults.Count > 0)
                {
                    enriched = PrioritizeLocalRecipes(localResults, enriched, sanitizedMax);
                }
                enriched = FilterDisallowedRecipeTitles(enriched);
                EnsureRecipeImages(enriched);
            }
            catch (HttpRequestException ex)
            {
                if (!IsSpoonacularQuotaFailure(ex))
                {
                    return StatusCode(StatusCodes.Status502BadGateway, "Recipe provider is unavailable right now. Please try again later.");
                }

                RememberSpoonacularQuotaFailure();
                enriched = await BuildLocalSearchFallbackAsync(normalized, sanitizedMax, localResults);
            }

            CacheRecipesInBackground(enriched);
            SaveSearchHistoryInBackground(string.Join(',', normalized), "suggest-by-ingredients", enriched);

            return Ok(enriched);
        }

        private static List<Recipe> PrioritizeLocalRecipes(List<Recipe> localResults, List<Recipe> remoteResults, int max)
        {
            var merged = localResults
                .Concat(remoteResults)
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .Take(Math.Clamp(max, 1, 50))
                .ToList();

            return merged;
        }

        private async Task<List<Recipe>> BuildLocalSearchFallbackAsync(string[] normalizedIngredients, int max, List<Recipe>? knownLocalResults = null)
        {
            var sanitizedMax = Math.Clamp(max, 1, 50);
            var localResults = FilterDisallowedRecipeTitles(knownLocalResults ?? _nigerianDataset.SearchByIngredientsPriority(normalizedIngredients, sanitizedMax))
                .Take(sanitizedMax)
                .ToList();

            if (localResults.Count > 0)
            {
                EnsureRecipeImages(localResults);
                CacheRecipesInBackground(localResults);
                return localResults;
            }

            var cachedResults = FilterDisallowedRecipeTitles(await _store.SearchStoredRecipesAsync(normalizedIngredients, sanitizedMax));
            var fallback = localResults
                .Concat(cachedResults)
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .Take(sanitizedMax)
                .ToList();

            EnsureRecipeImages(fallback);
            CacheRecipesInBackground(fallback);

            return fallback;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var databaseRecipes = await _context.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
                .ToListAsync();

            var recipes = FilterDisallowedRecipeTitles(
                databaseRecipes.Select(CloneRecipeForResponse)
                    .Concat(_nigerianDataset.GetAll()))
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .OrderBy(r => r.Id)
                .ToList();

            EnsureRecipeImages(recipes);
            return Ok(recipes);
        }

        // Get recipe detail (from redis or spoonacular)
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var nigerian = _nigerianDataset.GetById(id);
            if (nigerian != null)
            {
                if (IsDisallowedRecipe(nigerian))
                {
                    return NotFound();
                }
                EnsureRecipeImage(nigerian);
                try
                {
                    await _store.SaveRecipeAsync(nigerian);
                }
                catch
                {
                    // ignore cache failures
                }
                return Ok(nigerian);
            }

            var r = await _store.GetRecipeAsync(id);
            if (r != null)
            {
                if (IsDisallowedRecipe(r))
                {
                    return NotFound();
                }
                return Ok(r);
            }

            if (!_spoon.IsConfigured)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Spoonacular API key is missing. Set Spoonacular:ApiKey in appsettings or user secrets.");
            }

            try
            {
                r = await _spoon.GetRecipeDetailAsync(id);
            }
            catch (HttpRequestException ex)
            {
                if (IsSpoonacularQuotaFailure(ex))
                {
                    RememberSpoonacularQuotaFailure();
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Spoonacular daily quota has been reached. Try a local recipe or search again later.");
                }

                return StatusCode(StatusCodes.Status502BadGateway, "Recipe provider is unavailable right now. Please try again later.");
            }

            if (r == null) return NotFound();
            if (IsDisallowedRecipe(r)) return NotFound();
            EnsureRecipeImage(r);

            await _store.SaveRecipeAsync(r);
            return Ok(r);
        }

        [HttpPost("{id:int}/favorite")]
        public async Task<IActionResult> AddFavorite(int id)
        {
            var deviceId = GetOrCreateDeviceId();
            var recipe = await ResolveRecipeForFavoriteAsync(id);
            if (recipe == null)
            {
                return NotFound("Recipe could not be found.");
            }

            try
            {
                await _store.SaveRecipeAsync(recipe);
            }
            catch
            {
                // Favorite IDs can still be saved even if recipe caching fails.
            }
            await _store.AddFavoriteAsync(deviceId, id);
            return Ok(recipe);
        }

        [HttpDelete("{id:int}/favorite")]
        public async Task<IActionResult> RemoveFavorite(int id)
        {
            var deviceId = GetOrCreateDeviceId();
            await _store.RemoveFavoriteAsync(deviceId, id);
            return Ok();
        }

        [HttpGet("favorites")]
        public async Task<IActionResult> GetFavorites()
        {
            var deviceId = GetOrCreateDeviceId();
            var favoriteIds = await _store.GetFavoriteIdsAsync(deviceId);
            var list = new List<Recipe>();

            foreach (var id in favoriteIds)
            {
                var recipe = await ResolveRecipeForFavoriteAsync(id);
                if (recipe != null)
                {
                    list.Add(recipe);
                    try
                    {
                        await _store.SaveRecipeAsync(recipe);
                    }
                    catch
                    {
                        // Keep favorites readable even if cache persistence is unavailable.
                    }
                }
            }

            if (list.Count == 0)
            {
                list = await _store.GetFavoritesAsync(deviceId);
            }

            list = FilterDisallowedRecipeTitles(list)
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .ToList();
            EnsureRecipeImages(list);
            return Ok(list);
        }

        private async Task<Recipe?> ResolveRecipeForFavoriteAsync(int id)
        {
            var nigerian = _nigerianDataset.GetById(id);
            if (nigerian != null)
            {
                if (IsDisallowedRecipe(nigerian)) return null;
                EnsureRecipeImage(nigerian);
                return nigerian;
            }

            var stored = await _store.GetRecipeAsync(id);
            if (stored != null)
            {
                if (IsDisallowedRecipe(stored)) return null;
                EnsureRecipeImage(stored);
                return stored;
            }

            var databaseRecipe = await _context.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (databaseRecipe != null)
            {
                var cloned = CloneRecipeForResponse(databaseRecipe);
                if (IsDisallowedRecipe(cloned)) return null;
                EnsureRecipeImage(cloned);
                return cloned;
            }

            if (!_spoon.IsConfigured)
            {
                return null;
            }

            try
            {
                var remote = await _spoon.GetRecipeDetailAsync(id);
                if (remote == null || IsDisallowedRecipe(remote)) return null;
                EnsureRecipeImage(remote);
                return remote;
            }
            catch (HttpRequestException ex)
            {
                if (IsSpoonacularQuotaFailure(ex))
                {
                    RememberSpoonacularQuotaFailure();
                }

                return null;
            }
        }

        [HttpGet("stored/search")]
        public async Task<IActionResult> SearchStored([FromQuery] string ingredients, [FromQuery] int max = 50)
        {
            if (string.IsNullOrWhiteSpace(ingredients)) return BadRequest("ingredients required");
            var parts = ingredients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var list = FilterDisallowedRecipeTitles(await _store.SearchStoredRecipesAsync(parts, max));
            EnsureRecipeImages(list);
            return Ok(list);
        }

        private string GetOrCreateDeviceId()
        {
            var deviceId = Request.Cookies[CookieName];
            if (!string.IsNullOrWhiteSpace(deviceId)) return deviceId;

            if (HttpContext.Items.TryGetValue(CookieName, out var v) && v is string s && !string.IsNullOrWhiteSpace(s)) return s;

            deviceId = Guid.NewGuid().ToString();
            Response.Cookies.Append(CookieName, deviceId, new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                SameSite = IsSecureRequest() ? SameSiteMode.None : SameSiteMode.Lax,
                Secure = IsSecureRequest()
            });

            return deviceId;
        }

        private bool IsSecureRequest()
        {
            return Request.IsHttps ||
                string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SaveSearchHistorySafeAsync(string searchText, string category, List<Recipe> results)
        {
            try
            {
                var deviceId = GetOrCreateDeviceId();
                var history = new SearchHistory
                {
                    DeviceId = deviceId,
                    SearchText = searchText,
                    Category = category,
                    SearchDate = DateTime.UtcNow,
                    JsonResult = JsonSerializer.Serialize(results)
                };

                await _store.SaveSearchHistoryAsync(deviceId, history);
            }
            catch
            {
              
            }
        }

        private void SaveSearchHistoryInBackground(string searchText, string category, List<Recipe> results)
        {
            var deviceId = GetOrCreateDeviceId();
            var history = new SearchHistory
            {
                DeviceId = deviceId,
                SearchText = searchText,
                Category = category,
                SearchDate = DateTime.UtcNow,
                JsonResult = JsonSerializer.Serialize(results)
            };

            try
            {
                _store.SaveSearchHistoryAsync(deviceId, history).GetAwaiter().GetResult();
            }
            catch
            {
               
            }
        }

        private void CacheRecipesInBackground(IEnumerable<Recipe> recipes)
        {
            var recipesToCache = recipes.ToList();
            if (recipesToCache.Count == 0) return;

            try
            {
                foreach (var recipe in recipesToCache)
                {
                    _store.SaveRecipeAsync(recipe).GetAwaiter().GetResult();
                }
            }
            catch
            {
                
            }
        }

        private async Task<List<Recipe>> GetStrictSuggestionsAsync(string[] normalizedIngredients, int max)
        {
            var sanitizedMax = Math.Clamp(max, 1, 50);
            var providerTerms = normalizedIngredients
                .Select(MapToProviderIngredient)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var ingredientsCsv = string.Join(',', providerTerms);

            var candidateCount = Math.Clamp(sanitizedMax * 4, sanitizedMax, MaxProviderCandidates);
            var candidateIds = new List<int>();
            var fallbackCandidates = new List<Recipe>();
            HttpRequestException? providerFailure = null;
            try
            {
                candidateIds = await _spoon.SearchRecipeIdsByIncludedIngredientsAsync(ingredientsCsv, candidateCount);
                fallbackCandidates = await _spoon.SearchByIngredientsAsync(ingredientsCsv, candidateCount);
            }
            catch (HttpRequestException ex)
            {
                providerFailure = ex;
               
            }
            var fallbackById = fallbackCandidates
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .ToDictionary(r => r.Id, r => r);

            // Fallback source: findByIngredients can return broader candidates for strict post-filtering.
            if (candidateIds.Count == 0)
            {
                candidateIds = fallbackById.Keys.ToList();
            }
            else
            {
                candidateIds = candidateIds
                    .Concat(fallbackById.Keys)
                    .Distinct()
                    .Take(candidateCount)
                    .ToList();
            }

            var enriched = new List<Recipe>(sanitizedMax);
            foreach (var batch in candidateIds.Chunk(ProviderDetailBatchSize))
            {
                var candidates = await Task.WhenAll(batch.Select(recipeId => GetBestCandidateAsync(recipeId, fallbackById)));

                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;
                    if (!normalizedIngredients.All(term => RecipeContainsRequestedIngredient(candidate, term))) continue;

                    enriched.Add(candidate);
                    if (enriched.Count >= sanitizedMax) break;
                }

                if (enriched.Count >= sanitizedMax)
                {
                    break;
                }
            }

            if (enriched.Count == 0 && normalizedIngredients.Length == 1)
            {
                // For single-ingredient searches, return best available fallback candidates.
                var term = normalizedIngredients[0];
                foreach (var candidate in fallbackById.Values)
                {
                    if (!RecipeContainsRequestedIngredient(candidate, term)) continue;
                    enriched.Add(candidate);
                    if (enriched.Count >= sanitizedMax) break;
                }
            }

            if (enriched.Count == 0)
            {
                var cached = await _store.SearchStoredRecipesAsync(normalizedIngredients, sanitizedMax);
                enriched.AddRange(cached);
            }

            if (enriched.Count == 0 && providerFailure != null)
            {
                throw providerFailure;
            }

            CacheRecipesInBackground(enriched);

            return enriched;
        }

        private async Task<Recipe?> GetBestCandidateAsync(int recipeId, IReadOnlyDictionary<int, Recipe> fallbackById)
        {
            try
            {
                var detail = await _spoon.GetRecipeDetailAsync(recipeId);
                if (detail != null)
                {
                    return detail;
                }
            }
            catch
            {
              
            }

            return fallbackById.TryGetValue(recipeId, out var fallback) ? fallback : null;
        }

        private async Task<List<Recipe>> GetNameFallbackSuggestionsAsync(string query, int max)
        {
            var sanitizedMax = Math.Clamp(max, 1, 50);
            var openResults = await GetOpenSearchFallbackSuggestionsAsync(query, [query.Trim()], sanitizedMax);
            if (openResults.Count > 0)
            {
                return openResults;
            }

            var candidateCount = Math.Clamp(sanitizedMax * 10, sanitizedMax, 100);
            var ids = await _spoon.SearchRecipeIdsByTitleAsync(query.Trim(), candidateCount);
            if (ids.Count == 0) return new List<Recipe>();

            var list = new List<Recipe>(sanitizedMax);
            foreach (var batch in ids.Distinct().Take(candidateCount).Chunk(ProviderDetailBatchSize))
            {
                var details = await Task.WhenAll(batch.Select(GetRecipeDetailSafeAsync));
                foreach (var detail in details)
                {
                    if (detail == null) continue;
                    list.Add(detail);
                    if (list.Count >= sanitizedMax) break;
                }

                if (list.Count >= sanitizedMax)
                {
                    break;
                }
            }

            CacheRecipesInBackground(list);

            return list;
        }

        private async Task<List<Recipe>> GetOpenSearchFallbackSuggestionsAsync(string query, IEnumerable<string> requiredTerms, int max)
        {
            var sanitizedMax = Math.Clamp(max, 1, 50);
            var list = FilterDisallowedRecipeTitles(await _spoon.SearchOpenRecipesAsync(query.Trim(), requiredTerms, sanitizedMax))
                .Take(sanitizedMax)
                .ToList();

            EnsureRecipeImages(list);
            CacheRecipesInBackground(list);

            return list;
        }

        private async Task<Recipe?> GetRecipeDetailSafeAsync(int recipeId)
        {
            try
            {
                return await _spoon.GetRecipeDetailAsync(recipeId);
            }
            catch
            {
                return null;
            }
        }

        private static bool RecipeContainsTerm(Recipe recipe, string term)
        {
            var normalizedTerm = term.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedTerm)) return false;

            var variants = BuildVariants(normalizedTerm);
            var values = new List<string>();

            if (!string.IsNullOrWhiteSpace(recipe.Title))
            {
                values.Add(recipe.Title.ToLowerInvariant());
            }

            if (recipe.Ingredients != null)
            {
                values.AddRange(recipe.Ingredients
                    .Select(i => i.Ingredient?.Name ?? i.Quantity ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.ToLowerInvariant()));
            }

            return variants.Any(v => values.Any(val => val.Contains(v)));
        }

        private static bool RecipeContainsRequestedIngredient(Recipe recipe, string requestedIngredient)
        {
            var candidates = BuildSearchCandidates(requestedIngredient);
            return candidates.Any(candidate => RecipeContainsTerm(recipe, candidate));
        }

        private static IEnumerable<string> BuildSearchCandidates(string requestedIngredient)
        {
            var cleaned = requestedIngredient.Trim().ToLowerInvariant();
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variant in BuildVariants(cleaned))
            {
                variants.Add(variant);
            }

            if (IngredientAliases.TryGetValue(cleaned, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    foreach (var variant in BuildVariants(alias.ToLowerInvariant()))
                    {
                        variants.Add(variant);
                    }
                }
            }

            return variants;
        }

        private static IEnumerable<string> SplitIngredientParts(string input)
        {
            return Regex.Split(input, @"\s*(?:,|;|\+|&|\band\b)\s*", RegexOptions.IgnoreCase)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim());
        }

        private static string MapToProviderIngredient(string ingredient)
        {
            var cleaned = ingredient.Trim().ToLowerInvariant();
            return cleaned switch
            {
                "maggi" => "bouillon cube",
                _ => cleaned
            };
        }

        private static string[] ParseIngredientsInput(string input)
        {
            return Regex.Split(input, @"\s*(?:,|;|\+|&|\band\b)\s*", RegexOptions.IgnoreCase)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<string> BuildVariants(string value)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value };

            if (value.EndsWith("es", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
            {
                variants.Add(value[..^2]);
            }

            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
            {
                variants.Add(value[..^1]);
            }

            return variants;
        }

        private static void EnsureRecipeImage(Recipe recipe)
        {
            if (string.IsNullOrWhiteSpace(recipe.ImageUrl))
            {
                recipe.ImageUrl = DefaultFoodImage;
            }
        }

        private static void EnsureRecipeImages(IEnumerable<Recipe> recipes)
        {
            foreach (var recipe in recipes)
            {
                EnsureRecipeImage(recipe);
            }
        }

        private static Recipe CloneRecipeForResponse(Recipe recipe)
        {
            return new Recipe
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Summary = recipe.Summary,
                Instructions = recipe.Instructions,
                Category = recipe.Category,
                ImageUrl = recipe.ImageUrl,
                SourceUrl = recipe.SourceUrl,
                ReadyInMinutes = recipe.ReadyInMinutes,
                Servings = recipe.Servings,
                Nutrition = new NutritionInfo
                {
                    Calories = recipe.Nutrition.Calories,
                    FatGrams = recipe.Nutrition.FatGrams,
                    CarbsGrams = recipe.Nutrition.CarbsGrams,
                    ProteinGrams = recipe.Nutrition.ProteinGrams
                },
                Ingredients = recipe.Ingredients.Select(i => new RecipeIngredient
                {
                    RecipeId = i.RecipeId,
                    IngredientId = i.IngredientId,
                    Quantity = i.Quantity,
                    Ingredient = new Ingredient
                    {
                        Id = i.Ingredient?.Id ?? i.IngredientId,
                        Name = i.Ingredient?.Name ?? string.Empty,
                        Notes = i.Ingredient?.Notes
                    }
                }).ToList()
            };
        }

        private static bool IsDisallowedTitle(string? title)
        {
            return !string.IsNullOrWhiteSpace(title) && ExcludedTitlePattern.IsMatch(title);
        }

        private static bool IsDisallowedRecipe(Recipe recipe)
        {
            if (IsDisallowedTitle(recipe.Title))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(recipe.Title) &&
                recipe.Title.Contains("goat", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return recipe.Ingredients.Any(i =>
                !string.IsNullOrWhiteSpace(i.Ingredient?.Name) &&
                i.Ingredient.Name.Contains("goat", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSpoonacularQuotaFailure(HttpRequestException ex)
        {
            return ex.StatusCode == System.Net.HttpStatusCode.PaymentRequired ||
                   ex.Message.Contains("daily points limit", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("PaymentRequired", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpoonacularQuotaBlocked()
        {
            return _spoonacularQuotaBlockedUntil > DateTimeOffset.UtcNow;
        }

        private static void RememberSpoonacularQuotaFailure()
        {
            _spoonacularQuotaBlockedUntil = DateTimeOffset.UtcNow.AddHours(12);
        }

        private static List<Recipe> FilterDisallowedRecipeTitles(IEnumerable<Recipe> recipes)
        {
            return recipes
                .Where(r => !IsDisallowedRecipe(r))
                .ToList();
        }
    }
}
