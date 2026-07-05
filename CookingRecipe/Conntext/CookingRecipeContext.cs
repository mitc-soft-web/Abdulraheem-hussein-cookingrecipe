using Microsoft.EntityFrameworkCore;
using CookingRecipe.Entities;

namespace CookingRecipe.Conntext
{
    public class CookingRecipeContext : DbContext
    {
        public CookingRecipeContext(DbContextOptions<CookingRecipeContext> options)
            : base(options)
        {
        }

        public DbSet<SearchHistory> SearchHistories => Set<SearchHistory>();
        public DbSet<FavoriteRecipe> FavoriteRecipes => Set<FavoriteRecipe>();
        public DbSet<Recipe> Recipes { get; set; } = null!;
        public DbSet<Ingredient> Ingredients { get; set; } = null!;
        public DbSet<RecipeIngredient> RecipeIngredients { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Recipe>(entity =>
            {
                entity.HasKey(r => r.Id);

                entity.Property(r => r.Title)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(r => r.Category)
                      .HasMaxLength(100);

                entity.Property(r => r.ImageUrl)
                      .HasMaxLength(500);

                entity.OwnsOne(r => r.Nutrition, nutrition =>
                {
                    nutrition.Property(n => n.Calories).HasColumnName("Calories");
                    nutrition.Property(n => n.FatGrams).HasColumnName("FatGrams");
                    nutrition.Property(n => n.CarbsGrams).HasColumnName("CarbsGrams");
                    nutrition.Property(n => n.ProteinGrams).HasColumnName("ProteinGrams");
                });

             
                entity.HasMany(r => r.Ingredients)
                      .WithOne(ri => ri.Recipe)
                      .HasForeignKey(ri => ri.RecipeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.HasKey(i => i.Id);

                entity.Property(i => i.Name)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.Property(i => i.Notes)
                      .HasMaxLength(250);
            });

            modelBuilder.Entity<RecipeIngredient>(entity =>
            {
                // Composite PK for join entity
                entity.HasKey(ri => new { ri.RecipeId, ri.IngredientId });

                entity.Property(ri => ri.Quantity)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.HasOne(ri => ri.Recipe)
                      .WithMany(r => r.Ingredients)
                      .HasForeignKey(ri => ri.RecipeId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ri => ri.Ingredient)
                      .WithMany(i => i.Recipes)
                      .HasForeignKey(ri => ri.IngredientId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SearchHistory>(entity =>
            {
                entity.HasKey(s => s.Id);

                entity.Property(s => s.SearchText)
                      .HasMaxLength(500);

                entity.Property(s => s.Category)
                      .HasMaxLength(100);

                entity.Property(s => s.JsonResult);

                entity.Property(s => s.SearchDate)
                      .IsRequired();
            });

            modelBuilder.Entity<FavoriteRecipe>(entity =>
            {
                entity.HasKey(f => new { f.DeviceId, f.RecipeId });

                entity.Property(f => f.DeviceId)
                      .HasMaxLength(100);

                entity.Property(f => f.CreatedAt)
                      .IsRequired();

                entity.HasIndex(f => f.DeviceId);
            });
        }
    }
}
