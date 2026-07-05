namespace CookingRecipe.Dtos
{
    public class YouTubeVideoDto
    {
        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string ChannelTitle { get; set; } = string.Empty;
        public DateTimeOffset? PublishedAt { get; set; }
        public string WatchUrl { get; set; } = string.Empty;
    }
}