# /validate — Run Validation Gates

Run build, tests, and quality checks to verify the project is in a good state.

## Process

Execute in order, stop on first failure:

1. **Format**: `dotnet format AskPdf.sln --verify-no-changes` (if sln exists)
2. **Build**: `dotnet build AskPdf.sln` (or project file if no sln yet)
3. **Tests**: `dotnet test AskPdf.sln` (if test project exists)
4. **RAG Pipeline Check** (if implemented):
   - Chunk metadata includes page_number and document_name? (grep for these fields)
   - Grounding prompt includes "ONLY" or "only from" instruction? (grep)
   - Response model includes sources array? (grep)
   - "I don't know" / "could not find" handling exists? (grep)
5. **Assessment Readiness** (if nearing completion):
   - README.md exists and has setup instructions?
   - API keys in appsettings.json / user secrets (not hardcoded)?
   - App starts without errors? (`dotnet run` quick check)

## Output Format

```markdown
## Validation Results

| Gate | Status | Details |
|------|--------|---------|
| Format | ✅/❌ | |
| Build | ✅/❌ | |
| Tests | ✅/❌/⏭️ | X passed, Y failed |
| RAG Pipeline | ✅/❌/⏭️ | |
| Assessment Ready | ✅/❌/⏭️ | |

### Action Required
[What needs fixing]
```
