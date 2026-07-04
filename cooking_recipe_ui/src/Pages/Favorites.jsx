import { useEffect, useMemo, useState } from "react";
import { FaHeart, FaSearch, FaTimes } from "react-icons/fa";
import RecipeCard from "../Components/RecipeCard";
import { addFavorite, getFavorites, removeFavorite } from "../Utils/Recipes";

function Favorites() {
  const [favorites, setFavorites] = useState([]);
  const [favoriteIds, setFavoriteIds] = useState(new Set());
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const loadFavorites = async () => {
      setLoading(true);
      setError("");
      try {
        const data = await getFavorites();
        const safeData = Array.isArray(data) ? data : [];
        setFavorites(safeData);
        setFavoriteIds(new Set(safeData.map((item) => item.id)));
      } catch (err) {
        setError(err.message || "Unable to load favorites.");
      } finally {
        setLoading(false);
      }
    };

    loadFavorites();
  }, []);

  const visibleFavorites = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return favorites;
    return favorites.filter((item) => {
      const title = item?.title?.toLowerCase() ?? "";
      return title.includes(query);
    });
  }, [favorites, search]);

  const toggleFavorite = async (recipeId) => {
    const isCurrentlyFavorite = favoriteIds.has(recipeId);

    setFavoriteIds((prev) => {
      const next = new Set(prev);
      if (isCurrentlyFavorite) {
        next.delete(recipeId);
      } else {
        next.add(recipeId);
      }
      return next;
    });

    setFavorites((prev) => prev.filter((item) => item.id !== recipeId));

    try {
      if (isCurrentlyFavorite) {
        await removeFavorite(recipeId);
      } else {
        await addFavorite(recipeId);
      }
    } catch (err) {
      setError(err.message || "Unable to update favorite.");
    }
  };

  return (
    <section className="page-container">
      <div className="section-header">
        <div>
          <h2>Your Favorites</h2>
          <p>{visibleFavorites.length} recipe(s) saved</p>
        </div>
      </div>

      <div className="home-toolbar">
        <label className="search-bar" htmlFor="favorite-search">
          <FaSearch className="search-icon" />
          <input
            id="favorite-search"
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search your favorites"
          />
          {search && (
            <button type="button" className="clear-search" onClick={() => setSearch("")}
              aria-label="Clear search">
              <FaTimes />
            </button>
          )}
        </label>
      </div>

      {loading && <div className="spinner" aria-label="Loading favorites" />}

      {!loading && (
        <>
          {error ? (
            <div className="empty-state">{error}</div>
          ) : visibleFavorites.length === 0 ? (
            <div className="empty-state">
              <FaHeart style={{ marginBottom: "10px" }} />
              <div>No favorites yet. Add recipes with the heart icon.</div>
            </div>
          ) : (
            <div className="recipe-grid">
              {visibleFavorites.map((item) => (
                <RecipeCard
                  key={item.id}
                  item={item}
                  isFavorite={favoriteIds.has(item.id)}
                  onToggleFavorite={toggleFavorite}
                />
              ))}
            </div>
          )}
        </>
      )}
    </section>
  );
}

export default Favorites;
