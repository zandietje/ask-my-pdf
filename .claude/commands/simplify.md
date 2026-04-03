# /simplify — Simplify Code

Review recently changed or specified code and simplify without changing behavior.

## Arguments

$TARGET: File path or `recent` for recently modified files. Default: `recent`

## Process

1. Identify target code
2. Read carefully
3. Find simplification opportunities:
   - Interfaces with one implementation (not needed for testing)
   - Unused generics, over-engineered patterns
   - Verbose code that could use modern C# (primary constructors, `?.`, `??`, collection expressions)
   - Dead code, commented-out code, unused usings
   - Overly defensive error handling for internal code
4. Apply simplifications preserving exact behavior
5. Run `dotnet build` to verify

## Rules

- **Never change behavior** — only simplify implementation
- **Never remove error handling at API boundaries**
- **Run `dotnet build` after changes** — revert if broken
- **Small, targeted changes** — don't rewrite entire files
