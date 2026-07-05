using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CookingRecipe.Entities;

namespace CookingRecipe.Services
{
   
    public class InMemoryRecipeStore : IRedisStore
    {
        private readonly ConcurrentDictionary<int, Recipe> _recipes = new();
        private readonly ConcurrentDictionary<string, List<SearchHistory>> _history = new();
        private readonly ConcurrentDictionary<string, HashSet<int>> _favorites = new();

        public Task AddFavoriteAsync(string deviceId, int recipeId)
        {
            var set = _favorites.GetOrAdd(deviceId, _ => new HashSet<int>());
            lock (set)
            {
                set.Add(recipeId);
            }
            return Task.CompletedTask;
        }

        public Task RemoveFavoriteAsync(string deviceId, int recipeId)
        {
            if (_favorites.TryGetValue(deviceId, out var set))
            {
                lock (set)
                {
                    set.Remove(recipeId);
                }
            }
            return Task.CompletedTask;
        }

        public Task<List<Recipe>> GetFavoritesAsync(string deviceId)
        {
            var list = new List<Recipe>();
            if (_favorites.TryGetValue(deviceId, out var set))
            {
                lock (set)
                {
                    foreach (var id in set)
                    {
                        if (_recipes.TryGetValue(id, out var r)) list.Add(r);
                    }
                }
            }
            return Task.FromResult(list);
        }

        public Task<List<int>> GetFavoriteIdsAsync(string deviceId)
        {
            if (_favorites.TryGetValue(deviceId, out var set))
            {
                lock (set)
                {
                    return Task.FromResult(set.ToList());
                }
            }

            return Task.FromResult(new List<int>());
        }

        public Task<Recipe?> GetRecipeAsync(int id)
        {
            _recipes.TryGetValue(id, out var r);
            return Task.FromResult<Recipe?>(r);
        }

        public Task<List<SearchHistory>> GetSearchHistoryAsync(string deviceId, int count = 50)
        {
            if (_history.TryGetValue(deviceId, out var list))
            {
                return Task.FromResult(list.Take(count).ToList());
            }
            return Task.FromResult(new List<SearchHistory>());
        }

        public Task SaveRecipeAsync(Recipe recipe)
        {
            _recipes[recipe.Id] = recipe;
            return Task.CompletedTask;
        }

        public Task SaveSearchHistoryAsync(string deviceId, SearchHistory history)
        {
            history.DeviceId = deviceId;
            if (history.SearchDate == default) history.SearchDate = DateTime.UtcNow;
            var list = _history.GetOrAdd(deviceId, _ => new List<SearchHistory>());
            lock (list)
            {
                list.Insert(0, history);
                if (list.Count > 100) list.RemoveRange(100, list.Count - 100);
            }
            return Task.CompletedTask;
        }

        public Task<List<Recipe>> SearchStoredRecipesAsync(IEnumerable<string> ingredients, int max = 50)
        {
            var normalized = ingredients.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim().ToLowerInvariant()).ToArray();
            var result = new List<Recipe>();
            foreach (var kv in _recipes)
            {
                var recipe = kv.Value;
                var names = recipe.Ingredients?.Select(ri => ri.Ingredient?.Name ?? ri.Quantity ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.ToLowerInvariant()).ToArray() ?? Array.Empty<string>();
                var matches = normalized.All(n => names.Any(name => name.Contains(n)));
                if (matches)
                {
                    result.Add(recipe);
                    if (result.Count >= max) break;
                }
            }
            return Task.FromResult(result);
        }
    }
}
