# RAG Evaluation System - Implementation Plan

## Overview
Build a comprehensive RAG evaluation framework for the StudentManagement AI chat system to measure and improve response quality through systematic testing, metrics, and prompt regression testing.

---

## Phase 1: Core RAG Evaluation Infrastructure

### 1.1 Evaluation Data Models
- [ ] Create `EvaluationQuestion` record (question, expectedAnswer, expectedContext, difficulty, category)
- [ ] Create `EvaluationResult` record (questionId, generatedAnswer, retrievedContexts, metrics, timestamp)
- [ ] Create `EvaluationMetrics` record (relevance, groundedness, correctness, hallucination, latencyMs)
- [ ] Create `EvaluationRun` record (runId, promptVersion, config, summaryMetrics, startedAt, completedAt)

### 1.2 Evaluation Dataset
- [ ] Create `EvaluationDataset` class with 20-50 golden test questions
- [ ] Include StudentManagement-specific questions:
  - Student CRUD operations
  - Course/Enrollment queries
  - Chat tool capabilities
  - RAG document queries
- [ ] Include edge cases:
  - Out-of-scope questions ("What is the company HQ?")
  - Ambiguous queries
  - Negative tests ("Tell me something not in docs")
  - Multi-hop reasoning questions
- [ ] Store as JSON/embedded resource for version control

### 1.3 Evaluation Engine
- [ ] Create `IEvaluationService` interface
- [ ] Implement `EvaluationService` with:
  - `RunEvaluationAsync(EvaluationDataset, EvaluationConfig)`
  - `RunSingleQuestionAsync(question, config)`
  - `CompareRunsAsync(runId1, runId2)`
- [ ] Integrate with existing `ChatService` and `AppKnowledgeService`
- [ ] Support configurable retrieval (k chunks, similarity threshold)

---

## Phase 2: Metrics Implementation

### 2.1 Deterministic Metrics
- [ ] **Relevance**: Cosine similarity between question embedding and answer embedding
- [ ] **Groundedness**: Overlap between answer and retrieved contexts (token/phrase overlap)
- [ ] **Context Recall**: % of expected context found in retrieved chunks
- [ ] **Context Precision**: % of retrieved chunks that are relevant

### 2.2 LLM-as-a-Judge Metrics
- [ ] Create `ILlmJudgeService` interface
- [ ] Implement judge prompts for:
  - **Correctness**: Does answer match expected answer? (1-5 scale)
  - **Hallucination**: Does answer contain info not in context? (boolean + confidence)
  - **Completeness**: Does answer address all parts of question? (1-5 scale)
  - **Tone/Style**: Is response appropriate for student management domain?
- [ ] Support multiple judge models (Ollama, OpenAI-compatible)
- [ ] Cache judge results for reproducibility

### 2.3 Composite Scoring
- [ ] Weighted aggregate score (configurable weights)
- [ ] Pass/fail thresholds per metric
- [ ] Category-specific thresholds (factual vs conversational)

---

## Phase 3: Prompt Versioning & Regression Testing

### 3.1 Prompt Management
- [ ] Create `PromptTemplate` class with version, template, variables
- [ ] Store prompt versions in code/config (not just in prompt string)
- [ ] Current prompts to version:
  - System prompt (RAG + tools + docs)
  - Tool descriptions
  - Follow-up/clarification prompts

### 3.2 Prompt Experiments
- [ ] Create `PromptExperiment` class:
  - Name, description, baselinePromptId, variantPromptId
  - Dataset filter (run on subset)
  - Metrics to track
- [ ] Implement A/B testing framework
- [ ] Statistical significance testing (t-test, bootstrap CI)

### 3.3 Regression Detection
- [ ] Compare current run vs baseline run
- [ ] Flag regressions: any metric drops > threshold (e.g., 5%)
- [ ] Generate regression report with:
  - Metric deltas
  - Failing questions (new failures)
  - Improved questions
  - Statistical significance

---

## Phase 4: Integration & Automation

### 4.1 CLI / API Endpoints
- [ ] `dotnet run --project StudentManagement -- evaluate` - run full evaluation
- [ ] `dotnet run --project StudentManagement -- evaluate:compare <runId1> <runId2>`
- [ ] REST endpoint: `POST /api/evaluation/run` (admin only)
- [ ] REST endpoint: `GET /api/evaluation/runs` - history
- [ ] REST endpoint: `GET /api/evaluation/runs/{id}/report` - detailed report

### 4.2 CI/CD Integration
- [ ] GitHub Action: run evaluation on PR to `develop`
- [ ] Fail build if regression detected on critical metrics
- [ ] Publish evaluation report as artifact
- [ ] Comment PR with summary

### 4.3 Dashboard / Reporting
- [ ] Generate HTML report with:
  - Summary table (metrics per category)
  - Per-question breakdown
  - Prompt version comparison charts
  - Trend over time (if multiple runs stored)
- [ ] Export to JSON/CSV for external analysis

---

## Phase 5: Advanced Features (Future)

### 5.1 Synthetic Data Generation
- [ ] Use LLM to generate additional test questions from documents
- [ ] Adversarial question generation

### 5.2 Continuous Evaluation
- [ ] Scheduled evaluation runs (nightly)
- [ ] Alert on metric drift
- [ ] Integration with monitoring (Grafana/Prometheus)

### 5.3 Human-in-the-loop
- [ ] UI for manual review of failed cases
- [ ] Feedback collection for RLHF-style improvement

---

## Technical Design Notes

### Architecture
```
StudentManagement.Application/
  Evaluation/
    Interfaces/
      IEvaluationService.cs
      ILlmJudgeService.cs
      IEvaluationDatasetProvider.cs
    Models/
      EvaluationQuestion.cs
      EvaluationResult.cs
      EvaluationMetrics.cs
      EvaluationRun.cs
      PromptTemplate.cs
      PromptExperiment.cs
    Services/
      EvaluationService.cs
      LlmJudgeService.cs
      EvaluationDatasetProvider.cs
      PromptManager.cs
    Datasets/
      StudentManagementDataset.cs (embedded resource)
```

### Dependencies
- Existing: `Microsoft.Extensions.AI`, `ChatService`, `AppKnowledgeService`
- New: `Microsoft.ML.Tokenizers` (for token overlap metrics), `System.Text.Json` (serialization)

### Configuration
```json
"Evaluation": {
  "Enabled": true,
  "DefaultPromptVersion": "v1",
  "JudgeModel": "qwen2.5:3b-instruct",
  "Metrics": {
    "RelevanceWeight": 0.25,
    "GroundednessWeight": 0.30,
    "CorrectnessWeight": 0.25,
    "HallucinationWeight": 0.20
  },
  "Thresholds": {
    "RelevanceMin": 0.7,
    "GroundednessMin": 0.8,
    "HallucinationMax": 0.1
  }
}
```

---

## Acceptance Criteria

### Phase 1-2 Complete
- [ ] Can run `dotnet run -- evaluate` and get metrics for all test questions
- [ ] Evaluation dataset has ≥20 questions covering core domain + edge cases
- [ ] All 4 metrics (relevance, groundedness, correctness, hallucination) computed
- [ ] Results persisted and queryable

### Phase 3 Complete
- [ ] Can define prompt versions and run A/B comparison
- [ ] Regression detection flags metric drops >5%
- [ ] Comparison report shows per-question deltas

### Phase 4 Complete
- [ ] CI runs evaluation on PR
- [ ] Build fails on regression
- [ ] Report published as artifact

---

## Effort Estimate

| Phase | Tasks | Estimate |
|-------|-------|----------|
| 1: Core Infrastructure | 8 tasks | 3-4 days |
| 2: Metrics | 7 tasks | 3-4 days |
| 3: Prompt Versioning | 6 tasks | 2-3 days |
| 4: Integration | 5 tasks | 2-3 days |
| **Total** | **26 tasks** | **10-14 days** |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| LLM judge inconsistency | Use multiple judges, temperature=0, cache results |
| Evaluation slow (many LLM calls) | Parallel execution, async, caching |
| Metrics don't correlate with user satisfaction | Validate with human eval on sample, iterate |
| Prompt changes break tools | Include tool-calling questions in dataset |

---

## Next Steps

1. ✅ Create feature branch (`feature/rag-evaluation-system`)
2. ✅ Create this plan file
3. → Start Phase 1: Implement data models and dataset provider
4. → Write tests first (TDD) for evaluation engine
5. → Implement deterministic metrics
6. → Implement LLM judge
7. → Build prompt versioning
8. → Add CLI/CI integration