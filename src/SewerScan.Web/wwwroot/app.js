"use strict";

const input = document.getElementById("pdf-files");
const dropzone = document.getElementById("dropzone");
const fileList = document.getElementById("files-list");
const form = document.getElementById("upload-form");
const analyzeButton = document.getElementById("analyze-button");
const statusBox = document.getElementById("status");
const resultsSection = document.getElementById("results");
const searchInput = document.getElementById("table-search");
const tableHead = document.getElementById("table-head");
const tableBody = document.getElementById("table-body");
const tabs = Array.from(document.querySelectorAll(".tab"));
const resultData = { manholes: [], inlets: [], pipes: [] };
let selectedFiles = [];
let activeTab = "manholes";

const schemas = {
  manholes: [
    ["identifier", "Oznaczenie"], ["type", "Typ"], ["diameterMm", "DN [mm]"],
    ["heightM", "H [m]"], ["groundElevationM", "Rz. góry [m]"],
    ["invertElevationM", "Rz. dna [m]"], ["crown", "Zwieńczenie"],
    ["transitions", "Przejścia"], ["completenessPercent", "Kompletność [%]"],
    ["confidence", "Pewność"], ["validationIssues", "Uwagi"]
  ],
  inlets: [
    ["identifier", "Oznaczenie"], ["confidence", "Pewność"], ["sourceDocuments", "Źródła"]
  ],
  pipes: [
    ["material", "Materiał"], ["diameterMm", "DN [mm]"],
    ["page", "Strona"], ["sourceDocument", "Źródło"]
  ]
};

function display(value, key) {
  if (value === null || value === undefined || value === "") return "—";
  if (key === "transitions") {
    return (value || []).map(t => `${t.material || "?"} DN${t.diameterMm} × ${t.quantity}`).join(" | ") || "—";
  }
  if (Array.isArray(value)) return value.join(", ") || "—";
  if (typeof value === "number" && ["heightM", "groundElevationM", "invertElevationM"].includes(key)) {
    return value.toLocaleString("pl-PL", { maximumFractionDigits: 3 });
  }
  return String(value);
}

function setStatus(message, error = false) {
  statusBox.hidden = false;
  statusBox.classList.toggle("error", error);
  statusBox.textContent = message;
}

function renderFiles() {
  fileList.replaceChildren();
  for (const file of selectedFiles) {
    const chip = document.createElement("span");
    chip.className = "file-chip";
    const title = document.createElement("strong");
    title.textContent = file.name;
    const size = document.createElement("span");
    size.className = "size";
    size.textContent = ` · ${(file.size / 1048576).toFixed(1)} MB`;
    chip.append(title, size);
    fileList.appendChild(chip);
  }
}

function setFiles(files) {
  selectedFiles = Array.from(files);
  renderFiles();
}

input.addEventListener("change", () => setFiles(input.files));
["dragenter", "dragover"].forEach(eventName => {
  dropzone.addEventListener(eventName, event => {
    event.preventDefault();
    dropzone.classList.add("dragging");
  });
});
["dragleave", "drop"].forEach(eventName => {
  dropzone.addEventListener(eventName, event => {
    event.preventDefault();
    dropzone.classList.remove("dragging");
  });
});
dropzone.addEventListener("drop", event => {
  if (event.dataTransfer && event.dataTransfer.files) setFiles(event.dataTransfer.files);
});

function renderTable() {
  tableHead.replaceChildren();
  tableBody.replaceChildren();
  const row = document.createElement("tr");
  for (const [, label] of schemas[activeTab]) {
    const th = document.createElement("th");
    th.textContent = label;
    row.appendChild(th);
  }
  tableHead.appendChild(row);

  const needle = searchInput.value.trim().toLocaleLowerCase("pl-PL");
  const rows = resultData[activeTab].filter(item => {
    return !needle || schemas[activeTab].some(([key]) =>
      display(item[key], key).toLocaleLowerCase("pl-PL").includes(needle));
  });

  for (const item of rows) {
    const tr = document.createElement("tr");
    for (const [key] of schemas[activeTab]) {
      const td = document.createElement("td");
      const value = display(item[key], key);
      td.textContent = value;
      if (key === "identifier") td.classList.add("key");
      if (key === "confidence") {
        td.textContent = "";
        const indicator = document.createElement("span");
        indicator.className = /niska/i.test(value) ? "low" : "high";
        indicator.textContent = value;
        td.appendChild(indicator);
      }
      tr.appendChild(td);
    }
    tableBody.appendChild(tr);
  }

  if (!rows.length) {
    const tr = document.createElement("tr");
    const td = document.createElement("td");
    td.colSpan = schemas[activeTab].length;
    td.textContent = needle ? "Brak wyników dla podanego filtra." : "Nie znaleziono obiektów tego typu.";
    tr.appendChild(td);
    tableBody.appendChild(tr);
  }
  document.getElementById("row-count").textContent = `Wyświetlono ${rows.length} z ${resultData[activeTab].length} pozycji`;
}

function activateTab(tab) {
  activeTab = tab;
  tabs.forEach(button => {
    const active = button.dataset.tab === tab;
    button.classList.toggle("active", active);
    button.setAttribute("aria-selected", String(active));
  });
  renderTable();
}
tabs.forEach(button => button.addEventListener("click", () => activateTab(button.dataset.tab)));
searchInput.addEventListener("input", renderTable);

function showResult(data) {
  resultData.manholes = data.manholes || [];
  resultData.inlets = data.inlets || [];
  resultData.pipes = data.pipes || [];
  document.getElementById("metric-manholes").textContent = data.counts.manholes;
  document.getElementById("metric-inlets").textContent = data.counts.inlets;
  document.getElementById("metric-pipes").textContent = data.counts.pipes;
  document.getElementById("metric-incomplete").textContent = data.counts.incomplete;

  const docDescription = document.getElementById("documents-description");
  docDescription.textContent = `${data.documents.length} dokumentów · rodzaj: ${data.drawingType}`;
  document.getElementById("export-button").href = `/api/export/${encodeURIComponent(data.analysisId)}`;

  const warnings = document.getElementById("warnings");
  warnings.replaceChildren();
  warnings.hidden = !(data.warnings && data.warnings.length);
  if (!warnings.hidden) {
    const heading = document.createElement("strong");
    heading.textContent = "Uwaga: nie wszystkie pliki udało się przeanalizować.";
    warnings.appendChild(heading);
    const list = document.createElement("ul");
    data.warnings.forEach(message => {
      const li = document.createElement("li");
      li.textContent = message;
      list.appendChild(li);
    });
    warnings.appendChild(list);
  }
  searchInput.value = "";
  resultsSection.hidden = false;
  activateTab("manholes");
  resultsSection.scrollIntoView({ behavior: "smooth", block: "start" });
}

form.addEventListener("submit", async event => {
  event.preventDefault();
  if (!selectedFiles.length) {
    setStatus("Wybierz przynajmniej jeden plik PDF.", true);
    return;
  }
  if (selectedFiles.length > 10 || selectedFiles.some(f => f.size > 25 * 1048576)) {
    setStatus("Limit: 10 plików, maksymalnie 25 MB każdy.", true);
    return;
  }
  const body = new FormData();
  selectedFiles.forEach(file => body.append("files", file, file.name));
  analyzeButton.disabled = true;
  resultsSection.hidden = true;
  setStatus("Trwa analiza PDF. Duże skany mogą wymagać przetwarzania OCR…");
  try {
    const response = await fetch("/api/analyze", {
      method: "POST",
      headers: { "X-PrefabScan-Client": "web" },
      body
    });
    const payload = await response.json();
    if (!response.ok) {
      setStatus(payload.error || payload.title || `Błąd HTTP ${response.status}`, true);
      return;
    }
    setStatus(`Analiza zakończona: ${payload.counts.manholes} studni, ${payload.counts.inlets} wpustów, ${payload.counts.pipes} przewodów.`);
    showResult(payload);
  } catch (error) {
    setStatus("Brak odpowiedzi serwera. Sprawdź, czy PrefabScan Web nadal działa.", true);
    console.error(error);
  } finally {
    analyzeButton.disabled = false;
  }
});
