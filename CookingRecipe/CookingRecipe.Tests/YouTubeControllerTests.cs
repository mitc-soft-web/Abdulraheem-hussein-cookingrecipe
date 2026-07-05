using CookingRecipe.Controllers;
using CookingRecipe.Dtos;
using CookingRecipe.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CookingRecipe.Tests;

public class YouTubeControllerTests
{
    [Fact]
    public async Task Search_WhenQueryIsMissing_ReturnsBadRequest()
    {
        var controller = new YouTubeController(new FakeYouTubeService(isConfigured: true));

        var result = await controller.Search("");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Search_WhenApiKeyIsMissing_Returns500()
    {
        var controller = new YouTubeController(new FakeYouTubeService(isConfigured: false));

        var result = await controller.Search("jollof rice");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task Search_WhenConfigured_ReturnsVideos()
    {
        var controller = new YouTubeController(new FakeYouTubeService(isConfigured: true));

        var result = await controller.Search("jollof rice");

        var ok = Assert.IsType<OkObjectResult>(result);
        var videos = Assert.IsType<List<YouTubeVideoDto>>(ok.Value);
        Assert.Single(videos);
        Assert.Equal("Jollof Rice Tutorial", videos[0].Title);
    }

    private sealed class FakeYouTubeService : IYouTubeService
    {
        private readonly bool _isConfigured;

        public FakeYouTubeService(bool isConfigured)
        {
            _isConfigured = isConfigured;
        }

        public bool IsConfigured => _isConfigured;

        public Task<List<YouTubeVideoDto>> SearchRecipeVideosAsync(string query, int max = 6, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<YouTubeVideoDto>
            {
                new()
                {
                    VideoId = "abc123",
                    Title = "Jollof Rice Tutorial",
                    ThumbnailUrl = "https://example.com/jollof.jpg",
                    ChannelTitle = "Cooking Channel",
                    WatchUrl = "https://www.youtube.com/watch?v=abc123"
                }
            });
        }
    }
}