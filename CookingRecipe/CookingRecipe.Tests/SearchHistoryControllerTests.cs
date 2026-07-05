using CookingRecipe.Controllers;
using CookingRecipe.Dtos;
using CookingRecipe.Entities;
using CookingRecipe.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CookingRecipe.Tests;

public class SearchHistoryControllerTests
{
    [Fact]
    public async Task SaveSearch_ThenGetHistory_ReturnsSavedItem()
    {
        var store = new InMemoryRecipeStore();
        var controller = new SearchHistoryController(store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["deviceId"] = "device-abc";
        var request = new SaveSearchRequestDto
        {
            SearchText = "rice,tomato",
            Category = "ingredients-search",
            JsonResult = "[]"
        };

        var saveResult = await controller.SaveSearch(request);
        var historyResult = await controller.GetHistory();

        Assert.IsType<OkResult>(saveResult);
        var ok = Assert.IsType<OkObjectResult>(historyResult);
        var history = Assert.IsType<List<SearchHistory>>(ok.Value);
        Assert.Single(history);
        Assert.Equal("rice,tomato", history[0].SearchText);
        Assert.Equal("device-abc", history[0].DeviceId);
    }
}
