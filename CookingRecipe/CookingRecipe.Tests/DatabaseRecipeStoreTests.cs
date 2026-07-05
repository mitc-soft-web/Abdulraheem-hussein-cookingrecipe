using CookingRecipe.Conntext;
using CookingRecipe.Entities;
using CookingRecipe.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CookingRecipe.Tests;

public class DatabaseRecipeStoreTests
{
    [Fact]
    public async Task FavoritesAndHistory_WorkWithoutRedis()
    {
        await using var context = CreateContext();
        var store = new DatabaseRecipeStore(context);

        var recipe = new Recipe
        {
            Id = 123,
            Title = "Test Jollof Rice",
            Summary = "A test recipe.",
            Instructions = "1. Cook rice.",
            Category = "Nigerian",
            ImageUrl = "https://example.com/rice.jpg",
            ReadyInMinutes = 45,
            Servings = 4,
            Ingredients =
            [
                new RecipeIngredient
                {
                    Ingredient = new Ingredient { Name = "rice" },
                    Quantity = "2 cups"
                }
            ]
        };

        await store.SaveRecipeAsync(recipe);
        await store.AddFavoriteAsync("device-1", recipe.Id);
        await store.SaveSearchHistoryAsync("device-1", new SearchHistory
        {
            SearchText = "rice",
            Category = "test",
            JsonResult = "[]"
        });

        var favorites = await store.GetFavoritesAsync("device-1");
        var history = await store.GetSearchHistoryAsync("device-1");

        Assert.Single(favorites);
        Assert.Equal("Test Jollof Rice", favorites[0].Title);
        Assert.Single(history);
        Assert.Equal("rice", history[0].SearchText);
    }

    private static CookingRecipeContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CookingRecipeContext>()
            .UseSqlite(connection)
            .Options;

        var context = new CookingRecipeContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
