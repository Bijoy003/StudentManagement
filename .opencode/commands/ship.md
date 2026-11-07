---
description: Run the full agent lifecycle (spec → RED → GREEN → heal → review) for a task via the orchestrator
agent: orchestrator
---

Working tree state:

!`git status --short && git branch --show-current`

Run the orchestrator workflow for this task: $ARGUMENTS

Start at INTAKE: classify the task, draft the numbered acceptance criteria, and confirm them with me before delegating to test-planner. If the working tree above shows unrelated dirty files, ask me how to handle them before any stage relies on `git diff`.
