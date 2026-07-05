using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CookingRecipe.Conntext
{
    public class CookingRecipeContextFactory : IDesignTimeDbContextFactory<CookingRecipeContext>
    {
        public CookingRecipeContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<CookingRecipeContext>();
            optionsBuilder.UseSqlite("Data Source=cookingrecipe.db");
            return new CookingRecipeContext(optionsBuilder.Options);
        }
    }
}
