// Runs before the page is drawn: puts a theme chosen by hand on the page. Kept in step with ThemeService (src/app/theme).
(function () {
  try {
    var choice = localStorage.getItem('oep.theme');
    if (choice === 'light' || choice === 'dark') {
      document.documentElement.setAttribute('data-theme', choice);
    }
  } catch (e) {
    // Storage is blocked: the page follows the system.
  }
})();
