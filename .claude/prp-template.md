# PRP Template

Use this template when generating PRPs with the `/prp` command.

---

# PRP: [Task Name]

## Objective
[One sentence: what we're building and why it matters for the assessment]

## Scope
**In scope:**
- [Feature 1]

**Out of scope:**
- [Explicitly excluded]

## Prerequisites
[What must exist before this task — prior PRPs, API keys, packages]

## Outputs
[What will exist when done — files, capabilities, endpoints]

## Files to Create/Modify
| File | Action | Description |
|------|--------|-------------|
| `path/to/file.cs` | Create | What it does |
| `path/to/existing.cs` | Modify | What changes |

## NuGet Packages Needed
| Package | Version | Project |
|---------|---------|---------|
| `PackageName` | x.y.z | AskPdf.Infrastructure |

## Implementation Steps
1. [Specific, actionable step with enough detail to code from]
2. [Next step]

## Patterns to Follow
[Reference existing code or define the pattern]

```csharp
// Example if helpful
```

## Risks
| Risk | Mitigation |
|------|------------|
| [What could go wrong] | [How to handle] |

## Validation
- [ ] [Specific testable check: "Upload a 5-page PDF, verify 10+ chunks created"]
- [ ] [Build: "dotnet build succeeds"]
- [ ] [Functional: "Ask a question, receive answer with page citation"]
