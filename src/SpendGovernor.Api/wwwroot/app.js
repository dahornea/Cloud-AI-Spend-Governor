const state = {
  email: localStorage.getItem("spendgov.email") || "demo@spendgov.local",
  view: "overview",
  workspaces: [],
  projects: [],
  selectedWorkspaceId: null,
  selectedProjectId: null,
  projectDetail: null,
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

async function loadWorkspaces() {
  try {
    state.workspaces = await api("/api/workspaces");
    state.selectedWorkspaceId = state.workspaces[0]?.id || null;
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

  state.projects = await api(`/api/workspaces/${state.selectedWorkspaceId}/projects`);
  state.selectedProjectId = state.projects[0]?.id || null;
  renderProjectSelect();
  await loadProjectDetail();
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
  await Promise.all([loadPolicies(), loadApprovals(), loadAudit()]);
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
  $("latestAnalysesBody").innerHTML = `<tr><td colspan="9">No project available.</td></tr>`;
  $("analysesBody").innerHTML = "";
  $("analysisDetail").innerHTML = "";
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
  $("metricRepo").textContent = `${project.repositoryOwner}/${project.repositoryName}`;
  $("latestAnalysesBody").innerHTML = tableRows(metrics.latestAnalyses);
  showView(state.view);
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
      <thead><tr><th>Resource</th><th>Type</th><th>SKU</th><th>Delta</th></tr></thead>
      <tbody>${changes.map((change) => `
        <tr><td>${escapeHtml(change.resourceName)}</td><td>${escapeHtml(change.resourceType)}</td><td>${escapeHtml(change.beforeSku || "-")} -> ${escapeHtml(change.afterSku || "-")}</td><td>${formatMoney(change.monthlyDelta, currency, true)}</td></tr>`).join("")}</tbody>
    </table></div>`;
}

function renderResources(resources, currency) {
  if (!resources || resources.length === 0) {
    return "";
  }

  return `
    <h3>Resources</h3>
    <div class="table-wrap"><table>
      <thead><tr><th>Name</th><th>Type</th><th>SKU</th><th>Env</th><th>Monthly</th><th>Status</th><th>Confidence</th></tr></thead>
      <tbody>${resources.map((resource) => `
        <tr><td>${escapeHtml(resource.resourceName)}</td><td>${escapeHtml(resource.resourceType)}</td><td>${escapeHtml(resource.sku || "-")}</td><td>${escapeHtml(resource.environment || "-")}</td><td>${formatMoney(resource.monthlyCost, currency)}</td><td>${escapeHtml(resource.status)}</td><td>${escapeHtml(resource.confidence || "-")}</td></tr>`).join("")}</tbody>
    </table></div>`;
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
  if (!detail.gitHubCommentId && !detail.gitHubPullRequestUrl) {
    return "";
  }

  const pr = detail.gitHubPullRequestUrl
    ? `<a href="${escapeHtml(detail.gitHubPullRequestUrl)}" target="_blank" rel="noreferrer">${escapeHtml(detail.gitHubPullRequestUrl)}</a>`
    : "-";
  return `
    <h3>GitHub</h3>
    <div class="list-panel">
      <div><strong>PR</strong><br>${pr}</div>
      <div><strong>Comment ID</strong><br>${escapeHtml(detail.gitHubCommentId || "-")}</div>
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

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
