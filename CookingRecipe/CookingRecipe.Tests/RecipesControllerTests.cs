using CookingRecipe.Controllers;
using CookingRecipe.Conntext;
using CookingRecipe.Dtos;
using CookingRecipe.Entities;
using CookingRecipe.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CookingRecipe.Tests;

public class RecipesControllerTests
{
    [Fact]
    public async Task Search_WhenSpoonacularNotConfiguredAndNoLocalMatch_Returns500()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Search("avocado", 10);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task SuggestByIngredients_WhenBodyIsEmpty_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.SuggestByIngredients(new IngredientSuggestionRequestDto());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Search_NigerianMeal_WhenSpoonacularNotConfigured_ReturnsDatasetResults()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Search("jollof", 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var recipes = Assert.IsType<List<Recipe>>(ok.Value);
        Assert.NotEmpty(recipes);
        Assert.Contains(recipes, r => r.Title.Contains("Jollof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_RiceAndTomato_PrioritizesNigerianMatches()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Search("rice, tomato", 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var recipes = Assert.IsType<List<Recipe>>(ok.Value);
        Assert.NotEmpty(recipes);
        Assert.Contains(recipes, r => r.Title.Contains("Jollof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_BeansMaggiTomato_ReturnsMoiMoiCandidate()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Search("beans, maggi, tomato", 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var recipes = Assert.IsType<List<Recipe>>(ok.Value);
        Assert.NotEmpty(recipes);
        Assert.Contains(recipes, r => r.Title.Contains("Moi Moi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_SingleWordDish_UsesOpenSearchFallbackWhenStrictAndTitleMatchesAreEmpty()
    {
        await using var context = CreateContext();
        var controller = new RecipesController(
            new OpenSearchFallbackSpoonacularService(),
            new NigerianRecipeDatasetService(),
            new InMemoryRecipeStore(),
            context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Search("pizza", 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var recipes = Assert.IsType<List<Recipe>>(ok.Value);
        Assert.NotEmpty(recipes);
        Assert.Contains(recipes, r => r.Title.Contains("Pizza", StringComparison.OrdinalIgnoreCase));
    }

    private static RecipesController CreateController(CookingRecipeContext context)
    {
        return new RecipesController(
            new FakeSpoonacularService(),
            new NigerianRecipeDatasetService(),
            new InMemoryRecipeStore(),
            context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
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

    private sealed class FakeSpoonacularService : ISpoonacularService
    {
        public bool IsConfigured => false;

        public Task<Recipe?> GetRecipeDetailAsync(int id) => Task.FromResult<Recipe?>(null);

        public Task<List<Recipe>> SearchByIngredientsAsync(string ingredientsCsv, int number = 10) =>
            Task.FromResult(new List<Recipe>());

        public Task<List<int>> SearchRecipeIdsByIncludedIngredientsAsync(string ingredientsCsv, int number = 10) =>
            Task.FromResult(new List<int>());

        public Task<List<int>> SearchRecipeIdsByTitleAsync(string query, int number = 10) =>
            Task.FromResult(new List<int>());

        public Task<List<Recipe>> SearchOpenRecipesAsync(string query, IEnumerable<string> requiredTerms, int number = 10) =>
            Task.FromResult(new List<Recipe>());
    }

    private sealed class OpenSearchFallbackSpoonacularService : ISpoonacularService
    {
        public bool IsConfigured => true;

        public Task<Recipe?> GetRecipeDetailAsync(int id) => Task.FromResult<Recipe?>(null);

        public Task<List<Recipe>> SearchByIngredientsAsync(string ingredientsCsv, int number = 10) =>
            Task.FromResult(new List<Recipe>());

        public Task<List<int>> SearchRecipeIdsByIncludedIngredientsAsync(string ingredientsCsv, int number = 10) =>
            Task.FromResult(new List<int>());

        public Task<List<int>> SearchRecipeIdsByTitleAsync(string query, int number = 10) =>
            Task.FromResult(new List<int>());

        public Task<List<Recipe>> SearchOpenRecipesAsync(string query, IEnumerable<string> requiredTerms, int number = 10) =>
            Task.FromResult(new List<Recipe>
            {
                new()
                {
                    Id = 42,
                    Title = "Pizza Margherita",
                    Summary = "Classic pizza fallback result.",
                    ImageUrl = "https://example.com/pizza.jpg",
                    Ingredients = new List<RecipeIngredient>
                    {
                        new()
                        {
                            Ingredient = new Ingredient { Name = "pizza dough" },
                            Quantity = "1"
                        }
                    }
                }
            });
    }
}
