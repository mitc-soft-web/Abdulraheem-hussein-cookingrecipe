namespace CookingRecipe.Entities
{
    public class Ingredient
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        // Optional normalized description (e.g., "chopped", "diced")
        public string? Notes { get; set; }

        // navigation
        public List<RecipeIngredient>? Recipes { get; set; }
    }
}
