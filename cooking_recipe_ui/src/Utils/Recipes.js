const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "http://localhost:5209";

const handleResponse = async (response) => {
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || "Request failed.");
  }

  if (response.status === 204) {
    return null;
  }

  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes("application/json")) {
    const text = await response.text();
    return text ? { message: text } : null;
  }

  const text = await response.text();
  if (!text) return null;

  return JSON.parse(text);
};

export const searchRecipes = async (query, max = 24, options = {}) => {
  const params = new URLSearchParams({
    ingredients: query,
    max: String(max),
  });
  const response = await fetch(`${API_BASE_URL}/api/recipes/search?${params.toString()}`, {
    credentials: "include",
    signal: options.signal,
  });
  return handleResponse(response);
};

export const getAll = async () => {
  const response = await fetch(`${API_BASE_URL}/api/recipes`, {
    credentials: "include",
  });
  return handleResponse(response);
};

export const getById = async (id) => {
  const response = await fetch(`${API_BASE_URL}/api/recipes/${id}`, {
    credentials: "include",
  });
  return handleResponse(response);
};

export const getFavorites = async () => {
  const response = await fetch(`${API_BASE_URL}/api/recipes/favorites`, {
    credentials: "include",
  });
  return handleResponse(response);
};

export const addFavorite = async (id) => {
  const response = await fetch(`${API_BASE_URL}/api/recipes/${id}/favorite`, {
    method: "POST",
    credentials: "include",
  });
  return handleResponse(response);
};

export const removeFavorite = async (id) => {
  const response = await fetch(`${API_BASE_URL}/api/recipes/${id}/favorite`, {
    method: "DELETE",
    credentials: "include",
  });
  return handleResponse(response);
};
