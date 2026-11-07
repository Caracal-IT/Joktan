// The Joktan website: sign in, the work items waiting for you, and the page each is filled in on, laid out as
// the Joktan app is: a sidebar menu, a top bar, a page header, tiles and cards.
// Everything it shows comes from the JSON calls under /api, which other programs can make as well.
// Text from workflows is only ever set as text, never as HTML.
"use strict";

let S = {};
let me = null;
let pages = null;
let refreshTimer = null;
let search = "";
let onSearch = null;

// The app's icons (Material Design paths), drawn as SVG.
const ICONS = {
  menu: "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z",
  search: "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z",
  clock: "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z",
  timer: "M12,20A7,7 0 0,1 5,13A7,7 0 0,1 12,6A7,7 0 0,1 19,13A7,7 0 0,1 12,20M19.03,7.39L20.45,5.97C20,5.46 19.55,5 19.04,4.56L17.62,6C16.07,4.74 14.12,4 12,4A9,9 0 0,0 3,13A9,9 0 0,0 12,22C17,22 21,17.97 21,13C21,10.88 20.26,8.93 19.03,7.39M11,14H13V8H11M15,1H9V3H15V1Z",
  workflow: "M9,2V8H11V11H5C3.89,11 3,11.89 3,13V16H1V22H7V16H5V13H11V16H9V22H15V16H13V13H19V16H17V22H23V16H21V13C21,11.89 20.11,11 19,11H13V8H15V2H9Z",
  people: "M12,5.5A3.5,3.5 0 0,1 15.5,9A3.5,3.5 0 0,1 12,12.5A3.5,3.5 0 0,1 8.5,9A3.5,3.5 0 0,1 12,5.5M5,8C5.56,8 6.08,8.15 6.53,8.42C6.38,9.85 6.8,11.27 7.66,12.38C7.16,13.34 6.16,14 5,14A3,3 0 0,1 2,11A3,3 0 0,1 5,8M19,8A3,3 0 0,1 22,11A3,3 0 0,1 19,14C17.84,14 16.84,13.34 16.34,12.38C17.2,11.27 17.62,9.85 17.47,8.42C17.92,8.15 18.44,8 19,8M5.5,18.25C5.5,16.18 8.41,14.5 12,14.5C15.59,14.5 18.5,16.18 18.5,18.25V20H5.5V18.25M0,20V18.5C0,17.11 1.89,15.94 4.45,15.6C3.86,16.28 3.5,17.22 3.5,18.25V20H0M24,20H20.5V18.25C20.5,17.22 20.14,16.28 19.55,15.6C22.11,15.94 24,17.11 24,18.5V20Z",
  page: "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z",
  eye: "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z",
  inbox: "M19,15H15A3,3 0 0,1 12,18A3,3 0 0,1 9,15H5V5H19M19,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3Z",
  account: "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
  alert: "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z",
  logout: "M16,17V14H9V10H16V7L21,12L16,17M14,2A2,2 0 0,1 16,4V6H14V4H5V20H14V18H16V20A2,2 0 0,1 14,22H5A2,2 0 0,1 3,20V4A2,2 0 0,1 5,2H14Z",
  refresh: "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z",
  back: "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z",
  chevron: "M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z",
  check: "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z",
  calendar: "M19,19H5V8H19M16,1V3H8V1H6V3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3H18V1M17,12H12V17H17V12Z",
  chevronLeft: "M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z",
};

function icon(name, cls) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("class", "icon" + (cls ? " " + cls : ""));
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", ICONS[name]);
  svg.append(path);
  return svg;
}

function el(tag, attrs, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs || {})) {
    if (value === undefined || value === null || value === false) continue;
    if (key === "class") node.className = value;
    else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
    else if (key in node && typeof value !== "string") node[key] = value;
    else node.setAttribute(key, value === true ? "" : value);
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

function format(text, ...values) {
  return (text || "").replace(/\{(\d+)\}/g, (_, i) => values[Number(i)] ?? "");
}

async function api(method, url, body) {
  const response = await fetch(url, {
    method,
    credentials: "same-origin",
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data = null;
  try { data = await response.json(); } catch { data = null; }
  if (response.status === 401 && url !== "/api/login") {
    me = null;
    pages = null;
    go("/login?next=" + encodeURIComponent(location.pathname));
    throw new Error(S.SignInAgain);
  }
  if (!response.ok) {
    const error = new Error((data && data.error) || format(S.Failed, response.status));
    error.status = response.status;
    error.data = data;
    throw error;
  }
  return data;
}

function go(path, replace) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  route();
}

function link(path, attrs, ...children) {
  return el("a", { ...attrs, href: path, onclick: e => { if (!e.metaKey && !e.ctrlKey) { e.preventDefault(); go(path); } } }, ...children);
}

function when(iso) {
  if (!iso) return "";
  const date = new Date(iso);
  return isNaN(date) ? iso : date.toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

function ago(iso) {
  const date = new Date(iso);
  if (isNaN(date)) return "";
  const minutes = Math.round((Date.now() - date) / 60000);
  if (minutes < 1) return S.JustNow;
  if (minutes < 60) return format(S.MinutesAgo, minutes);
  const hours = Math.round(minutes / 60);
  if (hours < 48) return format(S.HoursAgo, hours);
  return format(S.DaysAgo, Math.round(hours / 24));
}

function initials(name) {
  return (name || "?").split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0].toUpperCase()).join("");
}

function assignee(to) {
  if (!to || !to.id) return S.Nobody;
  return format(to.kind === "role" ? S.TheRole : S.TheUser, to.name || to.id);
}

// The frame: the sidebar menu, the top bar and the page under them, as in the app.

function frame(active, header, ...content) {
  clearInterval(refreshTimer);
  refreshTimer = null;
  onSearch = null;
  const app = document.getElementById("app");
  const nav = el("nav", { class: "menu", "aria-label": S.Menu });
  const item = (path, name, text, count) => link(path, { class: "menu-item" + (active === path ? " active" : ""), "aria-current": active === path ? "page" : null },
    icon(name), el("span", {}, text), count === undefined ? null : el("span", { class: "count", "data-count": path }, String(count)));
  nav.append(el("p", { class: "menu-caption" }, S.MainMenu));
  if (me && me.administrator) {
    nav.append(item("/", "inbox", S.AllItems), item("/mine", "account", S.Mine));
  } else {
    nav.append(item("/", "inbox", S.MyItems));
  }
  if (me && me.administrator && pages && pages.length) {
    nav.append(el("p", { class: "menu-caption" }, S.Pages));
    for (const page of pages)
      nav.append(item("/preview/" + encodeURIComponent(page.id), "page", page.name));
  }

  const sidebar = el("aside", { class: "sidebar", id: "sidebar" },
    link("/", { class: "brand" }, "joktan"),
    nav,
    me ? el("div", { class: "user" },
      el("span", { class: "avatar", "aria-hidden": "true" }, initials(me.name || me.username)),
      el("div", { class: "user-text" }, el("strong", {}, me.name || me.username), el("span", {}, me.role + (me.administrator && me.role !== S.Administrator ? " · " + S.Administrator : ""))),
      el("button", { type: "button", class: "icon-button", title: S.SignOut, "aria-label": S.SignOut, onclick: signOut }, icon("logout"))) : null);

  const searchBox = el("input", {
    type: "search", class: "search-input", placeholder: S.Search, "aria-label": S.Search, value: search,
    oninput: e => { search = e.target.value; if (onSearch) onSearch(); },
  });
  const topbar = el("header", { class: "topbar" },
    el("button", { type: "button", class: "icon-button menu-button", "aria-label": S.Menu, onclick: () => document.body.classList.toggle("menu-open") }, icon("menu")),
    active === "/" || active === "/mine" ? el("label", { class: "search" }, searchBox, icon("search")) : null,
    el("span", { class: "spacer" }),
    el("button", { type: "button", class: "icon-button", title: S.Refresh, "aria-label": S.Refresh, onclick: () => route() }, icon("refresh")));

  document.body.classList.remove("menu-open", "signed-out");
  app.replaceChildren(sidebar, el("div", { class: "content" }, topbar, header, el("main", { id: "main", class: "page" }, ...content)));
}

function pageHeader(title, subtitle, ...actions) {
  return el("div", { class: "page-header" },
    el("div", {}, el("h1", { class: "page-title" }, title), subtitle ? el("p", { class: "page-subtitle" }, subtitle) : null),
    actions.length ? el("div", { class: "page-actions" }, ...actions) : null);
}

function tile(name, value, label, note) {
  return el("div", { class: "card tile" },
    el("span", { class: "tile-icon" }, icon(name)),
    el("div", {}, el("div", { class: "tile-value" }, value), el("div", { class: "tile-label" }, label), note ? el("div", { class: "tile-note" }, note) : null));
}

async function signOut() {
  try { await api("POST", "/api/logout", {}); } catch { /* signed out anyway */ }
  me = null;
  pages = null;
  go("/login");
}

// Sign in

function loginView() {
  clearInterval(refreshTimer);
  document.body.classList.add("signed-out");
  const error = el("p", { class: "error", role: "alert", hidden: true });
  const username = el("input", { id: "username", name: "username", autocomplete: "username", required: true, autofocus: true, "aria-describedby": "username-error" });
  const password = el("input", { id: "password", name: "password", type: "password", autocomplete: "current-password", required: true, "aria-describedby": "password-error" });
  const button = el("button", { type: "submit", class: "primary wide" }, S.SignIn);
  const form = el("form", { class: "card login-form", novalidate: true },
    el("h1", {}, S.SignIn),
    el("p", { class: "muted" }, S.SignInTip),
    el("div", { class: "field" }, el("label", { for: "username" }, S.Username), username, el("p", { class: "error field-error", id: "username-error", hidden: true })),
    el("div", { class: "field" }, el("label", { for: "password" }, S.Password), password, el("p", { class: "error field-error", id: "password-error", hidden: true })),
    error,
    button);
  const checks = [
    [username, () => (username.value.trim() ? null : S.MustBeFilledIn)],
    [password, () => (password.value ? null : S.MustBeFilledIn)],
  ];
  for (const [input, check] of checks) recheck(input, check);
  form.addEventListener("submit", async e => {
    e.preventDefault();
    error.hidden = true;
    if (!checkFields(checks)) {
      error.textContent = S.EnterBoth;
      error.hidden = false;
      return;
    }
    button.disabled = true;
    button.textContent = S.SigningIn;
    try {
      const result = await api("POST", "/api/login", { username: username.value.trim(), password: password.value });
      me = result.user;
      const next = new URLSearchParams(location.search).get("next");
      go(next && next.startsWith("/") && !next.startsWith("//") && next !== "/login" ? next : "/", true);
    } catch (err) {
      error.textContent = err.message;
      error.hidden = false;
      password.value = "";
      password.focus();
    } finally {
      button.disabled = false;
      button.textContent = S.SignIn;
    }
  });
  document.getElementById("app").replaceChildren(el("div", { class: "login" },
    el("section", { class: "login-brand" },
      el("img", { src: "/assets/icon.png", alt: "", class: "login-logo" }),
      el("div", { class: "login-wordmark" }, "joktan"),
      el("h2", {}, S.LoginHeadline),
      el("p", {}, S.LoginTagline)),
    el("section", { class: "login-side" }, form)));
  username.focus();
}

// The work items

async function listView(mineOnly) {
  const path = mineOnly ? "/mine" : "/";
  const tiles = el("div", { class: "tiles" });
  const table = el("div", { class: "card" }, el("p", { class: "muted" }, S.Loading));
  const title = () => me && me.administrator ? (mineOnly ? S.Mine : S.AllItems) : S.MyItems;
  const subtitle = () => me && me.administrator ? (mineOnly ? S.MineTip : S.AllItemsTip) : S.MyItemsTip;
  frame(path, pageHeader(title(), subtitle()), tiles, table);
  let items = [];

  function draw() {
    const words = search.trim().toLowerCase();
    const shown = words ? items.filter(i => [i.title, i.message, i.workflow, "#" + i.serial, assignee(i.assignedTo)].join(" ").toLowerCase().includes(words)) : items;
    const rows = shown.map(item => link("/items/" + encodeURIComponent(item.id), { class: "table-row item-row" },
      el("div", { class: "cell main" },
        el("strong", {}, item.title || item.message || item.workflow),
        el("span", { class: "muted small" }, icon("workflow", "inline"), " ", item.workflow, " #", item.serial)),
      el("div", { class: "cell" }, el("span", { class: "label" }, S.ColFor), item.assignedToYou && !(me && me.administrator) ? assignee(item.assignedTo) : item.assignedToYou ? S.AssignedToYou : assignee(item.assignedTo)),
      el("div", { class: "cell" }, el("span", { class: "label" }, S.ColAsked), ago(item.asked)),
      el("div", { class: "cell" }, el("span", { class: "label" }, S.ColAnswerBy), item.until ? when(item.until) : S.None),
      el("div", { class: "cell status" },
        item.escalated ? el("span", { class: "badge warn" }, S.Escalated) : null,
        item.paused ? el("span", { class: "badge" }, S.Paused) : null,
        !item.escalated && !item.paused ? el("span", { class: "badge accent" }, S.StatusWaiting) : null),
      icon("chevron", "row-chevron")));
    table.replaceChildren(
      el("div", { class: "card-head" },
        el("div", {}, el("h2", { class: "card-title" }, S.WaitingForAnAnswer), el("p", { class: "card-subtitle" }, format(S.UpdatedAt, new Date().toLocaleTimeString([], { timeStyle: "short" }))))),
      items.length === 0
        ? el("div", { class: "empty" }, el("span", { class: "tile-icon big" }, icon("check")), el("p", {}, S.NoItems), el("p", { class: "muted" }, S.NoItemsTip))
        : shown.length === 0
          ? el("div", { class: "empty" }, el("p", { class: "muted" }, S.NoMatch))
          : el("div", { class: "table" },
            el("div", { class: "table-row table-head" }, el("div", { class: "cell main" }, S.ColItem), el("div", { class: "cell" }, S.ColFor),
              el("div", { class: "cell" }, S.ColAsked), el("div", { class: "cell" }, S.ColAnswerBy), el("div", { class: "cell status" }, S.ColStatus), el("span", { class: "row-chevron" })),
            ...rows));
  }

  async function load() {
    let data;
    try {
      data = await api("GET", "/api/items");
    } catch (err) {
      if (err.status) table.replaceChildren(el("p", { class: "error" }, err.message));
      return;
    }
    me = data.user;
    items = data.all && mineOnly ? data.items.filter(i => i.assignedToYou) : data.items;
    const soon = items.filter(i => i.until && new Date(i.until) - Date.now() < 24 * 3600 * 1000).length;
    const oldest = items.reduce((a, i) => (!a || new Date(i.asked) < new Date(a) ? i.asked : a), null);
    tiles.replaceChildren(
      tile("inbox", String(items.length), S.Waiting, data.all && !mineOnly ? format(S.OfWhichYours, data.items.filter(i => i.assignedToYou).length) : null),
      tile("alert", String(items.filter(i => i.escalated).length), S.Escalated),
      tile("timer", String(soon), S.DueSoon),
      tile("clock", oldest ? ago(oldest) : S.None, S.Oldest));
    for (const badge of document.querySelectorAll(".count")) badge.remove();
    const counts = { "/": data.items.length, "/mine": data.items.filter(i => i.assignedToYou).length };
    for (const a of document.querySelectorAll(".menu-item")) {
      const where = a.getAttribute("href");
      if (where in counts && counts[where] > 0) a.append(el("span", { class: "count" }, String(counts[where])));
    }
    draw();
  }

  onSearch = draw;
  await load();
  refreshTimer = setInterval(load, 15000);
}

// Checking what is typed, as the server checks it again: a problem is shown under its field, the first one is
// focused, and each goes as soon as its field is put right.

function validDate(text) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
  const d = m && new Date(+m[1], +m[2] - 1, +m[3]);
  return !!d && d.getFullYear() === +m[1] && d.getMonth() === +m[2] - 1 && d.getDate() === +m[3];
}

// What is wrong with a page's field, with the server's rules; null when nothing is.
function problemOf(part, input) {
  if (part.type === "CheckBox") return part.required && !input.checked ? S.MustBeTicked : null;
  // A number box gives no value at all for what is not a number, so that is asked of the box itself.
  if (part.type === "Number" && input.validity && input.validity.badInput) return S.GiveANumber;
  const text = (input.value || "").trim();
  if (!text) return part.required ? S.MustBeFilledIn : null;
  switch (part.type) {
    case "Number": return /^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(text) ? null : S.GiveANumber;
    case "Date": return validDate(text) ? null : S.GiveADate;
    case "DropDown": return (part.options || []).length && !part.options.includes(input.value) ? S.ChooseAnOption : null;
    default: return null;
  }
}

// Shows a field's problem under it, or takes it away when there is none.
function showProblem(input, problem) {
  const box = input.closest(".field");
  const p = box && box.querySelector(".field-error");
  if (!p) return;
  p.textContent = problem || "";
  p.hidden = !problem;
  box.classList.toggle("invalid", !!problem);
  if (problem) input.setAttribute("aria-invalid", "true");
  else input.removeAttribute("aria-invalid");
}

// Checks fields again as they change, once a problem has been shown on them.
function recheck(input, check) {
  const again = () => { if (input.closest(".field")?.classList.contains("invalid")) showProblem(input, check()); };
  input.addEventListener("input", again);
  input.addEventListener("change", again);
}

// Checks each field; shows what is wrong, focuses the first, and says whether all is well.
function checkFields(checks) {
  let first = null;
  for (const [input, check] of checks) {
    const problem = check();
    showProblem(input, problem);
    if (problem && !first) first = input;
  }
  if (first) first.focus();
  return !first;
}

// A page, to fill in or to preview

function field(item, value) {
  const id = "f-" + item.name;
  const label = item.label || item.name;
  const help = item.help ? el("p", { class: "help", id: id + "-help" }, item.help) : null;
  const error = el("p", { class: "error field-error", id: id + "-error", hidden: true });
  const described = [help ? id + "-help" : null, id + "-error"].filter(Boolean).join(" ");
  const star = item.required ? el("span", { class: "required", title: S.Required }, " *") : null;
  let input;
  switch (item.type) {
    case "CheckBox":
      input = el("input", { type: "checkbox", id, name: item.name, checked: String(value).toLowerCase() === "true", "aria-describedby": described });
      return el("div", { class: "field check" }, el("label", { for: id }, input, el("span", {}, label, star)), help, error);
    case "TextArea":
      input = el("textarea", { id, name: item.name, rows: 4, placeholder: item.placeholder || null, "aria-describedby": described });
      input.value = value ?? "";
      break;
    case "DropDown":
      input = el("select", { id, name: item.name, "aria-describedby": described },
        el("option", { value: "" }, S.ChooseOne),
        ...(item.options || []).map(o => el("option", { value: o, selected: o === value }, o)));
      break;
    case "Date": {
      const picker = datePicker({ id, name: item.name, "aria-describedby": described, placeholder: item.placeholder || null }, value);
      if (item.required) picker.input.required = true;
      return el("div", { class: "field" }, el("label", { for: id }, label, star), picker.wrap, help, error);
    }
    default:
      input = el("input", {
        id, name: item.name, "aria-describedby": described, placeholder: item.placeholder || null,
        type: item.type === "Number" ? "number" : "text",
        step: item.type === "Number" ? "any" : null,
      });
      input.value = value ?? "";
  }
  if (item.required) input.required = true;
  return el("div", { class: "field" }, el("label", { for: id }, label, star), input, help, error);
}

// A date, typed as yyyy-MM-dd or chosen on a month drawn in the website's own colours: browsers draw their own date
// boxes, each differently, and they cannot be made to match. Weeks start on Monday; names follow the browser's language.
function datePicker(attrs, value) {
  const input = el("input", { ...attrs, type: "text", inputmode: "numeric", autocomplete: "off", placeholder: attrs.placeholder || S.DateFormat });
  input.value = value ?? "";
  const button = el("button", { type: "button", class: "date-button", title: S.ChooseDate, "aria-label": S.ChooseDate, "aria-haspopup": "dialog", "aria-expanded": "false" }, icon("calendar"));
  const popup = el("div", { class: "date-popup", role: "dialog", "aria-label": S.ChooseDate, hidden: true });
  const wrap = el("div", { class: "date-field" }, input, button, popup);
  let month;
  let focused;

  const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const parse = text => {
    const t = (text || "").trim();
    return validDate(t) ? new Date(+t.slice(0, 4), +t.slice(5, 7) - 1, +t.slice(8, 10)) : null;
  };

  function draw(focus) {
    const selected = parse(input.value);
    const today = iso(new Date());
    const start = new Date(month.getFullYear(), month.getMonth(), 1 - ((month.getDay() + 6) % 7));
    const days = Array.from({ length: 42 }, (_, i) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + i));
    const names = days.slice(0, 7).map(d => el("span", { class: "weekday", "aria-hidden": "true" }, d.toLocaleDateString([], { weekday: "narrow" })));
    const cells = days.map(d => {
      const value = iso(d);
      const cls = ["day", d.getMonth() !== month.getMonth() && "other", value === today && "today", selected && value === iso(selected) && "selected"];
      return el("button", {
        type: "button", class: cls.filter(Boolean).join(" "), tabindex: value === focused ? "0" : "-1", "data-date": value,
        "aria-label": d.toLocaleDateString([], { dateStyle: "full" }), "aria-current": value === today ? "date" : null,
        onclick: () => choose(value),
      }, String(d.getDate()));
    });
    popup.replaceChildren(
      el("div", { class: "date-head" },
        el("button", { type: "button", class: "icon-button", title: S.PreviousMonth, "aria-label": S.PreviousMonth, onclick: () => turn(-1) }, icon("chevronLeft")),
        el("strong", { "aria-live": "polite" }, month.toLocaleDateString([], { month: "long", year: "numeric" })),
        el("button", { type: "button", class: "icon-button", title: S.NextMonth, "aria-label": S.NextMonth, onclick: () => turn(1) }, icon("chevron"))),
      el("div", { class: "date-grid" }, ...names, ...cells),
      el("div", { class: "date-foot" },
        el("button", { type: "button", class: "link-button", onclick: () => choose(today) }, S.Today),
        el("button", { type: "button", class: "link-button", onclick: () => choose("") }, S.Clear)));
    if (focus) popup.querySelector(`[data-date="${focused}"]`)?.focus();
  }

  // Shows the month of a date, with that date the one the keyboard is on.
  function show(date, focus) {
    month = new Date(date.getFullYear(), date.getMonth(), 1);
    focused = iso(date);
    draw(focus);
  }

  function turn(by) {
    const day = parse(focused) || month;
    const target = new Date(month.getFullYear(), month.getMonth() + by, 1);
    const last = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate();
    show(new Date(target.getFullYear(), target.getMonth(), Math.min(day.getDate(), last)), false);
  }

  function open() {
    popup.hidden = false;
    button.setAttribute("aria-expanded", "true");
    show(parse(input.value) || new Date(), true);
    document.addEventListener("mousedown", outside, true);
  }

  function close(refocus) {
    popup.hidden = true;
    button.setAttribute("aria-expanded", "false");
    document.removeEventListener("mousedown", outside, true);
    if (refocus) input.focus();
  }

  function outside(e) {
    if (!wrap.contains(e.target)) close(false);
  }

  function choose(value) {
    input.value = value;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    close(true);
  }

  button.addEventListener("click", () => (popup.hidden ? open() : close(true)));
  input.addEventListener("keydown", e => {
    if (e.key === "ArrowDown" && e.altKey) { e.preventDefault(); open(); }
  });
  popup.addEventListener("keydown", e => {
    if (e.key === "Escape") { e.preventDefault(); close(true); return; }
    const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
    const from = e.target.dataset && parse(e.target.dataset.date);
    if (!step || !from) return;
    e.preventDefault();
    show(new Date(from.getFullYear(), from.getMonth(), from.getDate() + step), true);
  });
  return { wrap, input };
}

function drawForm(page, item, onSubmit) {
  const defaults = {};
  for (const f of (item && item.fields) || []) defaults[f.name] = f.default;
  const parts = page.items.map(part => {
    switch (part.type) {
      case "Heading": return el("h3", { class: "section-title" }, part.text);
      case "Text": return el("p", { class: "text" }, part.text);
      case "Value": return el("div", { class: "kv" }, el("span", { class: "muted" }, part.label || part.name), el("strong", {}, part.text));
      case "Divider": return el("hr");
      default: return part.name ? field(part, part.name in defaults ? defaults[part.name] : part.default) : null;
    }
  });
  const error = el("p", { class: "error", role: "alert", hidden: true });
  const button = el("button", { type: "submit", class: "primary", disabled: !onSubmit }, page.submitText || S.Send);
  const form = el("form", { class: "card form-card", novalidate: true },
    el("div", { class: "card-head" }, el("div", {},
      el("h2", { class: "card-title" }, S.FillIn),
      page.description ? el("p", { class: "card-subtitle" }, page.description) : null)),
    ...parts,
    error,
    el("div", { class: "actions" }, button, link("/", { class: "button" }, S.Back)));
  const inputs = page.items
    .filter(part => part.name && !["Heading", "Text", "Value", "Divider"].includes(part.type))
    .map(part => [part, form.elements.namedItem(part.name)])
    .filter(([, input]) => input);
  for (const [part, input] of inputs) recheck(input, () => problemOf(part, input));
  if (onSubmit) {
    form.addEventListener("submit", async e => {
      e.preventDefault();
      error.hidden = true;
      if (!checkFields(inputs.map(([part, input]) => [input, () => problemOf(part, input)]))) {
        error.textContent = S.FixTheFields;
        error.hidden = false;
        return;
      }
      const values = {};
      for (const [part, input] of inputs)
        values[part.name] = part.type === "CheckBox" ? input.checked : input.value;
      button.disabled = true;
      button.textContent = S.Sending;
      try {
        await onSubmit(values);
      } catch (err) {
        error.textContent = err.message;
        error.hidden = false;
        // The server's own checks, which go further than these, such as a field the workflow no longer has.
        for (const [name, problem] of Object.entries((err.data && err.data.fields) || {})) {
          const input = form.elements.namedItem(name);
          if (input) showProblem(input, problem);
        }
        form.querySelector(".field.invalid input, .field.invalid select, .field.invalid textarea")?.focus();
        button.disabled = false;
        button.textContent = page.submitText || S.Send;
      }
    });
  }
  return form;
}

function detailsCard(rows) {
  return el("aside", { class: "card details" },
    el("div", { class: "card-head" }, el("div", {}, el("h2", { class: "card-title" }, S.Details), el("p", { class: "card-subtitle" }, S.DetailsTip))),
    ...rows.filter(Boolean).map(([k, v]) => el("div", { class: "kv" }, el("span", { class: "muted" }, k), el("strong", {}, v))));
}

async function itemView(id) {
  const back = link("/", { class: "button" }, icon("back", "inline"), " ", S.Back);
  frame(null, pageHeader(S.Loading, "", back), el("p", { class: "muted" }, S.Loading));
  let item;
  try {
    item = await api("GET", "/api/items/" + encodeURIComponent(id));
  } catch (err) {
    if (err.status) frame(null, pageHeader(S.NotFound, "", back), el("div", { class: "card" }, el("p", { class: "error" }, err.message)));
    return;
  }
  const details = detailsCard([
    [S.Workflow, item.workflow],
    [S.Run, "#" + item.serial],
    [S.Asked, when(item.asked)],
    item.until ? [S.Until, when(item.until)] : null,
    [S.For, assignee(item.assignedTo) + (item.escalated ? " (" + S.Escalated + ")" : "")],
    ...Object.entries(item.metadata || {}).filter(([k]) => !["runId", "serial", "assignedTo"].includes(k))
      .map(([k, v]) => [k, typeof v === "string" ? v : JSON.stringify(v)]),
  ]);
  const form = drawForm(item.page, item, async values => {
    await api("POST", "/api/items/" + encodeURIComponent(id), { values });
    frame(null, pageHeader(S.Sent, format(S.FromWorkflow, item.workflow, "#" + item.serial)),
      el("div", { class: "card done" }, el("span", { class: "tile-icon big" }, icon("check")), el("h2", {}, S.Sent), el("p", { class: "muted" }, S.SentTip),
        link("/", { class: "button primary" }, S.BackToList)));
  });
  frame(null, pageHeader(item.page.title || item.message, format(S.AskedBy, item.workflow, "#" + item.serial, ago(item.asked)), back),
    el("div", { class: "split" }, form, details));
}

async function previewView(id) {
  frame("/preview/" + encodeURIComponent(id), pageHeader(S.Loading, ""), el("p", { class: "muted" }, S.Loading));
  try {
    const data = await api("GET", "/api/pages/" + encodeURIComponent(id));
    const page = data.page;
    frame("/preview/" + encodeURIComponent(id), pageHeader(page.title || page.name, S.PreviewTitle),
      el("p", { class: "banner" }, icon("eye", "inline"), " ", S.PreviewNote),
      el("div", { class: "split" }, drawForm(page, null, null),
        detailsCard([[S.PageName, page.name], [S.Fields, String(page.items.filter(i => !["Heading", "Text", "Value", "Divider"].includes(i.type)).length)]])));
  } catch (err) {
    if (err.status) frame(null, pageHeader(S.NotFound, ""), el("div", { class: "card" }, el("p", { class: "error" }, err.message)));
  }
}

async function route() {
  const path = location.pathname;
  if (path === "/login") return loginView();
  if (!me) {
    try {
      me = await api("GET", "/api/me");
    } catch {
      return;
    }
  }
  if (me.administrator && pages === null) {
    try { pages = (await api("GET", "/api/pages")).pages; } catch { pages = []; }
  }
  const item = path.match(/^\/items\/([^/]+)$/);
  if (item) return itemView(decodeURIComponent(item[1]));
  const preview = path.match(/^\/preview\/([^/]+)$/);
  if (preview) return previewView(decodeURIComponent(preview[1]));
  return listView(path === "/mine" && me.administrator);
}

window.addEventListener("popstate", route);
document.addEventListener("DOMContentLoaded", async () => {
  try {
    S = await (await fetch("/assets/strings.json")).json();
  } catch {
    S = {};
  }
  document.title = S.AppTitle || document.title;
  route();
});
