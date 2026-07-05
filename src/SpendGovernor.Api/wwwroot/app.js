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
  $("seedDemoButton").addEventListener("click", seedDemoData);
  $("resetDemoButton").addEventListener("click", resetDemoData);
  $("refreshAnalysesButton").addEventListener("click", loadAnalyses);
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
  } catch {
    state.devDemoAvailable = false;
    $("devDemoControls").classList.add("hidden");
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
  $("metricRepo").textContent = "-";
  $("repositoryBody").innerHTML = `<tr><td colspan="5">No repositories.</td></tr>`;
  $("latestAnalysesBody").innerHTML = `<tr><td colspan="9">No project available.</td></tr>`;
  $("analysesBody").innerHTML = "";
  $("analysisDetail").innerHTML = "";
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
  $("metricPrs").textContent = metrics.totalPrsAnalyzed;
  $("metricDelta").textContent = formatMoney(metrics.totalMonthlyDeltaDetected, project.currency, true);
  $("metricRisk").textContent = metrics.warnedOrBlockedPrs;
  $("metricRepo").textContent = detail.repositories?.[0]?.fullName || `${project.repositoryOwner}/${project.repositoryName}`;
  renderRepositories(detail.repositories || []);
  $("latestAnalysesBody").innerHTML = tableRows(metrics.latestAnalyses);
  showView(state.view);
}

function renderRepositories(repositories) {
  $("repositoryBody").innerHTML = repositories.length === 0
    ? `<tr><td colspan="5">No repositories.</td></tr>`
    : repositories.map((repository) => `
      <tr>
        <td>${escapeHtml(repository.provider)}</td>
        <td>${escapeHtml(repository.fullName)}</td>
        <td>${escapeHtml(repository.defaultBranch || "-")}</td>
        <td>${escapeHtml(repository.installationId || "-")}</td>
        <td>${formatDate(repository.lastScanAt)}</td>
      </tr>`).join("");
}

function renderAnalyses() {
  $("analysesBody").innerHTML = tableRows(state.analyses);
  if (!state.selectedAnalysis && state.analyses.length > 0) {
    loadAnalysis(state.analyses[0].id);
  }
}

function tableRows(items) {
  if (!items || items.length === 0) {
    return `<tr><td colspan="9">No analyses yet.</td></tr>`;
  }

  return items.map((item) => `
    <tr onclick="loadAnalysis('${item.id}'); showView('analyses')">
      <td>#${item.pullRequestNumber}</td>
      <td>${escapeHtml(item.repository)}</td>
      <td>${escapeHtml(item.environment || "-")}</td>
      <td><span class="pill ${cssToken(item.status)}">${escapeHtml(item.status)}</span></td>
      <td><span class="pill ${cssToken(item.policyStatus)}">${humanPolicy(item.policyStatus)}</span></td>
      <td>${formatMoney(item.monthlyDelta, item.currency, true)}</td>
      <td>${escapeHtml(item.overallConfidence || "-")}</td>
      <td>${formatDate(item.createdAt)}</td>
      <td>${formatDate(item.completedAt)}</td>
    </tr>`).join("");
}

function renderAnalysisDetail() {
  const detail = state.selectedAnalysis;
  if (!detail) {
    $("analysisDetail").innerHTML = `<div class="empty-state">Select an analysis.</div>`;
    return;
  }

  const analysis = detail.analysis;
  $("analysisDetail").innerHTML = `
    <h2>PR #${analysis.pullRequestNumber}</h2>
    <div class="detail-actions">
      <span class="pill ${cssToken(analysis.status)}">${escapeHtml(analysis.status)}</span>
      <span class="pill ${cssToken(analysis.policyStatus)}">${humanPolicy(analysis.policyStatus)}</span>
      <span class="pill">${escapeHtml(detail.analysisSource || "Unknown source")}</span>
      <button onclick="downloadCsv('${analysis.id}', 'resources')">Resources CSV</button>
      <button onclick="downloadCsv('${analysis.id}', 'policy-findings')">Findings CSV</button>
      <button onclick="downloadCsv('${analysis.id}', 'recommendations')">Recommendations CSV</button>
    </div>
    <div class="metric-grid">
      <div class="metric-panel"><span>Baseline</span><strong>${formatMoney(analysis.baselineMonthlyCost, analysis.currency)}</strong></div>
      <div class="metric-panel"><span>Proposed</span><strong>${formatMoney(analysis.proposedMonthlyCost, analysis.currency)}</strong></div>
      <div class="metric-panel"><span>Delta</span><strong>${formatMoney(analysis.monthlyDelta, analysis.currency, true)}</strong></div>
      <div class="metric-panel"><span>Budget</span><strong>${formatMoney(analysis.budgetLimitMonthly, analysis.currency)}</strong></div>
    </div>
    <div class="metric-grid">
      <div class="metric-panel"><span>Environment</span><strong>${escapeHtml(analysis.environment || "-")}</strong></div>
      <div class="metric-panel"><span>Confidence</span><strong>${escapeHtml(analysis.overallConfidence || "-")}</strong></div>
      <div class="metric-panel"><span>Unknown</span><strong>${analysis.unknownResourceCount}</strong></div>
      <div class="metric-panel"><span>Completed</span><strong>${formatDate(analysis.completedAt || analysis.startedAt)}</strong></div>
    </div>
    ${analysis.errorMessage ? `<div class="status-banner error">Failure reason: ${escapeHtml(analysis.errorMessage)}</div>` : ""}
    ${renderCostChanges(detail.costChanges, analysis.currency)}
    ${renderResources(detail.resources, analysis.currency)}
    ${renderArmMetadata(detail.resources, analysis.currency)}
    ${renderPricingMetadata(detail.resources)}
    ${renderAssumptions(detail.assumptions)}
    ${renderPolicyEvaluations(detail.policyEvaluations)}
    ${renderFindings(detail.policyFindings)}
    ${renderRecommendations(detail.recommendations, analysis.currency)}
    ${renderGithubMetadata(detail)}
    <h3>PR Comment</h3>
    <pre>${escapeHtml(detail.commentMarkdown)}</pre>
  `;
}

function renderCostChanges(changes, currency) {
  if (!changes || changes.length === 0) {
    return "";
  }

  return `
    <h3>Cost Changes</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Resource</th><th>Type</th><th>Action</th><th>Before</th><th>After</th><th>Delta</th></tr></thead>
      <tbody>${changes.map((change) => `
        <tr><td>${escapeHtml(change.terraformAddress || change.resourceName)}</td><td>${escapeHtml(change.resourceType)}</td><td>${escapeHtml(change.changeKind || "-")}</td><td>${escapeHtml(change.beforeSummary || change.beforeSku || "-")}</td><td>${escapeHtml(change.afterSummary || change.afterSku || "-")}</td><td>${formatMoney(change.monthlyDelta, currency, true)}</td></tr>`).join("")}</tbody>
    </table></div>`;
}

function renderResources(resources, currency) {
  if (!resources || resources.length === 0) {
    return "";
  }

  return `
    <h3>Resources</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Name</th><th>Terraform</th><th>Action</th><th>Type</th><th>SKU</th><th>Region</th><th>Pricing</th><th>Env</th><th>Monthly</th><th>Status</th><th>Confidence</th></tr></thead>
      <tbody>${resources.map((resource) => `
        <tr><td>${escapeHtml(resource.resourceName)}</td><td>${escapeHtml(resource.terraformAddress || "-")}</td><td>${escapeHtml(resource.terraformChangeType || resource.terraformActions || "-")}</td><td>${escapeHtml(resource.resourceType)}</td><td>${escapeHtml(resource.sku || resource.afterSummary || "-")}</td><td>${escapeHtml(resource.region || "-")}</td><td>${escapeHtml(resource.pricingMatchType || "-")}</td><td>${escapeHtml(resource.environment || "-")}</td><td>${formatMoney(resource.monthlyCost, currency)}</td><td>${escapeHtml(resource.status)}</td><td>${escapeHtml(resource.confidence || "-")}</td></tr>`).join("")}</tbody>
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
    <div class="list-panel">
      <div><strong>Catalog</strong><br>${escapeHtml(first.pricingCatalogName || "-")} ${escapeHtml(first.pricingCatalogVersion || "")}</div>
      <div><strong>Source</strong><br>${escapeHtml(first.pricingSourceType || first.pricingSource || "-")}</div>
      <div><strong>Live API Used</strong><br>${liveApiUsed ? "yes" : "no"}</div>
      <div><strong>Currency</strong><br>${escapeHtml(first.currency || "-")}</div>
      <div><strong>Region</strong><br>${escapeHtml(first.region || "-")}</div>
      <div><strong>Unit Price</strong><br>${first.pricingUnitPrice == null ? "-" : `${formatMoney(first.pricingUnitPrice, first.currency || "EUR")} / ${escapeHtml(unit)}`}</div>
      <div><strong>Monthly Conversion</strong><br>${escapeHtml((first.pricingMonthlyHours || first.hoursPerMonth) ? `${first.pricingMonthlyHours || first.hoursPerMonth} hours/month` : unit)}</div>
      <div><strong>Meter</strong><br>${escapeHtml(first.pricingMeterName || first.pricingMeterId || "-")}</div>
      <div><strong>Product</strong><br>${escapeHtml(first.pricingProductName || "-")}</div>
      <div><strong>SKU</strong><br>${escapeHtml(first.pricingArmSkuName || first.pricingSkuName || first.sku || "-")}</div>
      <div><strong>Match Quality</strong><br>${escapeHtml(matchTypes || "-")}</div>
      <div><strong>Fallback Used</strong><br>${fallbackUsed ? "yes" : "no"}</div>
      ${first.pricingEffectiveStartDate ? `<div><strong>Effective Start</strong><br>${formatDate(first.pricingEffectiveStartDate)}</div>` : ""}
      ${fallback ? `<div><strong>Fallback</strong><br>${escapeHtml(fallback.pricingFallbackReason)}</div>` : ""}
      ${ai ? `<div><strong>AI Model Pricing</strong><br>${escapeHtml(ai.sku || "-")} - ${escapeHtml(ai.pricingUnit || "1M tokens")}</div>` : ""}
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
          <strong>${escapeHtml(resource.resourceName)}</strong><br>
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
    return "<h3>Policy Findings</h3><div class=\"empty-state\">No policy findings.</div>";
  }

  return `
    <h3>Policy Findings</h3>
    <div class="list-panel">${findings.map((finding) => `
      <div><span class="pill ${cssToken(finding.action)}">${humanPolicy(finding.action)}</span> <strong>${escapeHtml(finding.ruleId)}</strong><br>${escapeHtml(finding.message)}</div>`).join("")}</div>`;
}

function renderAssumptions(assumptions) {
  if (!assumptions || assumptions.length === 0) {
    return "";
  }

  return `
    <h3>Assumptions</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Name</th><th>Value</th></tr></thead>
      <tbody>${assumptions.map((item) => `
        <tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.value)}</td></tr>`).join("")}</tbody>
    </table></div>`;
}

function renderPolicyEvaluations(evaluations) {
  if (!evaluations || evaluations.length === 0) {
    return "";
  }

  return `
    <h3>Policy Evaluations</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Rule</th><th>Result</th><th>Message</th></tr></thead>
      <tbody>${evaluations.map((item) => `
        <tr><td>${escapeHtml(item.ruleName)}</td><td>${escapeHtml(item.result)}</td><td>${escapeHtml(item.message)}</td></tr>`).join("")}</tbody>
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
    <div class="list-panel">
      <div><strong>PR</strong><br>${pr}</div>
      <div><strong>Report URL</strong><br>${report}</div>
      <div><strong>Publishing Status</strong><br><span class="pill ${cssToken(detail.reportPublishingStatus || "Pending")}">${escapeHtml(detail.reportPublishingStatus || "Pending")}</span></div>
      <div><strong>Comment ID</strong><br>${escapeHtml(detail.gitHubCommentId || "-")}</div>
      <div><strong>Check Run ID</strong><br>${escapeHtml(detail.gitHubCheckRunId || "-")}</div>
      ${detail.reportPublishingError ? `<div><strong>Publishing Error</strong><br>${escapeHtml(detail.reportPublishingError)}</div>` : ""}
    </div>`;
}

function renderRecommendations(recommendations, currency) {
  if (!recommendations || recommendations.length === 0) {
    return "";
  }

  return `
    <h3>Recommendations</h3>
    <div class="list-panel">${recommendations.map((rec) => `
      <div><strong>${escapeHtml(rec.title)}</strong><br>${escapeHtml(rec.description)} ${rec.estimatedMonthlySavings ? `Impact ${formatMoney(rec.estimatedMonthlySavings, currency)}.` : ""}</div>`).join("")}</div>`;
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
        <td>${escapeHtml(environment)}</td>
        <td><input data-budget-cost type="number" min="0" step="1" value="${inputValue(budget.maxMonthlyCost)}"></td>
        <td><input data-budget-delta type="number" min="0" step="1" value="${inputValue(budget.maxMonthlyDelta)}"></td>
        <td><input data-budget-approval type="number" min="0" step="1" value="${inputValue(budget.requireApprovalAbove)}"></td>
        <td><input data-budget-block type="checkbox" ${budget.blockOnBudgetExceeded ? "checked" : ""}></td>
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

  const sign = signed && value > 0 ? "+" : "";
  return `${sign}${currency || "EUR"} ${Number(value).toFixed(2)}`;
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

function cssToken(value) {
  return String(value || "").toLowerCase().replace(/[^a-z]/g, "");
}

function shortSha(value) {
  return value ? String(value).slice(0, 12) : "-";
}

function formatDate(value) {
  return value ? new Date(value).toLocaleString() : "-";
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
