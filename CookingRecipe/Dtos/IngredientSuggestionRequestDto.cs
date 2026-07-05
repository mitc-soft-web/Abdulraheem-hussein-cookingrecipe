namespace CookingRecipe.Dtos
{
    public class IngredientSuggestionRequestDto
    {
        public List<string> Ingredients { get; set; } = new();
        public int Max { get; set; } = 10;
    }
}
