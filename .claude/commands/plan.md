# /plan — Generate Implementation Plan

Generate a step-by-step implementation plan for the Document Q&A application (or a specific feature).

## Process

1. Read CLAUDE.md for project state, tech stack, and constraints
2. Assess what already exists (scan the codebase)
3. Identify what needs to be built
4. Break into ordered steps mapped to the priority list in CLAUDE.md
5. Estimate complexity (S/M/L) per step

## Output Format

```markdown
# Implementation Plan: [Feature/Full Project]

## Current State
[What exists now]

## Goal
[What we're building toward]

## Steps

### Phase 1: Foundation (Day 1)
- [ ] Step 1 (S/M/L) — description
- [ ] Step 2 (S/M/L) — description

### Phase 2: Core RAG (Day 2)
- [ ] Step 3 ...

### Phase 3: UI + Integration (Day 3)
- [ ] Step 4 ...

### Phase 4: Polish + Deliver (Day 4)
- [ ] Step 5 ...

## Risks
- [Risk]: mitigation
```

## Rules

- Each step should be completable in 1–3 hours
- Steps must be in dependency order
- Map phases to the 4-day timeline
- Every step must contribute to a working demo
- Don't plan features not in the requirements
- Flag any blocking dependencies (API keys, etc.)
