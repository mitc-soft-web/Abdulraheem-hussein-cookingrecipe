using CookingRecipe.Conntext;
using CookingRecipe.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookingRecipe.Services
{
    public class DatabaseRecipeStore : IRedisStore
    {
        private readonly CookingRecipeContext _context;

        public DatabaseRecipeStore(CookingRecipeContext context)
        {
            _context = context;
        }

        public async Task SaveRecipeAsync(Recipe recipe)
        {
            var existing = await _context.Recipes
                .Include(r => r.Ingredients)
                .FirstOrDefaultAsync(r => r.Id == recipe.Id);

            if (existing == null)
            {
                existing = new Recipe { Id = recipe.Id };
                _context.Recipes.Add(existing);
            }
            else if (existing.Ingredients.Count > 0)
            {
                _context.RecipeIngredients.RemoveRange(existing.Ingredients);
            }

            existing.Title = recipe.Title;
            existing.Summary = recipe.Summary;
            existing.Instructions = recipe.Instructions;
            existing.Category = recipe.Category;
            existing.ImageUrl = recipe.ImageUrl;
            existing.ReadyInMinutes = recipe.ReadyInMinutes;
            existing.Servings = recipe.Servings;
            existing.SourceUrl = recipe.SourceUrl;
            existing.Nutrition = recipe.Nutrition ?? new NutritionInfo();

            await _context.SaveChangesAsync();

            var ingredientItems = (recipe.Ingredients ?? new List<RecipeIngredient>())
                .Select(item => new
                {
                    Name = item.Ingredient?.Name?.Trim(),
                    Notes = item.Ingredient?.Notes,
                    Quantity = item.Quantity ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var item in ingredientItems)
            {
                var ingredientName = item.Name!;

                var ingredient = await _context.Ingredients
                    .FirstOrDefaultAsync(i => i.Name.ToLower() == ingredientName.ToLower());

                if (ingredient == null)
                {
                    ingredient = new Ingredient
                    {
                        Name = ingredientName,
                        Notes = item.Notes
                    };
                    _context.Ingredients.Add(ingredient);
                    await _context.SaveChangesAsync();
                }

                _context.RecipeIngredients.Add(new RecipeIngredient
                {
                    RecipeId = existing.Id,
                    IngredientId = ingredient.Id,
                    Quantity = item.Quantity
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<Recipe?> GetRecipeAsync(int id)
        {
            var recipe = await _context.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id);

            return recipe == null ? null : CloneRecipe(recipe);
        }

        public async Task SaveSearchHistoryAsync(string deviceId, SearchHistory history)
        {
            history.DeviceId = deviceId;
            if (history.SearchDate == default)
            {
                history.SearchDate = DateTime.UtcNow;
            }

            _context.SearchHistories.Add(history);
            await _context.SaveChangesAsync();

            var oldItems = await _context.SearchHistories
                .Where(h => h.DeviceId == deviceId)
                .OrderByDescending(h => h.SearchDate)
                .Skip(100)
                .ToListAsync();

            if (oldItems.Count > 0)
            {
                _context.SearchHistories.RemoveRange(oldItems);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<SearchHistory>> GetSearchHistoryAsync(string deviceId, int count = 50)
        {
            return await _context.SearchHistories
                .AsNoTracking()
                .Where(h => h.DeviceId == deviceId)
                .OrderByDescending(h => h.SearchDate)
                .Take(count)
                .ToListAsync();
        }

        public async Task AddFavoriteAsync(string deviceId, int recipeId)
        {
            var exists = await _context.FavoriteRecipes
                .AnyAsync(f => f.DeviceId == deviceId && f.RecipeId == recipeId);

            if (exists) return;

            _context.FavoriteRecipes.Add(new FavoriteRecipe
            {
                DeviceId = deviceId,
                RecipeId = recipeId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        public async Task RemoveFavoriteAsync(string deviceId, int recipeId)
        {
            var favorite = await _context.FavoriteRecipes
                .FirstOrDefaultAsync(f => f.DeviceId == deviceId && f.RecipeId == recipeId);

            if (favorite == null) return;

            _context.FavoriteRecipes.Remove(favorite);
            await _context.SaveChangesAsync();
        }

        public async Task<List<int>> GetFavoriteIdsAsync(string deviceId)
        {
            return await _context.FavoriteRecipes
                .AsNoTracking()
                .Where(f => f.DeviceId == deviceId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.RecipeId)
                .ToListAsync();
        }

        public async Task<List<Recipe>> GetFavoritesAsync(string deviceId)
        {
            var ids = await GetFavoriteIdsAsync(deviceId);
            if (ids.Count == 0) return new List<Recipe>();

            var recipes = await _context.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
                .Where(r => ids.Contains(r.Id))
                .ToListAsync();

            return ids
                .Select(id => recipes.FirstOrDefault(r => r.Id == id))
                .Where(r => r != null)
                .Select(r => CloneRecipe(r!))
                .ToList();
        }

        public async Task<List<Recipe>> SearchStoredRecipesAsync(IEnumerable<string> ingredients, int max = 50)
        {
            var normalized = ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim().ToLowerInvariant())
                .ToArray();

            var recipes = await _context.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
                .ToListAsync();

            return recipes
                .Where(recipe =>
                {
                    var names = recipe.Ingredients
                        .Select(ri => ri.Ingredient?.Name ?? ri.Quantity ?? string.Empty)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.ToLowerInvariant())
                        .ToArray();

                    return normalized.All(term => names.Any(name => name.Contains(term)));
                })
                .Take(Math.Clamp(max, 1, 50))
                .Select(CloneRecipe)
                .ToList();
        }

        private static Recipe CloneRecipe(Recipe recipe)
        {
            return new Recipe
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Summary = recipe.Summary,
                Instructions = recipe.Instructions,
                Category = recipe.Category,
                ImageUrl = recipe.ImageUrl,
                ReadyInMinutes = recipe.ReadyInMinutes,
                Servings = recipe.Servings,
                SourceUrl = recipe.SourceUrl,
                Nutrition = recipe.Nutrition ?? new NutritionInfo(),
                Ingredients = recipe.Ingredients.Select(ri => new RecipeIngredient
                {
                    RecipeId = recipe.Id,
                    IngredientId = ri.IngredientId,
                    Ingredient = ri.Ingredient == null
                        ? null
                        : new Ingredient
                        {
                            Id = ri.Ingredient.Id,
                            Name = ri.Ingredient.Name,
                            Notes = ri.Ingredient.Notes
                        },
                    Quantity = ri.Quantity
                }).ToList()
            };
        }
    }
}
