import { BiBookContent, BiFoodMenu, BiHeart, BiHome } from "react-icons/bi";
import { NavLink } from "react-router-dom";

function SideBar() {
  return (
    <aside className="side-bar">
      <div className="brand-wrap">
        <div className="brand-icon">
          <BiFoodMenu />
        </div>
        <div>
          <h2 className="brand-title">CookBook Pro</h2>
          <p className="brand-subtitle">Recipe Explorer</p>
        </div>
      </div>

      <nav className="navs">
        <NavLink to="/" className={({ isActive }) => `nav-item ${isActive ? "active" : ""}`}>
          <BiHome />
          <span>Home</span>
        </NavLink>
        <NavLink to="/favorites" className={({ isActive }) => `nav-item ${isActive ? "active" : ""}`}>
          <BiHeart />
          <span>Favorites</span>
        </NavLink>
        <NavLink to="/search-tips" className={({ isActive }) => `nav-item ${isActive ? "active" : ""}`}>
          <BiBookContent />
          <span>Search Tips</span>
        </NavLink>
      </nav>
    </aside>
  );
}

export default SideBar;
