---
description: "Review code for security, performance, and architectural issues"
argument-hint: "File path or feature area to review"
tools: [read, search]
---

Review the following code for issues:

${{input}}

## Review Checklist

### Security & Auth
- Missing `[Authorize]` attributes on pages or endpoints
- User data accessed without `userId` scoping (data leakage between users)
- Raw SQL or unparameterized queries
- Missing input validation or `[Required]` annotations
- Secrets or connection strings hardcoded in source

### Performance & EF Core
- N+1 query problems (missing `Include()` for navigation properties)
- Loading full entity lists when only counts or subsets are needed
- Missing `async`/`await` on database calls
- Unbounded queries without pagination or `Take()` limits

### Architecture & Conventions
- Services bypassing `I*Service` interface pattern
- DbContext used directly in Blazor components instead of through services
- Business logic in page components instead of service layer
- Missing DI registration in Program.cs for new services
- Client-side SignalR connections from Blazor Server components (causes 401)

## Output Format

For each issue found:
- **File**: path and line range
- **Severity**: Critical / Warning / Suggestion
- **Issue**: What's wrong
- **Fix**: How to resolve it

End with a summary: total issues by severity and an overall assessment.
