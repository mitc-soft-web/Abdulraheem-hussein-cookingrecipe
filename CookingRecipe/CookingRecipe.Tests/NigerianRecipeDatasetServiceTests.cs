using CookingRecipe.Services;

namespace CookingRecipe.Tests;

public class NigerianRecipeDatasetServiceTests
{
    private static readonly string[] IrrelevantTitleSnippets =
    [
        "festival",
        "festeval",
        "village pot",
        "signature",
        "sunday",
        "smoky",
        "pepper mix",
        "ofensala catfish"
    ];

    [Fact]
    public void Search_EmptyTerms_LoadsLargeDataset()
    {
        var service = new NigerianRecipeDatasetService();

        var results = service.Search(Array.Empty<string>(), 50);

        Assert.Equal(50, results.Count);
        Assert.All(results, recipe => Assert.False(string.IsNullOrWhiteSpace(recipe.ImageUrl)));
    }

    [Fact]
    public void SearchByIngredientsPriority_RiceAndTomato_IncludesJollof()
    {
        var service = new NigerianRecipeDatasetService();

        var results = service.SearchByIngredientsPriority(["rice", "tomato"], 15);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Title.Contains("Jollof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_EmptyTerms_ExcludesIrrelevantGeneratedTitles()
    {
        var service = new NigerianRecipeDatasetService();

        var results = service.Search(Array.Empty<string>(), 500);

        Assert.DoesNotContain(results, recipe =>
            IrrelevantTitleSnippets.Any(snippet => recipe.Title.Contains(snippet, StringComparison.OrdinalIgnoreCase)));
    }
}
