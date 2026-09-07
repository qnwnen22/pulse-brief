const state = {
  csrfToken: "",
  activePanel: "dashboardPanel",
  articlePage: 1,
  articlePageSize: 25,
  categories: [],
  sources: [],
  dashboard: null,
};

const $ = (selector) => document.querySelector(selector);
const loginView = $("#loginView");
const adminShell = $("#adminShell");
const statusMessage = $("#statusMessage");
