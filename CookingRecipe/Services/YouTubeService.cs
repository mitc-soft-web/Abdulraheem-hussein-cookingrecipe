using System.Net;
using System.Text.Json;
using CookingRecipe.Dtos;

namespace CookingRecipe.Services
{
    public interface IYouTubeService
    {
        bool IsConfigured { get; }
        Task<List<YouTubeVideoDto>> SearchRecipeVideosAsync(string query, int max = 6, CancellationToken cancellationToken = default);
    }

    public class YouTubeService : IYouTubeService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        public YouTubeService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["YouTube:ApiKey"] ?? string.Empty;
        }

        public async Task<List<YouTubeVideoDto>> SearchRecipeVideosAsync(string query, int max = 6, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return new List<YouTubeVideoDto>();
            }

            var sanitizedQuery = query.Trim();
            if (string.IsNullOrWhiteSpace(sanitizedQuery))
            {
                return new List<YouTubeVideoDto>();
            }

            var maxResults = Math.Clamp(max, 1, 12);
            var url =
                "https://www.googleapis.com/youtube/v3/search" +
                "?part=snippet" +
                "&type=video" +
                "&safeSearch=moderate" +
                "&order=relevance" +
                $"&maxResults={maxResults}" +
                $"&q={Uri.EscapeDataString($"{sanitizedQuery} recipe cooking")}" +
                $"&key={Uri.EscapeDataString(_apiKey)}";

            var response = await _http.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, "search");

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, _json, cancellationToken);
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return new List<YouTubeVideoDto>();
            }

            var videos = new List<YouTubeVideoDto>();
            foreach (var item in items.EnumerateArray())
            {
                var video = TryMapVideo(item);
                if (video == null) continue;

                videos.Add(video);
            }

            return videos;
        }

        private static YouTubeVideoDto? TryMapVideo(JsonElement item)
        {
            if (!item.TryGetProperty("id", out var id) ||
                !id.TryGetProperty("videoId", out var videoIdProp))
            {
                return null;
            }

            var videoId = videoIdProp.GetString();
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return null;
            }

            if (!item.TryGetProperty("snippet", out var snippet))
            {
                return null;
            }

            var thumbnailUrl = string.Empty;
            if (snippet.TryGetProperty("thumbnails", out var thumbnails))
            {
                thumbnailUrl = GetThumbnailUrl(thumbnails, "medium")
                    ?? GetThumbnailUrl(thumbnails, "high")
                    ?? GetThumbnailUrl(thumbnails, "default")
                    ?? string.Empty;
            }

            return new YouTubeVideoDto
            {
                VideoId = videoId,
                Title = WebUtility.HtmlDecode(GetString(snippet, "title") ?? string.Empty),
                Description = WebUtility.HtmlDecode(GetString(snippet, "description") ?? string.Empty),
                ThumbnailUrl = thumbnailUrl,
                ChannelTitle = WebUtility.HtmlDecode(GetString(snippet, "channelTitle") ?? string.Empty),
                PublishedAt = TryGetPublishedAt(snippet),
                WatchUrl = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}"
            };
        }

        private static string? GetThumbnailUrl(JsonElement thumbnails, string size)
        {
            if (!thumbnails.TryGetProperty(size, out var thumbnail))
            {
                return null;
            }

            return GetString(thumbnail, "url");
        }

        private static DateTimeOffset? TryGetPublishedAt(JsonElement snippet)
        {
            var value = GetString(snippet, "publishedAt");
            return DateTimeOffset.TryParse(value, out var publishedAt) ? publishedAt : null;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return property.GetString();
        }

        private static Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            return response.IsSuccessStatusCode
                ? Task.CompletedTask
                : ThrowProviderExceptionAsync(response, operation);
        }

        private static async Task ThrowProviderExceptionAsync(HttpResponseMessage response, string operation)
        {
            var body = await response.Content.ReadAsStringAsync();
            var shortBody = string.IsNullOrWhiteSpace(body)
                ? "No details from provider."
                : body[..Math.Min(250, body.Length)];

            throw new HttpRequestException(
                $"YouTube {operation} failed with {(int)response.StatusCode} ({response.StatusCode}). {shortBody}",
                null,
                response.StatusCode);
        }
    }
}