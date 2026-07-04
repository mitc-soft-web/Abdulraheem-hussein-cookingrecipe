import { FaMoon, FaSun } from "react-icons/fa";

function TopBar({ isDarkMode, onToggleDarkMode }) {
  return (
    <header className="topbar">
      <div>
        <p className="eyebrow">Smart Kitchen</p>
        <h1 className="topbar-title">Find recipes you'll actually cook</h1>
      </div>
      <div className="topbar-actions">
        <button className="theme-toggle" type="button" onClick={onToggleDarkMode}>
          {isDarkMode ? <FaSun /> : <FaMoon />}
          <span>{isDarkMode ? "Light" : "Dark"} mode</span>
        </button>
      </div>
    </header>
  );
}

export default TopBar;
