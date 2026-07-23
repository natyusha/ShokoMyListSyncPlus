/**
 * @file script.js
 * @description Frontend logic for the Shoko MyList Sync+ dashboard UI.
 */
(() => {
  // #region Setup & State
  const base = location.pathname.split("/dashboard")[0];
  const el = (id) => document.getElementById(id);
  const dropZone = el("drop-zone");
  const fileInput = el("file-input");
  const startBtn = el("start-sync");
  const logArea = el("log-area");
  const reportLinkContainer = el("report-link-container");
  const reportLink = el("report-link");
  const apiKeyInput = el("api-key");

  let selectedFile = null;
  let pollTimer = null;

  // Load and save the Shoko API Key in localStorage for convenience
  if (apiKeyInput) {
    apiKeyInput.value = localStorage.getItem("mylist-sync-apikey") || "";
    apiKeyInput.onchange = () => localStorage.setItem("mylist-sync-apikey", apiKeyInput.value.trim());
  }
  // #endregion

  // #region Helpers
  /**
   * Toggles a button's loading state with a spinner overlay.
   * @param {HTMLElement} btn - The button to modify.
   * @param {boolean} isLoading - True to enable the loading overlay.
   * @returns {void}
   */
  function setButtonLoading(btn, isLoading) {
    if (!btn) return;
    btn.classList.toggle("loading", isLoading);
    btn.disabled = isLoading;
    if (isLoading && !btn.querySelector(".button-spinner")) {
      const s = document.createElement("span");
      s.className = "button-spinner";
      s.innerHTML = '<svg><use href="img/icons.svg#loading"></use></svg>';
      btn.appendChild(s);
    } else if (!isLoading) {
      btn.querySelector(".button-spinner")?.remove();
    }
  }

  /**
   * Appends a message to the real-time UI log area and scrolls to the bottom.
   * @param {string} msg - The log message text.
   * @returns {void}
   */
  function log(msg) {
    logArea.textContent += msg + "\n";
    logArea.scrollTop = logArea.scrollHeight;
  }

  /**
   * Validates and accepts the dropped or selected file.
   * @param {File} file - The file to process.
   * @returns {void}
   */
  function handleFile(file) {
    selectedFile = file;
    el("file-name").textContent = file.name;
    startBtn.disabled = false;
    reportLinkContainer.style.display = "none";
  }
  // #endregion

  // #region Tooltips & Links
  /**
   * Initializes the custom hover tooltip overlay and configures secure external link target behaviors.
   * @returns {void}
   */
  function initTooltips() {
    if (el("shoko-tooltip")) return;
    const tpl = document.createElement("div");
    tpl.id = "shoko-tooltip";
    tpl.className = "tooltip-core tooltip-box tooltip-dark tooltip-place-top";
    tpl.setAttribute("role", "status");
    tpl.setAttribute("aria-hidden", "true");
    tpl.innerHTML = '<div class="tooltip-arrow"></div><div class="rt-content"></div>';
    document.body.appendChild(tpl);
    const content = tpl.querySelector(".rt-content");
    let showTimer;

    const show = (target) => {
      const text = target.dataset.tooltipText || target.getAttribute("data-tooltip") || target.getAttribute("title");
      if (target.disabled || !text) return;
      if (target.dataset.tooltipOverflowOnly === "true" && target.scrollWidth <= target.clientWidth) return;
      content.textContent = text;
      tpl.className = "tooltip-core tooltip-box tooltip-dark tooltip-show";
      tpl.setAttribute("aria-hidden", "false");
      const disabledChild = target.tagName !== "LABEL" ? target.querySelector(":disabled, [disabled]") : null;
      const rect = disabledChild ? disabledChild.getBoundingClientRect() : target.getBoundingClientRect();
      const vw = document.documentElement.clientWidth;
      const vh = document.documentElement.clientHeight;
      const margin = 10;
      let place = "top";
      let top = rect.top - tpl.offsetHeight - margin;
      let left = rect.left + rect.width / 2 - tpl.offsetWidth / 2;
      if (top < margin) {
        const spaceBelow = vh - rect.bottom;
        if (spaceBelow > tpl.offsetHeight + margin) {
          top = rect.bottom + margin;
          place = "bottom";
        }
      }
      const minLeft = margin,
        maxLeft = vw - tpl.offsetWidth - margin;
      const clampedLeft = Math.max(minLeft, Math.min(left, maxLeft));
      const arrow = tpl.querySelector(".tooltip-arrow");
      if (arrow) arrow.style.marginLeft = `${left - clampedLeft}px`;
      tpl.classList.add(`tooltip-place-${place}`);
      tpl.style.left = `${Math.round(clampedLeft + window.scrollX)}px`;
      tpl.style.top = `${Math.round(top + window.scrollY)}px`;
      target.setAttribute("aria-describedby", "shoko-tooltip");
    };

    const hide = () => {
      clearTimeout(showTimer);
      tpl.classList.remove("tooltip-show");
      tpl.classList.add("tooltip-closing");
      tpl.setAttribute("aria-hidden", "true");
      setTimeout(() => tpl.classList.remove("tooltip-closing"), 150);
    };

    window.addEventListener("blur", hide);
    document.addEventListener("mouseleave", hide);

    const attach = (t) => {
      // Tooltip logic
      if (t.title) {
        t.dataset.tooltipText = t.title;
        t.removeAttribute("title");
        if (!t.dataset.tooltipAttached) {
          t.dataset.tooltipAttached = "true";
          t.addEventListener("mouseenter", () => {
            showTimer = setTimeout(() => {
              if (t.matches(":hover")) show(t);
            }, 100);
          });
          t.addEventListener("mouseleave", hide);
          t.addEventListener("mousedown", hide);
        }
      }
      // Automated link behavior: Force new tab and security headers for all links
      if (t.tagName === "A" && t.hasAttribute("href")) {
        const href = t.getAttribute("href");
        if (href && !href.startsWith("#") && !href.startsWith("javascript:")) {
          t.target = "_blank";
          t.rel = "noopener noreferrer";
        }
      }
    };

    document.querySelectorAll("[title], a[href]").forEach(attach);
    new MutationObserver((ms) => {
      ms.forEach((m) => {
        m.addedNodes.forEach((n) => {
          if (n.nodeType === 1) {
            attach(n);
            n.querySelectorAll("[title], a[href]").forEach(attach);
          }
        });
        if (m.type === "attributes" && (m.attributeName === "title" || m.attributeName === "href") && m.target.nodeType === 1) attach(m.target);
      });
    }).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["title", "href"] });
  }
  // #endregion

  // #region Drag & Drop
  dropZone.onclick = () => fileInput.click();

  dropZone.ondragover = (e) => {
    e.preventDefault();
    dropZone.classList.add("dragover");
  };

  dropZone.ondragleave = () => dropZone.classList.remove("dragover");

  dropZone.ondrop = (e) => {
    e.preventDefault();
    dropZone.classList.remove("dragover");
    if (e.dataTransfer.files.length) handleFile(e.dataTransfer.files[0]);
  };

  fileInput.onchange = (e) => {
    if (e.target.files.length) handleFile(e.target.files[0]);
  };
  // #endregion

  // #region Status Polling
  /**
   * Polls the server API for current sync status and updates the UI counters.
   * @returns {Promise<void>}
   */
  async function pollStatus() {
    try {
      const res = await fetch(`${base}/status`);
      const data = await res.json();

      // Support both PascalCase and camelCase depending on Shoko's global JSON serializer settings
      const isRunning = data.IsRunning ?? data.isRunning;
      const missing = data.MissingCount ?? data.missingCount ?? 0;
      const outOfSync = data.OutOfSyncCount ?? data.outOfSyncCount ?? 0;
      const processed = data.ProcessedEpisodes ?? data.processedEpisodes ?? 0;
      const errors = data.Errors ?? data.errors ?? 0;
      const logs = data.Logs ?? data.logs ?? [];
      const reportUrl = data.LastReportUrl ?? data.lastReportUrl;

      const total = missing + outOfSync;

      el("stat-missing").textContent = missing;
      el("stat-out-of-sync").textContent = outOfSync;
      el("stat-processed").textContent = `${processed} / ${total}`;
      el("stat-errors").textContent = errors;

      if (logs.length) logs.forEach((l) => log(l));

      if (!isRunning && pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
        setButtonLoading(startBtn, false);

        // Show the report log link if generated
        if (reportUrl) {
          reportLink.href = reportUrl;
          reportLinkContainer.style.display = "";
        }
      }
    } catch (err) {
      console.error("Poll error", err);
    }
  }
  // #endregion

  // #region Event Handlers
  startBtn.onclick = async () => {
    if (!selectedFile) return;
    const dryRun = el("dry-run").checked;
    const apiKey = apiKeyInput ? apiKeyInput.value.trim() : "";

    if (!dryRun && !apiKey) {
      log("Error: Shoko API Key is required for live sync.");
      return;
    }

    const fd = new FormData();
    fd.append("exportFile", selectedFile);
    fd.append("dryRun", dryRun);
    fd.append("apiKey", apiKey);

    setButtonLoading(startBtn, true);
    reportLinkContainer.style.display = "none";
    logArea.textContent = "";
    log("Starting sync...");

    try {
      const res = await fetch(`${base}/sync`, { method: "POST", body: fd });
      if (!res.ok) {
        log("Error starting sync: " + (await res.text()));
        setButtonLoading(startBtn, false);
        return;
      }
      pollTimer = setInterval(pollStatus, 1000);
    } catch (err) {
      log("Fetch error: " + err.message);
      setButtonLoading(startBtn, false);
    }
  };
  // #endregion

  // #region Initialization
  initTooltips();
  // #endregion
})();
