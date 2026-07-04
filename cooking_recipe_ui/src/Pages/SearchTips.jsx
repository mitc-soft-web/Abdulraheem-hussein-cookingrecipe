function SearchTips() {
  return (
    <section className="page-container">
      <div className="section-header">
        <div>
          <h2>Search Tips</h2>
          <p>Use multiple ingredients to get strict match suggestions.</p>
        </div>
      </div>

      <div className="tips-grid">
        <div className="tip-card">
          <h3>Use Commas</h3>
          <p>Example: <strong>rice, beans</strong></p>
        </div>
        <div className="tip-card">
          <h3>Use "and"</h3>
          <p>Example: <strong>rice and beans</strong></p>
        </div>
        <div className="tip-card">
          <h3>Strict Ingredient Match</h3>
          <p>Multi-ingredient searches now return recipes containing all requested ingredients.</p>
        </div>
      </div>
    </section>
  );
}

export default SearchTips;
