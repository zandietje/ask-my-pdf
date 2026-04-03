# /prp — Generate Project Requirement Plan

Generate a focused PRP for the next piece of work. A PRP gives Claude all context needed to implement a feature in one pass.

## Arguments

$TASK_DESCRIPTION: Description of the task to plan. If empty, determine the next logical task from the implementation plan or current state.

## Process

1. Read CLAUDE.md for constraints and architecture
2. Scan existing codebase to understand current state
3. Identify the specific task (from argument or next logical step)
4. Research existing patterns in the codebase to follow
5. Generate the PRP using the template from `.claude/prp-template.md`

## Output

Save to: `PRPs/{task-name}.md`

## Rules

- One feature or component per PRP — keep it focused
- Implementation steps must be specific enough to code from directly
- Always include validation criteria
- Reference existing code patterns when available
- Include the exact NuGet packages and versions needed
- Include specific file paths for files to create/modify
