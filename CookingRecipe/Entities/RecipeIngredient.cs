namespace CookingRecipe.Entities
{
    public class RecipeIngredient
    {
        public int RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        public int IngredientId { get; set; }
        public Ingredient? Ingredient { get; set; }

        // Examples: "2 cups", "1 tbsp", "3"
        public string Quantity { get; set; } = string.Empty;
    }
}
