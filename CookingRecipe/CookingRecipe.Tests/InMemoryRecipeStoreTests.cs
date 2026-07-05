using CookingRecipe.Entities;
using CookingRecipe.Services;

namespace CookingRecipe.Tests;

public class InMemoryRecipeStoreTests
{
    [Fact]
    public async Task AddFavoriteAsync_ReturnsStoredRecipeInFavorites()
    {
        var store = new InMemoryRecipeStore();
        var recipe = new Recipe
        {
            Id = 42,
            Title = "Jollof Rice",
            Ingredients = new List<RecipeIngredient>
            {
                new()
                {
                    RecipeId = 42,
                    IngredientId = 1,
                    Ingredient = new Ingredient { Id = 1, Name = "Rice" },
                    Quantity = "2 cups"
                }
            }
        };

        await store.SaveRecipeAsync(recipe);
        await store.AddFavoriteAsync("device-1", recipe.Id);

        var favorites = await store.GetFavoritesAsync("device-1");

        Assert.Single(favorites);
        Assert.Equal(recipe.Id, favorites[0].Id);
        Assert.Equal("Jollof Rice", favorites[0].Title);
    }
}
