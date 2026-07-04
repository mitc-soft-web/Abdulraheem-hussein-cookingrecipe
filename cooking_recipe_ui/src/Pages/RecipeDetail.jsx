import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getById } from "../Utils/Recipes";

function RecipeDetail() {
  const { id } = useParams();
  const [recipe, setRecipe] = useState(null);
  const [loading, setLoading] = useState(true);
  const fallbackImage =
    "https://images.unsplash.com/photo-1498837167922-ddd27525d352?auto=format&fit=crop&w=1200&q=80";

  const toPlainText = (value) => {
    if (!value) return "";
    if (typeof window !== "undefined" && window.DOMParser) {
      const doc = new DOMParser().parseFromString(value, "text/html");
      return doc.body.textContent || "";
    }
    return value.replace(/<[^>]*>/g, "");
  };

  useEffect(() => {
    const getRecipe = async () => {
      try {
        const data = await getById(id);
        setRecipe(data);
      } catch (error) {
        alert(error.message);
      } finally {
        setLoading(false);
      }
    };

    getRecipe();
  }, [id]);

  if (loading) {
    return (
      <section className="page-container">
        <div className="spinner" aria-label="Loading recipe" />
      </section>
    );
  }

  if (!recipe) {
    return (
      <section className="page-container">
        <div className="empty-state">Recipe not found.</div>
      </section>
    );
  }

  return (
    <section className="page-container">
      <Link to="/" className="back-link">
        Back to recipes
      </Link>

      <article className="detail-card">
        <img
          src={recipe.imageUrl || fallbackImage}
          alt={recipe.title}
          className="detail-image"
          onError={(e) => {
            e.currentTarget.src = fallbackImage;
          }}
        />

        <div className="detail-content">
          <p className="eyebrow">Recipe Details</p>
          <h2>{recipe.title}</h2>

          {recipe.summary && <p className="detail-summary">{toPlainText(recipe.summary)}</p>}

          <div className="detail-meta">
            {recipe.category && <span>{recipe.category}</span>}
            {recipe.readyInMinutes > 0 && <span>{recipe.readyInMinutes} mins</span>}
            {recipe.servings > 0 && <span>{recipe.servings} servings</span>}
          </div>

          {recipe.instructions && (
            <div className="instructions">
              <h3>Instructions</h3>
              <p>{toPlainText(recipe.instructions)}</p>
            </div>
          )}
        </div>
      </article>
    </section>
  );
}

export default RecipeDetail;
