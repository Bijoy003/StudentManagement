# Autonomous Multi-Agent DevOps Pipeline Architecture (Generic Template)

This document outlines a **technology-agnostic** architecture, gate mechanism, and orchestration plan for an autonomous multi-agent DevOps pipeline driven by an LLM. The system uses a Master-Worker architecture, isolated per-task workspaces (Git Worktrees), and the Model Context Protocol (MCP) for direct tool execution.

> **How to use this template:** Replace every `<PLACEHOLDER>` with the values for your stack. The pipeline stages, gates, and agent roles stay the same regardless of language, framework, or tooling.

> **📝 Notation — Per-Ticket Run Documents:** This file is the **architecture spec**. Each pipeline run produces a separate **execution log** named `tickets/<TICKET_ID>-pipeline-run.md` (template: [`tickets/Ticket-1852-pipeline-run.md`](tickets/Ticket-1852-pipeline-run.md)). That per-ticket file records the **plan and execution details for every agent** (inputs, files changed, commands, gate verdicts, delivery). Store it **outside** the disposable worktree so it survives a branch teardown.
>
> **📝 Notation — Split Plan Placement:** Each ticket's **plan** is split so repo-specific detail travels with the code it changes: the **frontend plan** lives in `<FRONTEND_WORKSPACE>`, the **backend plan** in `<BACKEND_WORKSPACE>`, and a **common referencer** in the container root ties them together. Plans are **git-ignored by default** and delivered to a repo only when the **client explicitly requests** them — see **§7**.

## 1. System Architecture & Tech Stack

**Core Technologies**

* **Orchestration Engine:** Master script in `<ORCHESTRATION_LANGUAGE>` (handles API routing, workspace isolation, and state management). **Operator persona:** *Staff DevOps / Release Orchestration Engineer* — owns the run end-to-end, sequences agents, evaluates gates, and decides push vs. reject.
* **AI Engine:** LLM API with a tiered model set — `<FAST_MODEL>`, `<BALANCED_MODEL>`, `<REASONING_MODEL>`.
* **Ticket Source (Jira MCP):** The Orchestrator calls the Jira MCP tool with `<TICKET_ID>` to fetch ticket details, acceptance criteria, and linked requirements on each pipeline run. No webhook or JSON file drop required — the MCP tool provides a live query interface.
* **Frontend Workspace (`<FRONTEND_WORKSPACE>`):** `<FRONTEND_FRAMEWORK>` with the team's agreed conventions (typing, component model, state management).
* **Backend Workspace (`<BACKEND_WORKSPACE>`):** `<BACKEND_FRAMEWORK>` with the team's agreed architecture (layering, dependency injection, API standards).

> A pipeline may have one workspace or many. Add or remove workspace entries to match the repository.

**Tooling Integration**

* **Version-Control MCP:** Allows agents to directly read diffs, checkout branches, and commit changes.
* **Browser/E2E MCP:** Allows test agents to autonomously execute end-to-end tests and read UI states (omit if the project has no UI).

---

## 2. Pipeline Architecture & Gate Diagram

The pipeline is **strictly sequential** (Agent 1 → 2 → 3 → 4 → 5) — no two agents run at the same time — so a dedicated gate can sit **after every agent** as a synchronous checkpoint. Each gate is a state file (`gateN.*.json`) the Orchestrator writes and evaluates before releasing the next stage. A failed gate loops back to a **single owning agent**.

```text
               ┌──────────────────────────────────────────────┐
               │           TICKET SOURCE (Trigger)            │
               │      <ISSUE_TRACKER> Webhook / Jira MCP        │
               └──────────────────────────────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │        ORCHESTRATOR (<ORCH_LANGUAGE>)        │
               │   • Spawns isolated workspace(s) per ticket   │
               │   • Evaluates 5 state gates (gateN.*.json)   │
               │   • Routes each failure to its owning agent  │
               └──────────────────────────────────────────────┘
                                      │ create isolated workspace(s)
                                      ▼
  ═══════════════ INSIDE ISOLATED WORKSPACE (<WORKSPACE_DIRS>) ═══════════════

    ┌──────────────────────────────┐
    │ AGENT 1 · PRD Parser         │  Senior Business Analyst · <FAST_MODEL>
    │ → writes spec.json           │
    └──────────────┬───────────────┘
                   ▼
        ╔═════════════════════════════╗   [FAIL] ─▶ back to AGENT 1
        ║ GATE 1 · SPEC GATE          ║──────────────(or halt for human
        ║ spec valid · ACs testable   ║               if ticket ambiguous)
        ╚═════════════╤═══════════════╝
                      │ [PASS]
                      ▼
    ┌──────────────────────────────┐
    │ AGENT 2 · Test Planner       │  Senior SDET · <BALANCED_MODEL>
    │ → unit + E2E tests (RED)      │
    └──────────────┬───────────────┘
                   ▼
        ╔═════════════════════════════╗   [FAIL] ─▶ back to AGENT 2
        ║ GATE 2 · TEST-DESIGN GATE   ║──────────────
        ║ tests compile · RED · cover ║
        ║ every acceptance criterion  ║
        ╚═════════════╤═══════════════╝
                      │ [PASS]
                      ▼
    ┌──────────────────────────────┐
    │ AGENT 3 · Implementer        │  Principal Software Engineer · <REASONING_MODEL>
    │ → production source code      │
    └──────────────┬───────────────┘
                   ▼
        ╔═════════════════════════════╗   [FAIL] ─▶ back to AGENT 3
        ║ GATE 3 · BUILD GATE         ║──────────────
        ║ <BUILD_COMMANDS> exit 0     ║
        ║ lint / type-check clean     ║
        ╚═════════════╤═══════════════╝
                      │ [PASS]
                      ▼
    ┌──────────────────────────────┐
    │ AGENT 4 · Self-Healer        │  Staff SRE · <REASONING_MODEL>
    │ → run tests, patch, loop      │◀──────────┐ self-heal loop
    └──────────────┬───────────────┘           │
                   ▼                            │
        ╔═════════════════════════════╗  [FAIL]─┘
        ║ GATE 4 · TEST GATE          ║
        ║ <TEST_COMMANDS> 100% pass    ║
        ║ · no regressions            ║
        ╚═════════════╤═══════════════╝
                      │ [PASS]
                      ▼
    ┌──────────────────────────────┐
    │ AGENT 5 · PR Reviewer        │  Principal Architect · <REASONING_MODEL>
    │ → architecture audit via MCP  │
    └──────────────┬───────────────┘
                   ▼
        ╔═════════════════════════════╗  [REJECT] ─▶ validation report
        ║ GATE 5 · ARCHITECTURE GATE  ║─────────────── back to AGENT 3
        ║ backend + frontend arch     ║
        ║ standards · naming · reuse  ║
        ╚═════════════╤═══════════════╝
                      │ [PASS — all 5 gates green]
                      ▼
            ┌───────────────────────┐
            │   push branch +       │
            │   open pull request   │
            └───────────────────────┘

  Note: if the project spans multiple independent repos, GATE 3 and GATE 4 build/test
  those repos IN PARALLEL — inter-agent flow is sequential, intra-gate work is not.
```

---

## 3. Agent Roster & Model Selection

Each agent is given a specific system prompt tailored to its role, and **operates as a named senior persona** so it applies the judgment and standards of the highest role that performs that task best. Models are selected based on the complexity of reasoning required versus execution speed. Map the model tiers to whatever provider/models your team uses. The personas are stack-agnostic — keep them as-is.

| Agent | Role | Operating Persona (Seniority) | Target Workspace | Model Tier | Core Responsibility |
| --- | --- | --- | --- | --- | --- |
| **Orchestrator** | Operator | **Staff DevOps / Release Orchestration Engineer** | System Level | — | Sequences agents, evaluates gates, decides push vs. reject, owns the run. |
| **Agent 1** | PRD Parser | **Senior Business Analyst (Requirements Analyst)** | System Level | **Fast tier** (`<FAST_MODEL>`) | Parses incoming issues or PRD tickets and updates internal context specifications on disk. |
| **Agent 2** | Test Planner | **Senior SDET / QA Automation Architect** | All code workspaces | **Balanced tier** (`<BALANCED_MODEL>`) | Writes unit test scaffolds and E2E test files **before** code implementation. |
| **Agent 3** | Implementer | **Principal Software Engineer** | All code workspaces | **Reasoning tier** (`<REASONING_MODEL>`) | Writes the actual logic, then runs `<FORMAT_COMMANDS>` to auto-fix style before Gate 3. Built for heavy long-horizon coding tasks. |
| **Agent 4** | Self-Healer | **Staff Build & Reliability Engineer (SRE)** | All code workspaces | **Reasoning tier** (`<REASONING_MODEL>`) | Executes build engines, parses compiler/test failures, uses MCP tools to debug and adjust code natively. |
| **Agent 5** | PR Reviewer | **Principal Software Architect** | System Level | **Reasoning tier** (`<REASONING_MODEL>`) | High-level architectural auditor. Inspects structural code standards using VCS MCP before publishing to origin. |

> **Why personas matter:** each agent's system prompt opens with *"You are a &lt;persona&gt;…"*. Anchoring to the highest-competent seniority raises the bar for output quality, review strictness, and design judgment at every stage.

> **Model diversity (Agent 3 vs. Agent 5):** Agent 3 (Implementer) and Agent 5 (PR Reviewer) both use `<REASONING_MODEL>` by default, but a reviewer using the same model as the implementer shares the same blind spots. Consider using **different model families or providers** for Agent 5 than Agent 3 — a distinct reasoning perspective catches issues a larger version of the same model would miss. Override via `<REVIEW_MODEL>` if desired.

---

## 4. The Gate System

Gates act as automated validation barriers managed by the Orchestrator loop.

### 4.0 Feasibility: one gate per agent

**Verdict: feasible, and recommended.** Because the agents run **strictly sequentially with no inter-agent parallelism**, the Orchestrator can insert a synchronous checkpoint after each agent without any concurrency complications. Each gate reads the previous agent's artifact, writes a `gateN.*.json` verdict, and either releases the next agent or loops back.

| Aspect | Assessment |
| --- | --- |
| **Concurrency risk** | None — one agent active at a time; a gate is a barrier between two stages. |
| **Benefit** | **Fail fast, fail cheap.** A bad spec is caught at Gate 1 (fast-tier cost) instead of after the reasoning model writes code. Each failure routes to exactly one owning agent, so loop-backs are precise. |
| **Cost / risk** | More checkpoints ⇒ more potential loop-back cycles (tokens + latency). Gates 1–2 are partly qualitative, so they need a validator (Agent 5 assist). Over-gating could bounce work between Gate 3 (build) and Gate 4 (test). |
| **Mitigations** | Per-gate **max-retry counter** with escalation to a human on exhaustion; **single owning agent** per gate; keep evaluators deterministic where possible (exit codes) and reserve LLM judgment for Gates 1, 2, 5. |
| **Parallelism note** | Inter-agent flow is sequential. If the project spans multiple independent repos, **Gate 3 and Gate 4 build/test those repos in parallel** — the only concurrency in the pipeline. |

Each gate writes a state file the Orchestrator polls: `gate1.spec.json`, `gate2.testdesign.json`, `gate3.build.json`, `gate4.test.json`, `gate5.architecture.json`.

---

### Gate 1 — Spec Gate  ·  owner: Agent 1 (PRD Parser)

* **Evaluator:** Orchestrator JSON-schema validation + Agent 5 (PR Reviewer) lightweight scan via VCS MCP. **The spec author never validates their own spec** — a separate agent reviews for blind-spot detection.
* **Conditions:** `spec.json` is valid and complete; every acceptance criterion is present and **testable**; affected areas/workspaces are identified; no unresolved ambiguity.
* **Failure Actions:** Loop back to **Agent 1** to re-parse; if the ticket itself is ambiguous, halt and escalate to a human via `<ESCALATION_CHANNEL>`. The workspace is retained for `<WORKSPACE_RETENTION_HOURS>` hours. → `gate1.spec.json`

### Gate 2 — Test-Design Gate  ·  owner: Agent 2 (Test Planner)

* **Evaluator:** Orchestrator CLI Runner + acceptance-criteria coverage check.
* **Conditions:** test suites **compile/parse**; **every acceptance criterion maps to ≥1 test**; the new tests run **RED** against the un-implemented feature (TDD proof); no ambient/flaky dependencies.
* **Failure Actions:** Loop back to **Agent 2** to add/repair tests. → `gate2.testdesign.json`

### Gate 3 — Build Gate  ·  owner: Agent 3 (Implementer)

* **Evaluator:** Orchestrator CLI Runner (exit codes `0` vs non-zero). *(Multiple repos build in parallel.)*
* **Conditions:** all `<BUILD_COMMANDS>` complete with zero errors; `<SECURITY_SCAN_COMMANDS>` exit 0 (or skip if placeholder is empty); type-check and lint are clean. **No test execution yet** — this gate isolates "does it compile" from "does it pass."
* **Failure Actions:** Compiler/lint dumps routed to **Agent 3** (build-only breakages may be delegated to Agent 4's self-heal). → `gate3.build.json`

### Gate 4 — Test Gate  ·  owner: Agent 4 (Self-Healer)

* **Evaluator:** Orchestrator CLI Runner (exit codes). *(Multiple repos test in parallel.)*
* **Conditions:** all `<TEST_COMMANDS>` execute with **100% pass**; the previously-RED tests from Gate 2 are now **GREEN**; **no regressions** in the existing suites. For multi-workspace projects, also run `<INTEGRATION_TEST_COMMANDS>` to validate cross-workspace contracts (API schemas, shared types, etc.).
* **Failure Actions:** Stack traces handed to **Agent 4**, which patches source inside the workspace and loops back through build → test (self-heal loop) until green or the retry cap trips. → `gate4.test.json`

### Gate 5 — Architecture Gate  ·  owner: Agent 5 (PR Reviewer)

* **Evaluator:** **Agent 5 (PR Reviewer)** using **VCS MCP** (qualitative).
* **Conditions:** strict enforcement of backend architecture/design patterns/naming and frontend architecture/component-model/state-management conventions; security/reuse sanity.
* **Failure Actions:** PR locally rejected; feedback compiled into a validation report routed back to **Agent 3 (Implementer)** for refactoring (which re-enters at Agent 3 → Gate 3 → Gate 4 → Agent 5 → Gate 5). For build-only violations, the Orchestrator may route directly to Agent 3 → Gate 3 (build short-circuit) per `<ARCH_FAILURE_ROUTING>` setting. → `gate5.architecture.json`

---

## 5. Deployment Delivery

Once **all five gates** report a green status:

1. **VCS Synchronization:** The local isolated workspace changes are finalized.
2. **Remote Push:** The Orchestrator pushes the branch to the remote repository (`push origin <branch-name>`).
3. **PR Submission:** The Orchestrator opens a pull request via the platform CLI/API for final review or immediate release.

---

## 6. Workspace Isolation & Cleanup

> **Layout note:** Determine your repo topology first. A project may be a **single repo** (one worktree per ticket) or a **multi-repo container** where each code workspace is its own git repo (**one worktree per repo, per ticket**). If a container folder holds several repos, that container is typically *not* itself a repo — planning docs placed there are tracked by none of them (no pollution).

### 6.1 Directory Layout

Worktrees must live **outside** every repo's working tree, in a disposable `.worktrees/` folder. **Never nest a worktree inside its own repo.**

```text
<CONTAINER_ROOT>/
├── .worktrees/                   ← all disposable per-ticket work lives here
│   └── <TICKET_ID>/
│       ├── <WORKSPACE_A>/        ← worktree of repo A
│       └── <WORKSPACE_B>/        ← worktree of repo B (if multi-repo)
├── <PLAN_DOCS>.md                ← docs (tracked by no repo)
└── <WORKSPACE_A>/  <WORKSPACE_B>/ ← pristine primary repos, never touched
```

**Why:** the primary repos stay pristine, and a corrupted branch is fully disposable — nothing leaks into the source repos.

### 6.2 Spawn (Orchestrator, per ticket)

```bash
# Repeat once per code repo the ticket touches
git -C <WORKSPACE_A> worktree add -b ticket/<TICKET_ID> ../.worktrees/<TICKET_ID>/<WORKSPACE_A>
git -C <WORKSPACE_B> worktree add -b ticket/<TICKET_ID> ../.worktrees/<TICKET_ID>/<WORKSPACE_B>
```

### 6.3 Teardown (corrupted branch or after merge)

```bash
git -C <WORKSPACE_A> worktree remove --force ../.worktrees/<TICKET_ID>/<WORKSPACE_A>
git -C <WORKSPACE_A> branch -D ticket/<TICKET_ID>
git -C <WORKSPACE_B> worktree remove --force ../.worktrees/<TICKET_ID>/<WORKSPACE_B>
git -C <WORKSPACE_B> branch -D ticket/<TICKET_ID>
rm -rf .worktrees/<TICKET_ID>
```

### 6.4 Pollution Guardrails

* **Keep docs where no repo tracks them** — the container root, or a `.claude/docs/` folder that is git-ignored.
* **Audit each repo's `.gitignore` for `*.md`** before dropping docs inside it — a repo that does *not* ignore `*.md` will commit them.
* **Defensive insurance:** add `.worktrees/` to **every** repo's `.gitignore`, so a worktree never gets committed even if a script runs from inside a repo.

---

## 7. Per-Ticket Plan Documents — Placement, Ignore & Client Delivery

A ticket's plan is **split by repo** so repo-specific detail lives inside the repo it changes, with a lightweight **common referencer** in the container root that links everything. Plans stay **local (git-ignored) by default**, and are delivered into a repo **only when the client explicitly asks**.

### 7.1 Placement

| Document | Location | Tracked by git? |
| --- | --- | --- |
| **Frontend plan** | `<FRONTEND_WORKSPACE>/docs/plans/<TICKET_ID>-frontend-plan.md` | git-ignored by default |
| **Backend plan** | `<BACKEND_WORKSPACE>/docs/plans/<TICKET_ID>-backend-plan.md` | git-ignored by default |
| **Common referencer** (index + links to both plans + run log) | `<CONTAINER_ROOT>/tickets/<TICKET_ID>-plan.md` | never — the container root is not a git repo |

> **Single-repo projects:** collapse the two rows into one `<WORKSPACE_A>/docs/plans/<TICKET_ID>-plan.md`; the common referencer is optional.

**Rationale**
* Repo-specific plans travel with the code they describe — each developer sees the plan for the repo they work in.
* The common referencer holds cross-cutting context and links both slices + the `<TICKET_ID>-pipeline-run.md` execution log. It lives in the container root, which is **not** a git repo, so it can never leak into either repo.

### 7.2 Ignored by default

Each repo's plan folder is git-ignored so routine pipeline runs never pollute the repos:

```gitignore
# In EACH code repo's .gitignore
docs/plans/
```

> Confirm the effect per repo: a repo that already ignores `*.md` covers these implicitly, but add the explicit line anyway for clarity. A repo that does **not** ignore `*.md` **requires** this line.

### 7.3 Delivering a plan on explicit client request

When — and only when — the client explicitly asks for a ticket's plan, deliver **just that ticket's file** with a forced add. Once a file is tracked, the ignore rule no longer applies to it:

```bash
git -C <FRONTEND_WORKSPACE> add -f docs/plans/<TICKET_ID>-frontend-plan.md
git -C <FRONTEND_WORKSPACE> commit -m "docs(<TICKET_ID>): add frontend implementation plan (client requested)"

git -C <BACKEND_WORKSPACE> add -f docs/plans/<TICKET_ID>-backend-plan.md
git -C <BACKEND_WORKSPACE> commit -m "docs(<TICKET_ID>): add backend implementation plan (client requested)"
```

Then push with the ticket branch so each plan rides in its repo's pull request.

### 7.4 Best-practice notes

* **Per-ticket, never blanket.** Force-add the specific file; do **not** un-ignore the whole `docs/plans/` folder — that would leak every other ticket's local plan.
* **Auditability.** The `(client requested)` commit message records *why* the plan was published.
* **Container referencer isn't git-deliverable.** The container root is not a repo, so `tickets/<TICKET_ID>-plan.md` can't be pushed. If the client wants one combined document, generate it on demand by concatenating the repo plans, or deliver each repo's plan in its own PR.
* **Durable alternative to `add -f`.** Instead of a forced add, record an explicit negation in the repo's `.gitignore` — visible in history and it survives future edits:
  ```gitignore
  !docs/plans/<TICKET_ID>-frontend-plan.md
  ```

---

## Appendix: Placeholder Reference

| Placeholder | Meaning | Example |
| --- | --- | --- |
| `<ORCHESTRATION_LANGUAGE>` | Language of the master orchestrator script | Python, Node.js, Go |
| `<FAST_MODEL>` / `<BALANCED_MODEL>` / `<REASONING_MODEL>` | Model tiers for speed vs. reasoning | small / mid / frontier model |
| `<ISSUE_TRACKER>` | Ticket source system — Jira MCP is the primary integration; the Orchestrator calls Jira MCP with `<TICKET_ID>` to fetch details live | Jira (GitHub Issues, Linear) |
| `<FRONTEND_WORKSPACE>` / `<BACKEND_WORKSPACE>` | Workspace directory names | `web/`, `api/` |
| `<FRONTEND_FRAMEWORK>` / `<BACKEND_FRAMEWORK>` | Frameworks per workspace | any SPA / any server framework |
| `<WORKSPACE_DIRS>` | Directories present in the isolated workspace | `web/ & api/` |
| `<BUILD_COMMANDS>` | Commands that must exit 0 to pass Gate 3 | build/compile commands |
| `<TEST_COMMANDS>` | Commands that must pass 100% for Gate 4 | unit + E2E test commands |
| `<SECURITY_SCAN_COMMANDS>` | Security scanning for Gate 3 (leave empty to skip) | `trivy scan`, `npm audit` |
| `<INTEGRATION_TEST_COMMANDS>` | Cross-workspace contract validation for Gate 4 | `./validate-contracts.sh` |
| `<FORMAT_COMMANDS>` | Auto-formatting commands run by Agent 3 before Gate 3 | `dotnet format`, `prettier --write` |
| `<REVIEW_MODEL>` | Optional override model for Agent 5 (PR Reviewer) | frontier model different from `<REASONING_MODEL>` |
| `<ESCALATION_CHANNEL>` | Notification channel when pipeline halts on ambiguous ticket | Slack webhook, email, issue comment |
| `<WORKSPACE_RETENTION_HOURS>` | Hours to retain workspace during human-escalation halt | `48` |
| `<ARCH_FAILURE_ROUTING>` | Gate 5 failure re-entry strategy | `full-cycle` or `build-short-circuit` |
| `<CONTAINER_ROOT>` | Folder holding the repo(s) and `.worktrees/` | project root |
| `<WORKSPACE_A>` / `<WORKSPACE_B>` | Per-repo working-tree dirs (one per git repo) | `web/`, `api/` |
| `<TICKET_ID>` | Unique id for the ticket / branch | `PROJ-123` |
| `<PLAN_DOCS>` | Planning/architecture markdown files | this document |
