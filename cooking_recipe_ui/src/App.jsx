import { BrowserRouter, Route, Routes } from "react-router-dom";
import Home from "./Pages/Home";
import Layout from "./Layout/Layout";
import RecipeDetail from "./Pages/RecipeDetail";
import Favorites from "./Pages/Favorites";
import SearchTips from "./Pages/SearchTips";

function App() {
  return (
    <>
      <BrowserRouter>

        <Routes>

          <Route element={<Layout />}>
            <Route path="/" element={<Home />}/>
            <Route path="/favorites" element={<Favorites />}/>
            <Route path="/search-tips" element={<SearchTips />}/>
            <Route path="/recipe/:id" element={<RecipeDetail />}/>
          </Route>

        </Routes>
      </BrowserRouter>
    </>
  )
}

export default App
