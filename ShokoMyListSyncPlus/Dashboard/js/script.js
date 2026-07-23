/**
 * @file script.js
 * @description Frontend logic for the Shoko MyList Sync Plus dashboard UI.
 */
(() => {
  // #region MARK: Setup & State
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

  // #region MARK: Helpers
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

  // #region MARK: Drag & Drop
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

  // #region MARK: Status Polling
  /**
   * Polls the server API for current sync status and updates the UI counters.
   * @returns {Promise<void>}
   */
  async function pollStatus() {
    try {
      const res = await fetch("/api/plugin/ShokoMyListSyncPlus/status");
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

  // #region MARK: Event Handlers
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
      const res = await fetch("/api/plugin/ShokoMyListSyncPlus/sync", { method: "POST", body: fd });
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
})();
