# PRP: Phase 1 — Project Scaffold

## Objective
Create the full project structure (3 .NET projects + React frontend) so `dotnet build` and `npm run dev` both succeed, establishing the foundation for all subsequent phases.

## Scope
**In scope:**
- .NET solution with 3 projects (Web, Core, Infrastructure) + test project
- Directory.Build.props for shared settings
- Core domain models (used by all subsequent phases)
- Skeleton Program.cs with DI registration placeholders
- React 18 + Vite + TypeScript frontend with Tailwind + shadcn/ui
- Vite proxy config for `/api` → .NET backend
- .gitignore for .NET + Node
- Git init + first commit

**Out of scope:**
- Actual endpoint implementations (Phase 2-3)
- SQLite database setup (Phase 2)
- Claude API integration (Phase 3)
- Any UI beyond "Hello World" shell (Phase 4)
- PDF viewer (Phase 5)

## Prerequisites
- .NET 8 SDK installed
- Node.js 18+ installed
- No API keys needed for this phase

## Outputs
When done, the following will work:
- `dotnet build AskMyPdf.sln` — compiles with zero errors
- `dotnet test AskMyPdf.sln` — runs (trivially, with a placeholder test)
- `dotnet run --project src/AskMyPdf.Web/AskMyPdf.Web.csproj` — starts on port 5000
- `cd client && npm install && npm run dev` — starts Vite dev server with API proxy
- Core domain models are compiled and ready for use
- Git repo initialized with first commit

## Files to Create/Modify

### Root
| File | Action | Description |
|------|--------|-------------|
| `AskMyPdf.sln` | Create | Solution file referencing all 4 projects |
| `Directory.Build.props` | Create | Shared: `net8.0`, nullable enable, implicit usings |
| `.gitignore` | Create | .NET + Node standard ignores |

### src/AskMyPdf.Core/ (zero external dependencies)
| File | Action | Description |
|------|--------|-------------|
| `AskMyPdf.Core.csproj` | Create | Class library, no NuGet packages |
| `Models/Document.cs` | Create | Id (string), FileName, UploadedAt, PageCount, FileSize |
| `Models/PageBoundingData.cs` | Create | PageNumber, PageWidth, PageHeight, Words list |
| `Models/WordBoundingBox.cs` | Create | Text, Left, Bottom, Right, Top (double, PDF units) |
| `Models/HighlightArea.cs` | Create | PageIndex (int, 0-based), Left, Top, Width, Height (double, percentages 0-100) |
| `Models/Citation.cs` | Create | DocumentId, DocumentName, PageNumber, CitedText, HighlightAreas list |
| `Models/AnswerStreamEvent.cs` | Create | TextDelta, CitationReceived, Done — discriminated union via records |

### src/AskMyPdf.Infrastructure/
| File | Action | Description |
|------|--------|-------------|
| `AskMyPdf.Infrastructure.csproj` | Create | Class library, references Core, has NuGet packages |

### src/AskMyPdf.Web/
| File | Action | Description |
|------|--------|-------------|
| `AskMyPdf.Web.csproj` | Create | Web project, references Core + Infrastructure |
| `Program.cs` | Create | Minimal API entry point, DI placeholders, placeholder `/health` endpoint |
| `appsettings.json` | Create | Config structure: `Anthropic:ApiKey`, `Database:Path` |
| `appsettings.Development.json` | Create | Development overrides, logging |
| `Properties/launchSettings.json` | Create | Port 5000 config |

### tests/AskMyPdf.Tests/
| File | Action | Description |
|------|--------|-------------|
| `AskMyPdf.Tests.csproj` | Create | Test project, references Core |
| `SmokeTests.cs` | Create | One trivial test to verify test runner works |

### client/
| File | Action | Description |
|------|--------|-------------|
| `package.json` | Create | React 18, Vite, TypeScript, Tailwind, dependencies |
| `tsconfig.json` | Create | Strict mode, path aliases |
| `tsconfig.app.json` | Create | App-specific TS config |
| `tsconfig.node.json` | Create | Node/Vite TS config |
| `vite.config.ts` | Create | Proxy `/api` to `http://localhost:5000` |
| `tailwind.config.js` | Create | Tailwind config with shadcn/ui paths |
| `postcss.config.js` | Create | PostCSS with Tailwind + autoprefixer |
| `index.html` | Create | Vite entry HTML |
| `src/main.tsx` | Create | React entry point |
| `src/App.tsx` | Create | Shell component — "AskMyPdf" heading |
| `src/index.css` | Create | Tailwind directives + base styles |
| `src/vite-env.d.ts` | Create | Vite type declarations |
| `src/lib/utils.ts` | Create | `cn()` helper for shadcn/ui (clsx + twMerge) |
| `components.json` | Create | shadcn/ui config |

## NuGet Packages Needed

| Package | Version | Project |
|---------|---------|---------|
| `PdfPig` | 0.1.14 | AskMyPdf.Infrastructure |
| `Anthropic` | 12.11.0 | AskMyPdf.Infrastructure |
| `Microsoft.Data.Sqlite` | 8.* | AskMyPdf.Infrastructure |
| `xunit` | latest | AskMyPdf.Tests |
| `xunit.runner.visualstudio` | latest | AskMyPdf.Tests |
| `Microsoft.NET.Test.Sdk` | latest | AskMyPdf.Tests |
| `FluentAssertions` | latest | AskMyPdf.Tests |

## npm Packages Needed

| Package | Version | Dev? |
|---------|---------|------|
| `react` | ^18.3.0 | No |
| `react-dom` | ^18.3.0 | No |
| `@react-pdf-viewer/core` | latest | No |
| `@react-pdf-viewer/highlight` | latest | No |
| `pdfjs-dist` | ^3.11.174 | No |
| `react-resizable-panels` | latest | No |
| `lucide-react` | latest | No |
| `clsx` | latest | No |
| `tailwind-merge` | latest | No |
| `typescript` | ^5.4.0 | Yes |
| `vite` | ^5.4.0 | Yes |
| `@vitejs/plugin-react` | latest | Yes |
| `@types/react` | ^18.3.0 | Yes |
| `@types/react-dom` | ^18.3.0 | Yes |
| `tailwindcss` | ^3.4.0 | Yes |
| `postcss` | latest | Yes |
| `autoprefixer` | latest | Yes |
| `class-variance-authority` | latest | No |

## Implementation Steps

### Step 1: Root config files
1. Create `Directory.Build.props`:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
     </PropertyGroup>
   </Project>
   ```
2. Create `.gitignore` with standard .NET + Node entries (bin/, obj/, node_modules/, dist/, *.db, .env, appsettings.*.local.json)

### Step 2: Core project + models
1. Create `src/AskMyPdf.Core/AskMyPdf.Core.csproj` — bare class library (no packages)
2. Create all models in `src/AskMyPdf.Core/Models/`:
   - Use C# records for immutability
   - Use file-scoped namespaces (`namespace AskMyPdf.Core.Models;`)
   - `Document`: `record Document(string Id, string FileName, DateTime UploadedAt, int PageCount, long FileSize)`
   - `WordBoundingBox`: `record WordBoundingBox(string Text, double Left, double Bottom, double Right, double Top)`
   - `PageBoundingData`: `record PageBoundingData(int PageNumber, double PageWidth, double PageHeight, List<WordBoundingBox> Words)`
   - `HighlightArea`: `record HighlightArea(int PageIndex, double Left, double Top, double Width, double Height)`
   - `Citation`: `record Citation(string DocumentId, string DocumentName, int PageNumber, string CitedText, List<HighlightArea> HighlightAreas)`
   - `AnswerStreamEvent`: abstract record with three subtypes:
     ```csharp
     public abstract record AnswerStreamEvent
     {
         public record TextDelta(string Text) : AnswerStreamEvent;
         public record CitationReceived(Citation Citation) : AnswerStreamEvent;
         public record Done : AnswerStreamEvent;
     }
     ```

### Step 3: Infrastructure project
1. Create `src/AskMyPdf.Infrastructure/AskMyPdf.Infrastructure.csproj`
   - References `AskMyPdf.Core`
   - PackageReferences: PdfPig, Anthropic, Microsoft.Data.Sqlite
   - No code files yet — just the project with dependencies

### Step 4: Web project
1. Create `src/AskMyPdf.Web/AskMyPdf.Web.csproj` — `Microsoft.NET.Sdk.Web`
   - References Core + Infrastructure
   - Note: does NOT inherit `TargetFramework` issues — `Sdk.Web` works fine with `Directory.Build.props`
2. Create `Properties/launchSettings.json`:
   ```json
   {
     "profiles": {
       "AskMyPdf.Web": {
         "commandName": "Project",
         "applicationUrl": "http://localhost:5000",
         "environmentVariables": {
           "ASPNETCORE_ENVIRONMENT": "Development"
         }
       }
     }
   }
   ```
3. Create `appsettings.json`:
   ```json
   {
     "Logging": { "LogLevel": { "Default": "Information" } },
     "Anthropic": { "ApiKey": "" },
     "Database": { "Path": "askmypdf.db" }
   }
   ```
4. Create `appsettings.Development.json` with `"Microsoft.AspNetCore": "Warning"`
5. Create `Program.cs`:
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   // TODO: Register services (Phase 2-3)
   var app = builder.Build();
   app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
   // TODO: Map endpoints (Phase 2-3)
   app.Run();
   ```

### Step 5: Test project
1. Create `tests/AskMyPdf.Tests/AskMyPdf.Tests.csproj`
   - References Core
   - PackageReferences: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions
2. Create `SmokeTests.cs`:
   ```csharp
   using FluentAssertions;
   namespace AskMyPdf.Tests;
   public class SmokeTests
   {
       [Fact]
       public void Document_Record_Creates_Successfully()
       {
           var doc = new Core.Models.Document("1", "test.pdf", DateTime.UtcNow, 5, 1024);
           doc.FileName.Should().Be("test.pdf");
       }
   }
   ```

### Step 6: Solution file
1. Run `dotnet new sln -n AskMyPdf` at root
2. Add all 4 projects to solution
3. Verify `dotnet build AskMyPdf.sln` succeeds
4. Verify `dotnet test AskMyPdf.sln` passes

### Step 7: React frontend
1. Scaffold with Vite: `npm create vite@latest client -- --template react-ts` (or manual creation)
2. Install all npm dependencies
3. Configure `vite.config.ts` with API proxy:
   ```typescript
   export default defineConfig({
     plugins: [react()],
     server: {
       proxy: {
         '/api': 'http://localhost:5000'
       }
     }
   })
   ```
4. Set up Tailwind CSS (config, PostCSS, directives in index.css)
5. Set up shadcn/ui (`components.json`, `src/lib/utils.ts` with `cn()` helper)
6. Create minimal `App.tsx` with Tailwind styling as proof-of-life
7. Verify `npm run dev` starts successfully
8. Verify `npm run build` produces `dist/` output

### Step 8: Git init
1. `git init`
2. Stage all files
3. Initial commit: "Phase 1: Project scaffold"

## Patterns to Follow

### C# conventions (set here, followed everywhere)
```csharp
// File-scoped namespaces
namespace AskMyPdf.Core.Models;

// Records for immutable data
public record Document(string Id, string FileName, DateTime UploadedAt, int PageCount, long FileSize);

// Discriminated unions via abstract records
public abstract record AnswerStreamEvent
{
    public record TextDelta(string Text) : AnswerStreamEvent;
    // ...
}
```

### TypeScript conventions
```typescript
// Named exports, no default exports
export function App() { ... }

// Strict mode via tsconfig
```

## Risks

| Risk | Mitigation |
|------|------------|
| `@react-pdf-viewer` npm install may have peer dep conflicts with React 18 | Use `--legacy-peer-deps` if needed; verify import works |
| `pdfjs-dist` version must match `@react-pdf-viewer` expectations | Check viewer docs for compatible pdfjs version |
| `Directory.Build.props` may conflict with Web SDK | Web SDK's `Sdk.Web` handles TFM fine; test build early |
| Anthropic NuGet 12.11.0 may not exist yet | Check NuGet, fall back to latest available version |

## Validation

- [ ] `dotnet build AskMyPdf.sln` — zero errors, zero warnings
- [ ] `dotnet test AskMyPdf.sln` — 1 test passes (SmokeTests)
- [ ] `dotnet run --project src/AskMyPdf.Web/AskMyPdf.Web.csproj` — starts, `curl http://localhost:5000/health` returns `{"status":"healthy"}`
- [ ] `cd client && npm install` — no errors
- [ ] `cd client && npm run dev` — Vite dev server starts
- [ ] `cd client && npm run build` — produces `dist/` with no errors
- [ ] All 4 .NET projects are in the solution
- [ ] Core project has zero external package dependencies
- [ ] Infrastructure project references Core and has PdfPig, Anthropic, Microsoft.Data.Sqlite
- [ ] Web project references Core + Infrastructure
- [ ] `.gitignore` excludes bin/, obj/, node_modules/, dist/, *.db
- [ ] Git repo initialized with initial commit
