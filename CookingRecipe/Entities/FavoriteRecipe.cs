namespace CookingRecipe.Entities
{
    public class FavoriteRecipe
    {
        public string DeviceId { get; set; } = string.Empty;
        public int RecipeId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
