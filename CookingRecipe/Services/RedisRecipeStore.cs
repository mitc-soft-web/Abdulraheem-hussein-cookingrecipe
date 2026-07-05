using StackExchange.Redis;
using System.Text.Json;
using CookingRecipe.Entities;
using System.Linq;

namespace CookingRecipe.Services
{
    public interface IRedisStore
    {
        Task SaveRecipeAsync(Recipe recipe);
        Task<Recipe?> GetRecipeAsync(int id);

        Task SaveSearchHistoryAsync(string deviceId, SearchHistory history);
        Task<List<SearchHistory>> GetSearchHistoryAsync(string deviceId, int count = 50);

        
        Task AddFavoriteAsync(string deviceId, int recipeId);
        Task RemoveFavoriteAsync(string deviceId, int recipeId);
        Task<List<int>> GetFavoriteIdsAsync(string deviceId);
        Task<List<Recipe>> GetFavoritesAsync(string deviceId);

        // search stored recipes (simple scan)
        Task<List<Recipe>> SearchStoredRecipesAsync(IEnumerable<string> ingredients, int max = 50);
    }

    public class RedisRecipeStore : IRedisStore
    {
        private const string RecipeIndexKey = "recipe:index";
        private static readonly TimeSpan RedisOpTimeout = TimeSpan.FromSeconds(2);
        private readonly IDatabase _db;
        private readonly IConnectionMultiplexer _mux;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public RedisRecipeStore(IConnectionMultiplexer multiplexer)
        {
            _mux = multiplexer;
            _db = multiplexer.GetDatabase();
        }

        public async Task SaveRecipeAsync(Recipe recipe)
        {
            try
            {
                var key = $"recipe:{recipe.Id}";
                var json = JsonSerializer.Serialize(recipe, _jsonOptions);
                await _db.StringSetAsync(key, json).WaitAsync(RedisOpTimeout);
                await _db.SetAddAsync(RecipeIndexKey, recipe.Id.ToString()).WaitAsync(RedisOpTimeout);
            }
            catch
            {
                // Keep API non-blocking when Redis is slow/unreachable.
            }
        }

        public async Task<Recipe?> GetRecipeAsync(int id)
        {
            try
            {
                var key = $"recipe:{id}";
                var value = await _db.StringGetAsync(key).WaitAsync(RedisOpTimeout);
                if (value.IsNullOrEmpty) return null;
                return JsonSerializer.Deserialize<Recipe>(value!, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveSearchHistoryAsync(string deviceId, SearchHistory history)
        {
            try
            {
                // ensure the history carries the device id and timestamp
                history.DeviceId = deviceId;
                if (history.SearchDate == default)
                {
                    history.SearchDate = DateTime.UtcNow;
                }

                var key = $"history:{deviceId}";
                var json = JsonSerializer.Serialize(history, _jsonOptions);
                await _db.ListLeftPushAsync(key, json).WaitAsync(RedisOpTimeout);
                // keep only the most recent 100 entries
                await _db.ListTrimAsync(key, 0, 99).WaitAsync(RedisOpTimeout);
            }
            catch
            {
                // keep API non-blocking when Redis is slow/unreachable.
            }
        }

        public async Task<List<SearchHistory>> GetSearchHistoryAsync(string deviceId, int count = 50)
        {
            try
            {
                var key = $"history:{deviceId}";
                var values = await _db.ListRangeAsync(key, 0, count - 1).WaitAsync(RedisOpTimeout);
                var result = new List<SearchHistory>(values.Length);
                foreach (var v in values)
                {
                    if (v.IsNullOrEmpty) continue;
                    var item = JsonSerializer.Deserialize<SearchHistory>(v!, _jsonOptions);
                    if (item != null) result.Add(item);
                }
                return result;
            }
            catch
            {
                return new List<SearchHistory>();
            }
        }

        // Favorites stored as Redis set of recipe ids
        public async Task AddFavoriteAsync(string deviceId, int recipeId)
        {
            try
            {
                var key = $"favorites:{deviceId}";
                await _db.SetAddAsync(key, recipeId.ToString()).WaitAsync(RedisOpTimeout);
            }
            catch
            {
                // keep API non-blocking when Redis is slow/unreachable.
            }
        }

        public async Task RemoveFavoriteAsync(string deviceId, int recipeId)
        {
            try
            {
                var key = $"favorites:{deviceId}";
                await _db.SetRemoveAsync(key, recipeId.ToString()).WaitAsync(RedisOpTimeout);
            }
            catch
            {
                // keep API non-blocking when Redis is slow/unreachable.
            }
        }

        public async Task<List<int>> GetFavoriteIdsAsync(string deviceId)
        {
            try
            {
                var key = $"favorites:{deviceId}";
                var members = await _db.SetMembersAsync(key).WaitAsync(RedisOpTimeout);
                return members
                    .Select(m => m.ToString())
                    .Where(value => int.TryParse(value, out _))
                    .Select(int.Parse)
                    .ToList();
            }
            catch
            {
                return new List<int>();
            }
        }

        public async Task<List<Recipe>> GetFavoritesAsync(string deviceId)
        {
            try
            {
                var favoriteIds = await GetFavoriteIdsAsync(deviceId);
                var members = favoriteIds.Select(id => (RedisValue)id.ToString()).ToArray();
                var result = new List<Recipe>(members.Length);
                foreach (var m in members)
                {
                    if (m.IsNullOrEmpty) continue;
                    if (int.TryParse(m, out var id))
                    {
                        var r = await GetRecipeAsync(id);
                        if (r != null) result.Add(r);
                    }
                }
                return result;
            }
            catch
            {
                return new List<Recipe>();
            }
        }

        // naive scan of stored recipe keys and filter by ingredient names
        public async Task<List<Recipe>> SearchStoredRecipesAsync(IEnumerable<string> ingredients, int max = 50)
        {
            try
            {
                var normalized = ingredients.Where(i => !string.IsNullOrWhiteSpace(i))
                                            .Select(i => i.Trim().ToLowerInvariant())
                                            .ToArray();

                var result = new List<Recipe>();
                var ids = await _db.SetMembersAsync(RecipeIndexKey).WaitAsync(RedisOpTimeout);
                foreach (var member in ids)
                {
                    if (member.IsNullOrEmpty) continue;
                    var key = $"recipe:{member}";
                    var value = await _db.StringGetAsync(key).WaitAsync(RedisOpTimeout);
                    if (value.IsNullOrEmpty) continue;
                    var recipe = JsonSerializer.Deserialize<Recipe>(value!, _jsonOptions);
                    if (recipe == null) continue;

                    // build a searchable string of ingredient names
                    var names = recipe.Ingredients?.Select(ri => ri.Ingredient?.Name ?? ri.Quantity ?? string.Empty)
                                      .Where(s => !string.IsNullOrWhiteSpace(s))
                                      .Select(s => s.ToLowerInvariant())
                                      .ToArray() ?? Array.Empty<string>();

                    var matchesAll = normalized.All(n => names.Any(name => name.Contains(n)));
                    if (matchesAll)
                    {
                        result.Add(recipe);
                        if (result.Count >= max) break;
                    }
                }

                // Backward-compatible fallback for older data where recipe index is empty.
                if (result.Count == 0 && normalized.Length == 0)
                {
                    try
                    {
                        var endpoint = _mux.GetEndPoints().FirstOrDefault();
                        if (endpoint != null)
                        {
                            var server = _mux.GetServer(endpoint);
                            foreach (var key in server.Keys(pattern: "recipe:*").Take(1000))
                            {
                                var value = await _db.StringGetAsync(key).WaitAsync(RedisOpTimeout);
                                if (value.IsNullOrEmpty) continue;
                                var recipe = JsonSerializer.Deserialize<Recipe>(value!, _jsonOptions);
                                if (recipe == null) continue;
                                result.Add(recipe);
                                if (result.Count >= max) break;
                            }
                        }
                    }
                    catch
                    {
                        // Keep search resilient if server scan is not allowed by provider.
                    }
                }

                return result;
            }
            catch
            {
                // Never break API response due to Redis provider restrictions/outages.
                return new List<Recipe>();
            }
        }
    }
}
