(() => {
  "use strict";
  const buttons = [...document.querySelectorAll("button[data-page]")];
  const links = [...document.querySelectorAll("a[data-page]")];
  const pages = [...document.querySelectorAll("[data-view]")];
  const title = document.getElementById("section-name");
  const labels = {overview:"Обзор",architecture:"Архитектура",dax:"DAX Security",access:"Уровни доступа",roadmap:"Планы"};
  function show(name, changeHash = true) {
    if (!Object.prototype.hasOwnProperty.call(labels, name)) return;
    pages.forEach(p => p.classList.toggle("active", p.dataset.view === name));
    [...buttons, ...links].forEach(el => {
      const selected = el.dataset.page === name;
      el.classList.toggle("active", selected);
      if (el.tagName === "BUTTON") el.setAttribute("aria-current", selected ? "page" : "false");
    });
    if (title) title.textContent = labels[name];
    if (changeHash && location.hash !== "#" + name) history.replaceState(null, "", "#" + name);
    window.scrollTo({top:0,behavior:matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth"});
  }
  [...buttons, ...links].forEach(el => el.addEventListener("click", event => {
    if (el.tagName === "A") event.preventDefault();
    show(el.dataset.page);
  }));
  const initial = location.hash.slice(1);
  show(labels[initial] ? initial : "overview", false);
  addEventListener("hashchange", () => show(location.hash.slice(1), false));
})();