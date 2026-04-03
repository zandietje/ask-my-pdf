# /review — Review Code or Approach

Perform a focused review of recent changes, a specific file, or the overall approach.

## Arguments

$TARGET: What to review. Can be:
- A file path → review that file
- `recent` → review git diff (staged + unstaged)
- `approach` → review overall architecture and project structure
- `rag` → review RAG pipeline (chunking, retrieval, prompt, citations)
- `demo` → full assessment readiness check

## Process

1. Read CLAUDE.md for constraints and evaluation criteria
2. Gather context based on target
3. Evaluate against the reviewer agent's checklist
4. Generate actionable feedback prioritized by assessment impact

## Output Format

```markdown
## Review: [Target]

**Verdict**: ✅ / ⚠️ / ❌

### Issues (by severity)
1. [🔴/🟡/🟢] Issue → Fix

### Simplifications
1. What → How

### Assessment Readiness
- Does it work? [Y/N]
- Source citations accurate? [Y/N]
- UI polished? [Y/N]
- README complete? [Y/N]
```

## Rules

- Be direct — prioritize by demo impact
- Don't nitpick formatting
- Suggest concrete fixes, not vague improvements
- For `demo` target, simulate an assessor's perspective
