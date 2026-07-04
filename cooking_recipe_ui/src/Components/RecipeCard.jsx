import { FaHeart, FaRegHeart } from "react-icons/fa";
import { Link } from "react-router-dom";

function RecipeCard({ item, isFavorite, onToggleFavorite }) {
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

  const truncateWords = (str, count) => {
    const plainText = toPlainText(str);
    if (!plainText) return "No summary available.";
    const words = plainText.split(" ");
    if (words.length <= count) return plainText;
    return `${words.slice(0, count).join(" ")}...`;
  };

  return (
    <Link to={`/recipe/${item.id}`} className="recipe-card">
      <button
        type="button"
        className={`favorite-btn ${isFavorite ? "active" : ""}`}
        onClick={(e) => {
          e.preventDefault();
          e.stopPropagation();
          onToggleFavorite?.(item.id);
        }}
        aria-label={isFavorite ? "Remove from favorites" : "Add to favorites"}
      >
        {isFavorite ? <FaHeart /> : <FaRegHeart />}
      </button>

      <div className="recipe-image-wrap">
        <img
          src={item?.imageUrl || fallbackImage}
          alt={item?.title || "Recipe"}
          onError={(e) => {
            e.currentTarget.src = fallbackImage;
          }}
        />
      </div>
      <div className="recipe-card-down">
        <h3>{item?.title || "Untitled Recipe"}</h3>
        <p>{truncateWords(item?.summary, 14)}</p>
        <span className="card-link">View details</span>
      </div>
    </Link>
  );
}

export default RecipeCard;
