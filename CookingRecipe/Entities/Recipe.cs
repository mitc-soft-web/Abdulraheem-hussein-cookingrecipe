namespace CookingRecipe.Entities
{
    public class Recipe
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
       
        public string Summary { get; set; } = string.Empty;

        
        public List<RecipeIngredient> Ingredients { get; set; } = new();

        // Full instructions / steps
        public string Instructions { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;

       
        public int ReadyInMinutes { get; set; }
        public int Servings { get; set; }
        public string SourceUrl { get; set; } = string.Empty;

        public NutritionInfo Nutrition { get; set; } = new();
    }
}
