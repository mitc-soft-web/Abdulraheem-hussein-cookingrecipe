import { useEffect, useMemo, useState } from "react";
import { FaSearch, FaTimes } from "react-icons/fa";
import RecipeCard from "../Components/RecipeCard";
import {
  addFavorite,
  getAll,
  getFavorites,
  removeFavorite,
  searchRecipes,
} from "../Utils/Recipes";

const SEARCH_ALIASES = {
  bean: ["beans"],
  beans: ["bean", "ewa"],
  bouillon: ["maggi", "stock cube", "seasoning cube"],
  chicken: ["hen"],
  fish: ["catfish", "mackerel", "stock fish", "dried fish"],
  maggi: ["bouillon", "stock cube", "seasoning cube"],
  pepper: ["ata rodo", "scotch bonnet", "chilli", "chili"],
  plantain: ["dodo"],
  rice: ["jollof", "ofada"],
  tomato: ["tomatoes", "tomato paste", "stew"],
  yam: ["pounded yam"],
};
const DASHBOARD_RECIPE_LIMIT = 50;
const ONLINE_RECIPE_LIMIT = 18;
const ONLINE_SEARCH_DEBOUNCE_MS = 300;
const ONLINE_SEARCH_TIMEOUT_MS = 20000;

const splitSearchTerms = (value) => {
  return value
    .toLowerCase()
    .split(/\s*(?:,|;|\+|&|\band\b)\s*/i)
    .map((item) => item.trim())
    .filter(Boolean);
};

const getRecipeText = (recipe) => {
  const ingredientText = Array.isArray(recipe?.ingredients)
    ? recipe.ingredients
        .map((item) => `${item?.ingredient?.name ?? ""} ${item?.quantity ?? ""}`)
        .join(" ")
    : "";

  return `${recipe?.title ?? ""} ${recipe?.summary ?? ""} ${ingredientText}`.toLowerCase();
};

const getTermVariants = (term) => {
  const variants = new Set([term]);

  if (term.endsWith("es") && term.length > 2) {
    variants.add(term.slice(0, -2));
  }

  if (term.endsWith("s") && term.length > 1) {
    variants.add(term.slice(0, -1));
  }

  (SEARCH_ALIASES[term] || []).forEach((alias) => variants.add(alias));
  return Array.from(variants);
};

const textMatchesTerm = (text, term) => {
  if (getTermVariants(term).some((variant) => text.includes(variant))) {
    return true;
  }

  const words = term.split(/\s+/).filter(Boolean);
  return words.length > 1 && words.every((word) => getTermVariants(word).some((variant) => text.includes(variant)));
};

const filterRecipesLocally = (source, query) => {
  const terms = splitSearchTerms(query);
  if (terms.length === 0) return source;

  return source.filter((recipe) => {
    const text = getRecipeText(recipe);
    return terms.every((term) => textMatchesTerm(text, term));
  });
};

function Home() {
  const [allRecipes, setAllRecipes] = useState([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  const [onlineResult, setOnlineResult] = useState({
    query: "",
    recipes: [],
    loading: false,
    error: "",
  });
  const [favoriteIds, setFavoriteIds] = useState(new Set());

  useEffect(() => {
    const loadInitialData = async () => {
      setLoading(true);
      setError("");

      try {
        const [initialRecipes, favorites] = await Promise.all([getAll(), getFavorites()]);
        const safeInitial = Array.isArray(initialRecipes) ? initialRecipes : [];
        const favoriteSet = new Set((Array.isArray(favorites) ? favorites : []).map((item) => item.id));

        setAllRecipes(safeInitial);
        setFavoriteIds(favoriteSet);
      } catch (err) {
        setError(err.message || "Unable to load recipes right now.");
      } finally {
        setLoading(false);
      }
    };

    loadInitialData();
  }, []);

  const searchQuery = search.trim();

  const localRecipes = useMemo(() => {
    if (!searchQuery) {
      return allRecipes.slice(0, DASHBOARD_RECIPE_LIMIT);
    }

    return filterRecipesLocally(allRecipes, searchQuery);
  }, [allRecipes, searchQuery]);

  useEffect(() => {
    if (!searchQuery || localRecipes.length > 0) {
      return;
    }

    let ignore = false;
    const controller = new AbortController();

    const timeoutId = setTimeout(async () => {
      setOnlineResult({
        query: searchQuery,
        recipes: [],
        loading: true,
        error: "",
      });

      const abortId = setTimeout(() => controller.abort(), ONLINE_SEARCH_TIMEOUT_MS);

      try {
        const data = await searchRecipes(searchQuery, ONLINE_RECIPE_LIMIT, { signal: controller.signal });
        if (ignore) return;
        setOnlineResult({
          query: searchQuery,
          recipes: Array.isArray(data) ? data : [],
          loading: false,
          error: "",
        });
      } catch (err) {
        if (ignore) return;
        setOnlineResult({
          query: searchQuery,
          recipes: [],
          loading: false,
          error:
            err.name === "AbortError"
              ? "Online recipe search took too long. Try again."
              : err.message || "Unable to search online recipes right now.",
        });
      } finally {
        clearTimeout(abortId);
      }
    }, ONLINE_SEARCH_DEBOUNCE_MS);

    return () => {
      ignore = true;
      controller.abort();
      clearTimeout(timeoutId);
    };
  }, [searchQuery, localRecipes.length]);

  const usingOnlineSearch = Boolean(searchQuery && localRecipes.length === 0);
  const activeOnlineResult = usingOnlineSearch && onlineResult.query === searchQuery ? onlineResult : null;
  const onlineLoading = usingOnlineSearch && (!activeOnlineResult || activeOnlineResult.loading);
  const onlineError = activeOnlineResult?.error || "";
  const visibleRecipes = usingOnlineSearch ? activeOnlineResult?.recipes || [] : localRecipes;

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

    try {
      if (isCurrentlyFavorite) {
        await removeFavorite(recipeId);
      } else {
        await addFavorite(recipeId);
      }
    } catch (err) {
      setFavoriteIds((prev) => {
        const next = new Set(prev);
        if (isCurrentlyFavorite) {
          next.add(recipeId);
        } else {
          next.delete(recipeId);
        }
        return next;
      });
      setError(err.message || "Unable to update favorites right now.");
    }
  };

  return (
    <section className="page-container">
      <div className="home-toolbar">
        <label className="search-bar" htmlFor="recipe-search">
          <FaSearch className="search-icon" />
          <input
            id="recipe-search"
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Try: rice and beans, tomato + onion, beans, plantain"
          />
          {search && (
            <button
              className="clear-search"
              type="button"
              onClick={() => setSearch("")}
              aria-label="Clear search"
            >
              <FaTimes />
            </button>
          )}
        </label>
      </div>

      {loading && <div className="spinner" aria-label="Loading recipes" />}

      {!loading && (
        <>
          <div className="section-header">
            <div>
              <h2>Recipe Search</h2>
              <p>
                {search
                  ? localRecipes.length > 0
                    ? `${visibleRecipes.length} local result(s) for "${search}"`
                    : onlineLoading
                    ? `No local recipes for "${search}". Searching online...`
                    : `${visibleRecipes.length} online result(s) for "${search}"`
                  : `${visibleRecipes.length} recipe(s) shown from ${allRecipes.length} available. Multi-ingredient search matches all entered ingredients.`}
              </p>
            </div>
          </div>

          {error ? (
            <div className="empty-state">{error}</div>
          ) : onlineLoading ? (
            <div className="empty-state">Searching online recipes for "{search}"...</div>
          ) : onlineError ? (
            <div className="empty-state">{onlineError}</div>
          ) : visibleRecipes.length === 0 ? (
            <div className="empty-state">
              {search ? `No recipes match "${search}".` : "No recipes available right now."}
            </div>
          ) : (
            <div className="recipe-grid">
              {visibleRecipes.map((item) => (
                <RecipeCard
                  item={item}
                  key={item.id}
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

export default Home;
