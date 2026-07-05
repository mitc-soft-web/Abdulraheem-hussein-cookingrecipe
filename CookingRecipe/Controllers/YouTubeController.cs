using CookingRecipe.Services;
using Microsoft.AspNetCore.Mvc;

namespace CookingRecipe.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class YouTubeController : ControllerBase
    {
        private readonly IYouTubeService _youtube;

        public YouTubeController(IYouTubeService youtube)
        {
            _youtube = youtube;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int max = 6, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("query required");
            }

            if (!_youtube.IsConfigured)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "YouTube API key is missing. Set YouTube:ApiKey in appsettings or user secrets.");
            }

            try
            {
                var videos = await _youtube.SearchRecipeVideosAsync(query, Math.Clamp(max, 1, 12), cancellationToken);
                return Ok(videos);
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, "YouTube video search is unavailable right now. Please try again later.");
            }
        }
    }
}