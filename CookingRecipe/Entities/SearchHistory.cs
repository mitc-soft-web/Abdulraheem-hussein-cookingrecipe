using System.Text.Json.Serialization;
namespace CookingRecipe.Entities
{
    public class SearchHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // DeviceId to associate anonymous users (cookie or client-generated id)
        public string DeviceId { get; set; } = string.Empty;

        public string SearchText { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime SearchDate { get; set; } = DateTime.UtcNow;
        public string JsonResult { get; set; } = string.Empty;
    }
}
