const state = {
  email: localStorage.getItem("spendgov.email") || "demo@spendgov.local",
  view: "overview",
  workspaces: [],
  projects: [],
  selectedWorkspaceId: null,
  selectedProjectId: null,
  projectDetail: null,
  currentUser: null,
  budgets: [],
  analyses: [],
  selectedAnalysis: null,
  approvals: [],
  audit: [],
  filters: {
    search: "",
    status: "",
    decision: "",
    environment: ""
  },
  devDemoAvailable: false
};

const $ = (id) => document.getElementById(id);

document.addEventListener("DOMContentLoaded", async () => {
  $("emailInput").value = state.email;
  wireEvents();
  await initDevDemoControls();
  await loadWorkspaces();
});

function wireEvents() {
  document.querySelectorAll(".nav-button").forEach((button) => {
    button.addEventListener("click", () => showView(button.dataset.view));
  });
  $("switchUserButton").addEventListener("click", async () => {
    state.email = $("emailInput").value.trim() || "demo@spendgov.local";
    localStorage.setItem("spendgov.email", state.email);
    await loadWorkspaces();
  });
  $("loginButton").addEventListener("click", login);
  $("registerButton").addEventListener("click", register);
  $("logoutButton").addEventListener("click", logout);
  $("createWorkspaceButton").addEventListener("click", createWorkspace);
  $("createProjectButton").addEventListener("click", createProject);
  $("workspaceSelect").addEventListener("change", async (event) => {
    state.selectedWorkspaceId = event.target.value;
    await loadProjects();
  });
  $("projectSelect").addEventListener("change", async (event) => {
    state.selectedProjectId = event.target.value;
    await loadProjectDetail();
  });
  $("runDemoButton").addEventListener("click", runDemo);
  $("heroRunDemoButton").addEventListener("click", runDemo);
  $("seedDemoButton").addEventListener("click", seedDemoData);
  $("heroSeedDemoButton").addEventListener("click", seedDemoData);
  $("resetDemoButton").addEventListener("click", resetDemoData);
  $("refreshAnalysesButton").addEventListener("click", loadAnalyses);
  $("analysisSearchInput").addEventListener("input", (event) => {
    state.filters.search = event.target.value;
    renderAnalyses();
  });
  $("statusFilter").addEventListener("change", (event) => {
    state.filters.status = event.target.value;
    renderAnalyses();
  });
  $("decisionFilter").addEventListener("change", (event) => {
    state.filters.decision = event.target.value;
    renderAnalyses();
  });
  $("environmentFilter").addEventListener("change", (event) => {
    state.filters.environment = event.target.value;
    renderAnalyses();
  });
  $("saveBudgetsButton").addEventListener("click", saveBudgets);
  $("savePolicyButton").addEventListener("click", savePolicy);
  $("refreshAuditButton").addEventListener("click", loadAudit);
  $("exportSummaryButton").addEventListener("click", () => {
    if (state.selectedProjectId) {
      window.location.href = `/api/projects/${state.selectedProjectId}/export/summary.csv`;
    }
  });
}

async function initDevDemoControls() {
  try {
    const status = await api("/api/dev/demo/status");
    state.devDemoAvailable = Boolean(status.enabled);
    $("devDemoControls").classList.toggle("hidden", !state.devDemoAvailable);
    $("heroSeedDemoButton").disabled = !state.devDemoAvailable;
    $("heroSeedDemoButton").textContent = state.devDemoAvailable ? "Seed screenshot demo" : "Seed demo unavailable";
  } catch {
    state.devDemoAvailable = false;
    $("devDemoControls").classList.add("hidden");
    $("heroSeedDemoButton").disabled = true;
    $("heroSeedDemoButton").textContent = "Seed demo unavailable";
  }
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      "X-User-Email": state.email,
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `${response.status} ${response.statusText}`);
  }

  const contentType = response.headers.get("content-type") || "";
  return contentType.includes("application/json") ? response.json() : response.text();
}

async function login() {
  state.email = $("emailInput").value.trim() || "demo@spendgov.local";
  localStorage.setItem("spendgov.email", state.email);
  await api("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({
      email: state.email,
      password: $("passwordInput").value
    })
  });
  $("passwordInput").value = "";
  showBanner(`Logged in as ${state.email}.`);
  await loadWorkspaces();
}

async function register() {
  state.email = $("emailInput").value.trim() || "demo@spendgov.local";
  localStorage.setItem("spendgov.email", state.email);
  await api("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({
      email: state.email,
      password: $("passwordInput").value,
      displayName: $("displayNameInput").value.trim() || null
    })
  });
  $("passwordInput").value = "";
  showBanner(`Registered ${state.email}.`);
  await loadWorkspaces();
}

async function logout() {
  await api("/api/auth/logout", { method: "POST" });
  state.email = "demo@spendgov.local";
  localStorage.setItem("spendgov.email", state.email);
  $("emailInput").value = state.email;
  $("passwordInput").value = "";
  showBanner("Logged out.");
  await loadWorkspaces();
}

async function loadWorkspaces() {
  try {
    const previousWorkspaceId = state.selectedWorkspaceId;
    state.workspaces = await api("/api/workspaces");
    state.selectedWorkspaceId = state.workspaces.some((workspace) => workspace.id === previousWorkspaceId)
      ? previousWorkspaceId
      : state.workspaces[0]?.id || null;
    renderWorkspaceSelect();
    await loadProjects();
  } catch (error) {
    showBanner(error.message, true);
  }
}

async function loadProjects() {
  if (!state.selectedWorkspaceId) {
    state.projects = [];
    state.selectedProjectId = null;
    renderProjectSelect();
    renderEmptyWorkspace();
    return;
  }

  const previousProjectId = state.selectedProjectId;
  state.projects = await api(`/api/workspaces/${state.selectedWorkspaceId}/projects`);
  state.selectedProjectId = state.projects.some((project) => project.id === previousProjectId)
    ? previousProjectId
    : state.projects[0]?.id || null;
  renderProjectSelect();
  await loadProjectDetail();
}

async function createWorkspace() {
  const name = $("workspaceNameInput").value.trim();
  if (!name) {
    showBanner("Workspace name is required.", true);
    return;
  }

  const workspace = await api("/api/workspaces", {
    method: "POST",
    body: JSON.stringify({ name })
  });
  $("workspaceNameInput").value = "";
  state.selectedWorkspaceId = workspace.id;
  showBanner(`Created workspace ${workspace.name}.`);
  await loadWorkspaces();
}

async function createProject() {
  if (!state.selectedWorkspaceId) {
    showBanner("Select or create a workspace first.", true);
    return;
  }

  const name = $("projectNameInput").value.trim();
  const owner = $("repoOwnerInput").value.trim();
  const repositoryName = $("repoNameInput").value.trim();
  if (!name || !owner || !repositoryName) {
    showBanner("Project name, owner, and repository are required.", true);
    return;
  }

  const project = await api("/api/projects", {
    method: "POST",
    body: JSON.stringify({
      workspaceId: state.selectedWorkspaceId,
      name,
      repositoryOwner: owner,
      repositoryName,
      defaultRegion: "westeurope",
      currency: "EUR",
      hoursPerMonth: 730
    })
  });
  $("projectNameInput").value = "";
  $("repoOwnerInput").value = "";
  $("repoNameInput").value = "";
  state.selectedProjectId = project.id;
  showBanner(`Created project ${project.name}.`);
  await loadProjects();
}

async function loadProjectDetail() {
  if (!state.selectedProjectId) {
    renderEmptyWorkspace();
    return;
  }

  state.projectDetail = await api(`/api/projects/${state.selectedProjectId}`);
  state.analyses = await api(`/api/projects/${state.selectedProjectId}/analyses`);
  renderOverview();
  renderAnalyses();
  await Promise.all([loadPolicies(), loadBudgets(), loadApprovals(), loadAudit()]);
}

async function loadAnalyses() {
  if (!state.selectedProjectId) {
    return;
  }

  state.analyses = await api(`/api/projects/${state.selectedProjectId}/analyses`);
  renderAnalyses();
  renderOverview();
}

async function loadAnalysis(analysisId) {
  state.selectedAnalysis = await api(`/api/analyses/${analysisId}`);
  renderAnalysisDetail();
}

async function loadPolicies() {
  if (!state.selectedProjectId) {
    return;
  }

  const policy = await api(`/api/projects/${state.selectedProjectId}/policies`);
  $("policyEditor").value = policy.yaml;
  renderPolicyValidation(policy.parsed.errors || []);
}

async function loadBudgets() {
  if (!state.selectedProjectId) {
    state.budgets = [];
    renderBudgets();
    return;
  }

  state.budgets = await api(`/api/projects/${state.selectedProjectId}/budgets`);
  renderBudgets();
}

async function loadApprovals() {
  if (!state.selectedProjectId) {
    return;
  }

  state.approvals = await api(`/api/projects/${state.selectedProjectId}/approvals`);
  renderApprovals();
}

async function loadAudit() {
  if (!state.selectedProjectId) {
    return;
  }

  state.audit = await api(`/api/projects/${state.selectedProjectId}/audit-events`);
  renderAudit();
}

async function runDemo() {
  if (!state.selectedProjectId) {
    return;
  }

  const scenario = $("scenarioSelect").value;
  const result = await api(`/api/demo/projects/${state.selectedProjectId}/analyze`, {
    method: "POST",
    body: JSON.stringify({ scenario })
  });
  showBanner(`Analysis ${result.analysis.pullRequestNumber} completed with ${humanPolicy(result.analysis.policyStatus)}.`);
  state.selectedAnalysis = result;
  await loadProjectDetail();
  showView("analyses");
  renderAnalysisDetail();
}

async function seedDemoData() {
  const result = await api("/api/dev/demo/seed", { method: "POST" });
  showBanner(`Seeded ${result.seededScans.length} demo scans for ${result.repository}.`);
  await loadProjects();
  state.selectedProjectId = result.projectId;
  renderProjectSelect();
  await loadProjectDetail();
  showView("analyses");
}

async function resetDemoData() {
  const result = await api("/api/dev/demo/reset", { method: "DELETE" });
  showBanner(`Reset demo data: removed ${result.deletedScans} scans and ${result.deletedChildRows} detail rows.`);
  state.selectedAnalysis = null;
  await loadProjectDetail();
}

async function savePolicy() {
  if (!state.selectedProjectId) {
    return;
  }

  const policy = await api(`/api/projects/${state.selectedProjectId}/policies`, {
    method: "PUT",
    body: JSON.stringify({ yaml: $("policyEditor").value })
  });
  renderPolicyValidation(policy.parsed.errors || []);
  showBanner("Policy saved.");
}

async function saveBudgets() {
  if (!state.selectedProjectId) {
    return;
  }

  const rows = Array.from(document.querySelectorAll("[data-budget-env]"));
  for (const row of rows) {
    const environment = row.dataset.budgetEnv;
    await api(`/api/projects/${state.selectedProjectId}/budgets/${encodeURIComponent(environment)}`, {
      method: "PUT",
      body: JSON.stringify({
        environment,
        maxMonthlyCost: readNumber(row.querySelector("[data-budget-cost]").value),
        maxMonthlyDelta: readNumber(row.querySelector("[data-budget-delta]").value),
        requireApprovalAbove: readNumber(row.querySelector("[data-budget-approval]").value),
        currency: state.projectDetail?.project?.currency || "EUR",
        blockOnBudgetExceeded: row.querySelector("[data-budget-block]").checked
      })
    });
  }

  showBanner("Budgets saved.");
  await loadProjectDetail();
}

async function approveAnalysis(analysisId) {
  const reason = $(`reason-${analysisId}`).value.trim();
  if (!reason) {
    showBanner("Approval reason is required.", true);
    return;
  }

  await api(`/api/analyses/${analysisId}/approve`, {
    method: "POST",
    body: JSON.stringify({ reason })
  });
  showBanner("Approval granted.");
  await loadProjectDetail();
}

function showView(view) {
  state.view = view;
  document.querySelectorAll(".nav-button").forEach((button) => button.classList.toggle("active", button.dataset.view === view));
  document.querySelectorAll(".view").forEach((section) => section.classList.toggle("active", section.id === `${view}View`));
  $("pageTitle").textContent = view[0].toUpperCase() + view.slice(1);
  $("pageSubtitle").textContent = subtitleFor(view);
}

function subtitleFor(view) {
  const project = state.projectDetail?.project;
  if (!project) {
    return "";
  }

  const repo = `${project.repositoryOwner}/${project.repositoryName}`;
  return view === "overview" ? repo : `${repo} - ${project.defaultRegion} - ${project.currency}`;
}

function renderWorkspaceSelect() {
  $("workspaceSelect").innerHTML = state.workspaces.map((workspace) => `<option value="${workspace.id}">${escapeHtml(workspace.name)}</option>`).join("");
  $("workspaceSelect").value = state.selectedWorkspaceId || "";
}

function renderProjectSelect() {
  $("projectSelect").innerHTML = state.projects.map((project) => `<option value="${project.id}">${escapeHtml(project.name)}</option>`).join("");
  $("projectSelect").value = state.selectedProjectId || "";
}

function renderEmptyWorkspace() {
  $("metricPrs").textContent = "0";
  $("metricDelta").textContent = "EUR 0.00";
  $("metricRisk").textContent = "0";
  $("metricWarnings").textContent = "0";
  $("metricRepo").textContent = "-";
  $("metricConfidence").textContent = "-";
  $("repositoryBody").innerHTML = emptyRow(5, "No repositories connected", "Create a project or seed demo data to monitor a repository.");
  $("latestAnalysesBody").innerHTML = emptyRow(9, "No project selected", "Create a project or seed demo data to see cloud and AI spend checks.");
  $("analysesBody").innerHTML = "";
  $("analysisDetail").innerHTML = emptyPanel("Select a project", "Create a project or seed demo data before opening scan details.");
  state.budgets = [];
  renderBudgets();
}

function renderOverview() {
  const detail = state.projectDetail;
  if (!detail) {
    renderEmptyWorkspace();
    return;
  }

  const { project, metrics } = detail;
  const metricSummary = dashboardMetrics(state.analyses, detail.repositories || [], project.currency);
  $("metricPrs").textContent = metricSummary.latestScans;
  $("metricDelta").innerHTML = costDeltaHtml(metricSummary.costAtRisk, project.currency);
  $("metricRisk").textContent = metricSummary.failed;
  $("metricWarnings").textContent = metricSummary.warnings;
  $("metricRepo").textContent = metricSummary.repositories;
  $("metricConfidence").textContent = metricSummary.averageConfidence;
  renderRepositories(detail.repositories || []);
  $("latestAnalysesBody").innerHTML = tableRows(metrics.latestAnalyses);
  showView(state.view);
}

function renderRepositories(repositories) {
  $("repositoryBody").innerHTML = repositories.length === 0
    ? emptyRow(5, "No repositories connected", "Create a project or seed demo data to see repository health.")
    : repositories.map((repository) => `
      <tr>
        <td>${escapeHtml(repository.provider)}</td>
        <td><strong>${escapeHtml(repository.fullName)}</strong></td>
        <td>${escapeHtml(repository.defaultBranch || "-")}</td>
        <td>${repository.installationId ? escapeHtml(repository.installationId) : '<span class="pill simulated">Simulated/local</span>'}</td>
        <td>${formatDate(repository.lastScanAt)}</td>
      </tr>`).join("");
}

function renderAnalyses() {
  renderAnalysisFilters();
  const filtered = filteredAnalyses();
  $("analysesBody").innerHTML = tableRows(filtered, "No scans match these filters.", "Clear filters or seed demo data to see PASS/WARN/FAIL examples.");
  if (!state.selectedAnalysis && filtered.length > 0) {
    loadAnalysis(filtered[0].id);
  }
}

function tableRows(items, title = "No analyses yet.", message = "Connect a repository or seed demo data to see cloud and AI spend checks before merge.") {
  if (!items || items.length === 0) {
    return emptyRow(9, title, message);
  }

  return items.map((item) => `
    <tr onclick="loadAnalysis('${item.id}'); showView('analyses')">
      <td><strong>#${item.pullRequestNumber}</strong></td>
      <td>${escapeHtml(item.repository)}</td>
      <td>${escapeHtml(item.environment || "-")}</td>
      <td>${statusBadge(item.status)}</td>
      <td>${decisionBadge(item.policyStatus)}</td>
      <td>${costDeltaHtml(item.monthlyDelta, item.currency)}</td>
      <td>${confidenceBadge(item.overallConfidence)}</td>
      <td>${formatDate(item.createdAt)}</td>
      <td>${formatDate(item.completedAt)}</td>
    </tr>`).join("");
}

function dashboardMetrics(analyses, repositories, currency) {
  const confidenceScore = { High: 3, Medium: 2, Low: 1, Unknown: 0 };
  const confidenceLabel = { 3: "High", 2: "Medium", 1: "Low", 0: "Unknown" };
  const confidenceValues = (analyses || [])
    .map((analysis) => confidenceScore[analysis.overallConfidence] ?? 0)
    .filter((score) => score > 0);
  const averageScore = confidenceValues.length === 0
    ? null
    : Math.round(confidenceValues.reduce((sum, score) => sum + score, 0) / confidenceValues.length);
  return {
    latestScans: analyses?.length || 0,
    costAtRisk: (analyses || [])
      .filter((analysis) => analysis.policyStatus === "Warn" || analysis.policyStatus === "Block" || analysis.policyStatus === "ApprovalRequired")
      .reduce((sum, analysis) => sum + Math.max(0, Number(analysis.monthlyDelta || 0)), 0),
    failed: (analyses || []).filter((analysis) => analysis.policyStatus === "Block" || analysis.policyStatus === "ApprovalRequired").length,
    warnings: (analyses || []).filter((analysis) => analysis.policyStatus === "Warn").length,
    repositories: repositories?.length || 0,
    averageConfidence: averageScore == null ? "-" : `${confidenceLabel[averageScore]} confidence`
  };
}

function renderAnalysisFilters() {
  setOptions("statusFilter", "All statuses", uniqueValues(state.analyses.map((analysis) => analysis.status)), state.filters.status);
  setOptions("decisionFilter", "All decisions", uniqueValues(state.analyses.map((analysis) => humanPolicy(analysis.policyStatus))), state.filters.decision);
  setOptions("environmentFilter", "All environments", uniqueValues(state.analyses.map((analysis) => analysis.environment).filter(Boolean)), state.filters.environment);
}

function setOptions(id, emptyLabel, values, selected) {
  const select = $(id);
  const previous = selected || "";
  select.innerHTML = [`<option value="">${escapeHtml(emptyLabel)}</option>`]
    .concat(values.map((value) => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`))
    .join("");
  select.value = values.includes(previous) ? previous : "";
  if (id === "statusFilter") {
    state.filters.status = select.value;
  } else if (id === "decisionFilter") {
    state.filters.decision = select.value;
  } else if (id === "environmentFilter") {
    state.filters.environment = select.value;
  }
}

function uniqueValues(values) {
  return [...new Set(values.filter((value) => value !== null && value !== undefined && String(value).trim() !== "").map(String))].sort();
}

function filteredAnalyses() {
  const query = state.filters.search.trim().toLowerCase();
  return (state.analyses || []).filter((analysis) => {
    const decision = humanPolicy(analysis.policyStatus);
    const repository = String(analysis.repository || "").toLowerCase();
    const matchesSearch = !query
      || repository.includes(query)
      || String(analysis.pullRequestNumber).includes(query)
      || (analysis.environment || "").toLowerCase().includes(query);
    return matchesSearch
      && (!state.filters.status || analysis.status === state.filters.status)
      && (!state.filters.decision || decision === state.filters.decision)
      && (!state.filters.environment || analysis.environment === state.filters.environment);
  });
}

function renderAnalysisDetail() {
  const detail = state.selectedAnalysis;
  if (!detail) {
    $("analysisDetail").innerHTML = emptyPanel("Select an analysis", "Open a scan to review cost drivers, pricing metadata, policy results, and recommendations.");
    return;
  }

  const analysis = detail.analysis;
  const repo = `${analysis.repositoryOwner}/${analysis.repositoryName}`;
  $("analysisDetail").innerHTML = `
    <div class="detail-hero">
      <div>
        <p class="eyebrow">Scan detail</p>
        <h2>${escapeHtml(repo)} - PR #${analysis.pullRequestNumber}</h2>
        <p>${escapeHtml(analysis.headBranch || "-")} -> ${escapeHtml(analysis.baseBranch || "-")} | ${escapeHtml(detail.analysisSource || "Unknown source")}</p>
      </div>
      <div class="detail-badges" aria-label="Scan status badges">
        ${decisionBadge(analysis.policyStatus)}
        ${statusBadge(analysis.status)}
        ${confidenceBadge(analysis.overallConfidence)}
      </div>
    </div>
    <div class="detail-content">
      <div class="summary-grid">
        <div class="summary-card"><span>Estimated monthly delta</span><strong>${costDeltaHtml(analysis.monthlyDelta, analysis.currency)}</strong></div>
        <div class="summary-card"><span>Environment</span><strong>${escapeHtml(analysis.environment || "-")}</strong></div>
        <div class="summary-card"><span>Created</span><strong>${formatDate(analysis.createdAt)}</strong></div>
        <div class="summary-card"><span>Completed</span><strong>${formatDate(analysis.completedAt || analysis.startedAt)}</strong></div>
        <div class="summary-card"><span>Baseline monthly</span><strong>${formatMoney(analysis.baselineMonthlyCost, analysis.currency)}</strong></div>
        <div class="summary-card"><span>Proposed monthly</span><strong>${formatMoney(analysis.proposedMonthlyCost, analysis.currency)}</strong></div>
        <div class="summary-card"><span>Budget limit</span><strong>${formatMoney(analysis.budgetLimitMonthly, analysis.currency)}</strong></div>
        <div class="summary-card"><span>Unknown resources</span><strong>${analysis.unknownResourceCount}</strong></div>
      </div>
      <div class="detail-actions">
        <button onclick="downloadCsv('${analysis.id}', 'resources')">Resources CSV</button>
        <button onclick="downloadCsv('${analysis.id}', 'policy-findings')">Findings CSV</button>
        <button onclick="downloadCsv('${analysis.id}', 'recommendations')">Recommendations CSV</button>
      </div>
      ${analysis.errorMessage ? `<div class="status-banner error"><strong>Scan failed</strong><br>${escapeHtml(analysis.errorMessage)}<br>The scan was saved so you can inspect the failure.</div>` : ""}
      ${renderRecommendations(detail.recommendations, analysis.currency)}
      ${renderCostChanges(detail.costChanges, detail.resources, analysis.currency)}
      ${renderResources(detail.resources, analysis.currency)}
      ${renderArmMetadata(detail.resources, analysis.currency)}
      ${renderPricingMetadata(detail.resources)}
      ${renderPolicyAsCodeEvaluations(detail.policyEvaluations)}
      ${renderPolicyEvaluations(detail.policyEvaluations, detail.assumptions)}
      ${renderCiFindings(detail.findings)}
      ${renderFindings(detail.policyFindings)}
      ${renderAssumptions(detail.assumptions)}
      ${renderGithubMetadata(detail)}
      <h3>GitHub PR Report Preview</h3>
      <pre>${escapeHtml(stripPrMarker(detail.commentMarkdown))}</pre>
    </div>
  `;
}

function renderCostChanges(changes, resources, currency) {
  if (!changes || changes.length === 0) {
    return `<h3>Main Cost Breakdown</h3>${emptyPanel("No priced cost changes", "This scan did not produce cost breakdown rows.")}`;
  }

  return `
    <h3>Main Cost Breakdown</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Resource / Workflow</th><th>Change</th><th>Before</th><th>After</th><th>Estimated monthly cost</th><th>Pricing source</th><th>Confidence</th></tr></thead>
      <tbody>${changes.map((change) => {
        const resource = findResourceForChange(change, resources);
        return `
        <tr>
          <td><strong>${escapeHtml(change.resourceName || change.terraformAddress || "-")}</strong><br><small>${escapeHtml(change.resourceType || resource?.resourceType || "-")}</small></td>
          <td>${escapeHtml(formatChange(change.changeKind || resource?.terraformChangeType || resource?.terraformActions || "-"))}</td>
          <td>${escapeHtml(change.beforeSummary || change.beforeSku || "-")}</td>
          <td>${escapeHtml(change.afterSummary || change.afterSku || resource?.afterSummary || resource?.sku || "-")}</td>
          <td>${costDeltaHtml(change.monthlyDelta, currency)}</td>
          <td>${escapeHtml(change.pricingSource || resource?.pricingSourceType || resource?.pricingSource || "-")}</td>
          <td>${confidenceBadge(resource?.confidence || "-")}</td>
        </tr>`;
      }).join("")}</tbody>
    </table></div>`;
}

function renderResources(resources, currency) {
  if (!resources || resources.length === 0) {
    return `<h3>Detected Resources</h3>${emptyPanel("No detected resources", "No cloud resources or AI workflows were detected for this scan.")}`;
  }

  return `
    <h3>Detected Resources</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Source file</th><th>Analysis source</th><th>Provider</th><th>Resource type</th><th>Name</th><th>SKU / tier / size</th><th>Region</th><th>Change type</th><th>Monthly</th><th>Confidence</th></tr></thead>
      <tbody>${resources.map((resource) => `
        <tr>
          <td>${escapeHtml(resource.sourceFile || "-")}</td>
          <td>${escapeHtml(resource.analysisSource || sourceTypeLabel(resource.sourceType))}</td>
          <td>${escapeHtml(resource.provider || "-")}</td>
          <td>${escapeHtml(resource.resourceType || resource.armResourceType || "-")}</td>
          <td><strong>${escapeHtml(resource.resourceName || "-")}</strong></td>
          <td>${escapeHtml(resource.sku || resource.afterSummary || "-")}</td>
          <td>${escapeHtml(resource.region || "-")}</td>
          <td>${escapeHtml(formatChange(resource.terraformChangeType || resource.terraformActions || "added/desired state"))}</td>
          <td>${formatMoney(resource.monthlyCost, currency)}</td>
          <td>${confidenceBadge(resource.confidence)}</td>
        </tr>`).join("")}</tbody>
    </table></div>`;
}

function renderPricingMetadata(resources) {
  const priced = (resources || []).filter((resource) => resource.pricingCatalogVersion || resource.pricingSource || resource.pricingMatchType);
  if (priced.length === 0) {
    return "";
  }

  const first = priced[0];
  const matchTypes = [...new Set(priced.map((resource) => resource.pricingMatchType).filter(Boolean))].join(", ");
  const fallback = priced.find((resource) => resource.pricingFallbackReason);
  const ai = priced.find((resource) => resource.resourceType === "ai.workflow");
  const unit = first.pricingUnitOfMeasure || first.pricingUnit || "-";
  const liveApiUsed = priced.some((resource) => resource.pricingLiveApiUsed);
  const fallbackUsed = priced.some((resource) => resource.pricingFallbackUsed);
  return `
    <h3>Pricing Metadata</h3>
    <div class="list-panel metadata-grid">
      ${metadataItem("Pricing source", first.pricingSourceType || first.pricingSource || "-")}
      ${metadataItem("Catalog version", `${first.pricingCatalogName || "-"} ${first.pricingCatalogVersion || ""}`.trim())}
      ${metadataItem("Azure Retail Prices API used", liveApiUsed ? "yes" : "no")}
      ${metadataItem("Currency", first.currency || "-")}
      ${metadataItem("Region", first.region || "-")}
      ${metadataItem("Unit price", first.pricingUnitPrice == null ? "-" : `${formatMoney(first.pricingUnitPrice, first.currency || "EUR")} / ${unit}`)}
      ${metadataItem("Monthly hours assumption", (first.pricingMonthlyHours || first.hoursPerMonth) ? `${first.pricingMonthlyHours || first.hoursPerMonth} hours/month` : "-")}
      ${metadataItem("Meter", first.pricingMeterName || first.pricingMeterId || "-")}
      ${metadataItem("Product", first.pricingProductName || "-")}
      ${metadataItem("SKU", first.pricingArmSkuName || first.pricingSkuName || first.sku || "-")}
      ${metadataItem("Match type", matchTypes || "-")}
      ${metadataItem("Fallback used", fallbackUsed ? "yes" : "no")}
      ${first.pricingEffectiveStartDate ? metadataItem("Effective start", formatDate(first.pricingEffectiveStartDate)) : ""}
      ${fallback ? metadataItem("Fallback reason", fallback.pricingFallbackReason) : ""}
      ${ai ? metadataItem("AI model pricing", `${ai.sku || "-"} - ${ai.pricingUnit || "1M tokens"}`) : ""}
    </div>`;
}

function renderArmMetadata(resources, currency) {
  const arm = (resources || []).filter((resource) => resource.armResourceType || resource.analysisSource === "Bicep compiled ARM JSON");
  if (arm.length === 0) {
    return "";
  }

  return `
    <h3>ARM / Bicep Details</h3>
    <div class="list-panel">${arm.map((resource) => {
      const raw = armRaw(resource);
      const resolved = [...(raw.armParameterResolved || []), ...(raw.armVariableResolved || [])].join(", ");
      const unresolved = (raw.armUnresolvedExpressions || []).map((item) => `${item.field}: ${item.expression}`).join("; ");
      return `
        <div>
          <strong>${escapeHtml(resource.resourceName || "-")}</strong><br>
          Source ${escapeHtml(resource.sourceFile || "-")}<br>
          ARM ${escapeHtml(resource.armResourceType || "-")} -> ${escapeHtml(resource.mappedResourceType || resource.resourceType || "-")}<br>
          Location ${escapeHtml(resource.region || "-")} - SKU ${escapeHtml(resource.sku || "-")} - API ${escapeHtml(resource.armApiVersion || "-")} - Kind ${escapeHtml(resource.armKind || "-")}<br>
          Monthly ${formatMoney(resource.monthlyCost, currency)} - Confidence ${escapeHtml(resource.confidence || "-")} - Pricing ${escapeHtml(resource.pricingSourceType || resource.pricingMatchType || "-")}
          ${resolved ? `<br>Resolved ${escapeHtml(resolved)}` : ""}
          ${unresolved ? `<br>Unresolved ${escapeHtml(unresolved)}` : ""}
        </div>`;
    }).join("")}</div>`;
}

function armRaw(resource) {
  const outer = parseJson(resource.assumptionsJson);
  const nested = parseJson(outer.AssumptionsJson || outer.assumptionsJson);
  return outer.Raw || outer.raw || nested.Raw || nested.raw || {};
}

function parseJson(value) {
  if (!value || typeof value !== "string") {
    return {};
  }

  try {
    return JSON.parse(value);
  } catch {
    return {};
  }
}

function renderFindings(findings) {
  if (!findings || findings.length === 0) {
    return `<h3>Policy Findings</h3>${emptyPanel("No policy findings", "The scan did not produce warning or blocking findings.")}`;
  }

  return `
    <h3>Policy Findings</h3>
    <div class="list-panel">${findings.map((finding) => `
      <div><span class="pill ${cssToken(finding.action)}">${humanPolicy(finding.action)}</span> <strong>${escapeHtml(finding.ruleId)}</strong><br>${escapeHtml(finding.message)}</div>`).join("")}</div>`;
}

function renderCiFindings(findings) {
  if (!findings || findings.length === 0) {
    return `<h3>CI Findings</h3>${emptyPanel("No CI findings", "The scan did not produce SARIF or annotation-style diagnostics.")}`;
  }

  return `
    <h3>CI Findings</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Severity</th><th>Rule ID</th><th>Category</th><th>Location</th><th>Resource / Workflow</th><th>Message</th><th>Recommendation</th></tr></thead>
      <tbody>${findings.map((finding) => `
        <tr>
          <td>${findingSeverityBadge(finding.severity)}</td>
          <td><code>${escapeHtml(finding.ruleId || "-")}</code></td>
          <td>${escapeHtml(finding.category || "-")}</td>
          <td>${escapeHtml(formatFindingLocation(finding))}</td>
          <td>${escapeHtml(finding.resourceName || "-")}<br><small>${escapeHtml(finding.resourceType || "-")}</small></td>
          <td>${escapeHtml(finding.message || "-")}</td>
          <td>${escapeHtml(finding.recommendation || "-")}</td>
        </tr>`).join("")}</tbody>
    </table></div>`;
}

function formatFindingLocation(finding) {
  if (!finding || !finding.sourceFile) {
    return "-";
  }

  return finding.startLine ? `${finding.sourceFile}:${finding.startLine}` : finding.sourceFile;
}

function renderAssumptions(assumptions) {
  if (!assumptions || assumptions.length === 0) {
    return `<h3>Assumptions</h3>${emptyPanel("No assumptions saved", "This scan did not persist analyzer or pricing assumptions.")}`;
  }

  return `
    <h3>Assumptions</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Name</th><th>Value</th></tr></thead>
      <tbody>${assumptions.map((item) => `
        <tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.value)}</td></tr>`).join("")}</tbody>
    </table></div>`;
}

function renderPolicyEvaluations(evaluations, assumptions) {
  evaluations = (evaluations || []).filter((item) => !item.isPolicyAsCode);
  if (!evaluations || evaluations.length === 0) {
    return `<h3>Policy Evaluations</h3>${emptyPanel("No policy evaluations", "No policy rules were evaluated for this scan.")}`;
  }

  const budgetSource = assumptionValue(assumptions, "BudgetSource") || "-";
  return `
    <h3>Policy Evaluations</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Rule</th><th>Result</th><th>Budget source</th><th>Message</th></tr></thead>
      <tbody>${evaluations.map((item) => `
        <tr><td>${escapeHtml(item.ruleName)}</td><td>${policyResultBadge(item.result)}</td><td>${escapeHtml(budgetSource)}</td><td>${escapeHtml(item.message)}</td></tr>`).join("")}</tbody>
    </table></div>`;
}

function renderPolicyAsCodeEvaluations(evaluations) {
  const policies = (evaluations || []).filter((item) => item.isPolicyAsCode);
  if (policies.length === 0) {
    return `<h3>Policy-as-Code Evaluations</h3>${emptyPanel("No custom spend policies", "No custom spend policies were configured for this scan.")}`;
  }

  return `
    <h3>Policy-as-Code Evaluations</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Policy ID</th><th>Title</th><th>Severity</th><th>Result</th><th>Matched resource/workflow</th><th>Message</th><th>Recommendation</th></tr></thead>
      <tbody>${policies.map((item) => `
        <tr>
          <td><strong>${escapeHtml(item.policyId || item.ruleName)}</strong></td>
          <td>${escapeHtml(item.title || "-")}</td>
          <td>${policySeverityBadge(item.severity || "info")}</td>
          <td>${policyAsCodeResultBadge(item)}</td>
          <td>${escapeHtml(item.matchedResource || "-")}</td>
          <td>${escapeHtml(item.message || "-")}</td>
          <td>${escapeHtml(item.recommendation || "-")}</td>
        </tr>`).join("")}</tbody>
    </table></div>`;
}

function renderGithubMetadata(detail) {
  if (!detail.gitHubCommentId && !detail.gitHubPullRequestUrl && !detail.gitHubCheckRunId && !detail.reportPublishingStatus) {
    return "";
  }

  const pr = detail.gitHubPullRequestUrl
    ? `<a href="${escapeHtml(detail.gitHubPullRequestUrl)}" target="_blank" rel="noreferrer">${escapeHtml(detail.gitHubPullRequestUrl)}</a>`
    : "-";
  const report = detail.gitHubReportUrl
    ? `<a href="${escapeHtml(detail.gitHubReportUrl)}" target="_blank" rel="noreferrer">${escapeHtml(detail.gitHubReportUrl)}</a>`
    : "-";
  return `
    <h3>GitHub</h3>
    ${detail.reportPublishingStatus === "Simulated" ? `<div class="callout-panel"><strong>Simulated GitHub mode</strong><br>This local demo stored an idempotent simulated PR report instead of calling GitHub.</div>` : ""}
    <div class="list-panel">
      ${metadataItem("GitHub PR", pr, true)}
      ${metadataItem("Report URL", report, true)}
      ${metadataItem("Publishing status", `<span class="pill ${cssToken(detail.reportPublishingStatus || "Pending")}">${escapeHtml(detail.reportPublishingStatus || "Pending")}</span>`, true)}
      ${metadataItem("Comment ID", detail.gitHubCommentId || "-")}
      ${metadataItem("Check Run ID", detail.gitHubCheckRunId || "-")}
      ${detail.reportPublishingError ? metadataItem("Publishing error", detail.reportPublishingError) : ""}
    </div>`;
}

function renderRecommendations(recommendations, currency) {
  if (!recommendations || recommendations.length === 0) {
    return `<div class="recommendation-panel"><h3>Recommendation</h3><div>No blocking action needed.</div></div>`;
  }

  return `
    <div class="recommendation-panel">
      <h3>Recommendation</h3>
      <div class="recommendation-list">${recommendations.map((rec) => `
        <div class="recommendation-item">
          <strong>${escapeHtml(rec.title)}</strong><br>
          ${escapeHtml(rec.description)}
          ${rec.estimatedMonthlySavings ? `<br><small>Estimated savings opportunity: ${formatMoney(rec.estimatedMonthlySavings, currency)} / month</small>` : ""}
        </div>`).join("")}</div>
    </div>`;
}

function renderPolicyValidation(errors) {
  $("policyValidation").innerHTML = errors.length === 0
    ? "Policy parsed successfully."
    : errors.map((error) => `<div>${escapeHtml(error)}</div>`).join("");
}

function renderBudgets() {
  const defaults = ["dev", "staging", "production"];
  const byEnvironment = new Map((state.budgets || []).map((budget) => [budget.environment, budget]));
  const rows = [...new Set([...defaults, ...(state.budgets || []).map((budget) => budget.environment)])];
  $("budgetRows").innerHTML = rows.map((environment) => {
    const budget = byEnvironment.get(environment) || {
      environment,
      maxMonthlyCost: null,
      maxMonthlyDelta: null,
      requireApprovalAbove: null,
      blockOnBudgetExceeded: environment === "production"
    };
    return `
      <tr data-budget-env="${escapeHtml(environment)}">
        <td><span class="pill ${cssToken(environment)}">${escapeHtml(environment)}</span></td>
        <td><input data-budget-cost type="number" min="0" step="1" value="${inputValue(budget.maxMonthlyCost)}"></td>
        <td><input data-budget-delta type="number" min="0" step="1" value="${inputValue(budget.maxMonthlyDelta)}"></td>
        <td><input data-budget-approval type="number" min="0" step="1" value="${inputValue(budget.requireApprovalAbove)}"></td>
        <td><input data-budget-block aria-label="Block ${escapeHtml(environment)} budget overages" type="checkbox" ${budget.blockOnBudgetExceeded ? "checked" : ""}></td>
        <td>${escapeHtml(budget.currency || state.projectDetail?.project?.currency || "EUR")}</td>
      </tr>`;
  }).join("");
}

function renderApprovals() {
  const required = state.analyses.filter((analysis) => analysis.policyStatus === "ApprovalRequired");
  $("approvalRequiredList").innerHTML = required.length === 0
    ? `<div class="empty-state">No approvals required.</div>`
    : required.map((analysis) => `
      <div class="approval-item">
        <strong>PR #${analysis.pullRequestNumber}</strong>
        <div>${formatMoney(analysis.monthlyDelta, analysis.currency, true)} - ${shortSha(analysis.commitSha)}</div>
        <textarea id="reason-${analysis.id}" placeholder="Approval reason"></textarea>
        <button onclick="approveAnalysis('${analysis.id}')">Approve</button>
      </div>`).join("");

  $("grantedApprovalsList").innerHTML = state.approvals.length === 0
    ? `<div class="empty-state">No approvals granted.</div>`
    : state.approvals.map((approval) => `
      <div class="approval-item">
        <strong>${shortSha(approval.commitSha)}</strong>
        <div>${escapeHtml(approval.reason)}</div>
        <small>${formatDate(approval.createdAt)}</small>
      </div>`).join("");
}

function renderAudit() {
  $("auditBody").innerHTML = state.audit.length === 0
    ? `<tr><td colspan="3">No audit events.</td></tr>`
    : state.audit.map((event) => `
      <tr><td>${formatDate(event.createdAt)}</td><td>${escapeHtml(event.eventType)}</td><td>${escapeHtml(event.message)}</td></tr>`).join("");
}

function downloadCsv(analysisId, kind) {
  window.location.href = `/api/analyses/${analysisId}/export/${kind}.csv`;
}

function showBanner(message, isError = false) {
  const banner = $("statusBanner");
  banner.textContent = message;
  banner.classList.toggle("error", isError);
  banner.classList.remove("hidden");
  window.setTimeout(() => banner.classList.add("hidden"), 4200);
}

function formatMoney(value, currency, signed = false) {
  if (value === null || value === undefined) {
    return "not available";
  }

  const numeric = Number(value);
  const sign = signed && numeric > 0 ? "+" : signed && numeric < 0 ? "-" : "";
  return `${sign}${currency || "EUR"} ${Math.abs(numeric).toFixed(2)}`;
}

function costDeltaHtml(value, currency) {
  if (value === null || value === undefined) {
    return `<span class="cost-delta">not available</span>`;
  }

  const numeric = Number(value);
  const css = numeric > 0 ? "positive" : numeric < 0 ? "negative" : "zero";
  return `<span class="cost-delta ${css}">${formatMoney(numeric, currency, true)} / month</span>`;
}

function humanPolicy(value) {
  const normalized = String(value || "Pass");
  if (normalized === "Block") {
    return "FAIL";
  }

  if (normalized === "Warn") {
    return "WARN";
  }

  if (normalized === "Pass") {
    return "PASS";
  }

  return normalized.replace("ApprovalRequired", "APPROVAL REQUIRED");
}

function statusBadge(value) {
  const label = String(value || "Unknown");
  return `<span class="pill ${cssToken(label)}">Status: ${escapeHtml(label)}</span>`;
}

function decisionBadge(value) {
  const label = humanPolicy(value);
  const css = label === "FAIL" ? "fail" : label.toLowerCase();
  return `<span class="pill ${cssToken(css)}">Decision: ${escapeHtml(label)}</span>`;
}

function confidenceBadge(value) {
  const label = String(value || "Unknown");
  return `<span class="pill ${cssToken(label)}">${escapeHtml(label)} confidence</span>`;
}

function policyResultBadge(value) {
  const label = String(value || "Unknown");
  return `<span class="pill ${cssToken(label)}">${escapeHtml(label)}</span>`;
}

function policySeverityBadge(value) {
  const label = String(value || "info").toUpperCase();
  return `<span class="pill ${cssToken(label)}">${escapeHtml(label)}</span>`;
}

function findingSeverityBadge(value) {
  const label = String(value || "warning").toUpperCase();
  return `<span class="pill ${cssToken(label)}">${escapeHtml(label)}</span>`;
}

function policyAsCodeResultBadge(item) {
  const label = item.matched ? String(item.policyResult || item.result || "Matched") : "NOT MATCHED";
  return `<span class="pill ${cssToken(label)}">${escapeHtml(label)}</span>`;
}

function cssToken(value) {
  return String(value || "").toLowerCase().replace(/[^a-z]/g, "");
}

function shortSha(value) {
  return value ? String(value).slice(0, 12) : "-";
}

function formatDate(value) {
  return value ? new Date(value).toLocaleString() : "-";
}

function formatChange(value) {
  const text = String(value || "-");
  const normalized = text.toLowerCase();
  if (normalized === "added" || normalized === "create") {
    return "Added";
  }

  if (normalized === "removed" || normalized === "delete") {
    return "Removed";
  }

  if (normalized === "changed" || normalized === "modified" || normalized === "update") {
    return "Modified";
  }

  return text;
}

function sourceTypeLabel(value) {
  const text = String(value || "");
  if (text === "AiConfig") {
    return "AI workflow config";
  }

  if (text === "TerraformPlanJson") {
    return "Terraform plan JSON";
  }

  return text || "-";
}

function findResourceForChange(change, resources) {
  return (resources || []).find((resource) =>
    resource.resourceName === change.resourceName
    || resource.terraformAddress === change.terraformAddress
    || resource.resourceType === change.resourceType);
}

function assumptionValue(assumptions, name) {
  const match = (assumptions || []).find((assumption) => assumption.name === name);
  return match?.value || null;
}

function metadataItem(label, value, isHtml = false) {
  return `<div class="metadata-item"><strong>${escapeHtml(label)}</strong>${isHtml ? value : escapeHtml(value ?? "-")}</div>`;
}

function emptyPanel(title, message) {
  return `<div class="empty-state"><strong>${escapeHtml(title)}</strong>${escapeHtml(message || "")}</div>`;
}

function emptyRow(colspan, title, message) {
  return `<tr><td colspan="${colspan}">${emptyPanel(title, message)}</td></tr>`;
}

function stripPrMarker(markdown) {
  return String(markdown || "").replace("<!-- cloud-ai-spend-governor-report -->", "").trim();
}

function readNumber(value) {
  const trimmed = String(value || "").trim();
  return trimmed ? Number(trimmed) : null;
}

function inputValue(value) {
  return value === null || value === undefined ? "" : Number(value);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
