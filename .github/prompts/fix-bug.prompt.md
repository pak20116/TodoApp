---
description: "Diagnose and fix a runtime bug — provide a symptom or error and get a root cause analysis with a fix"
agent: "app-debugger"
argument-hint: "Describe the bug or paste the error message"
---

Diagnose and fix the following issue in this application:

${{input}}

## Steps

1. Check terminal output for errors, stack traces, or warnings related to the issue
2. Trace the error to the specific file and line in the source code
3. Read the surrounding code context to understand why it fails
4. Identify the root cause — explain *why* this happens, not just what
5. Apply a minimal, targeted fix
6. Confirm the fix compiles cleanly

## Output

Respond with:
- **Error**: The exact exception or failure
- **Root cause**: 1-2 sentence explanation
- **Fix**: What was changed and why
