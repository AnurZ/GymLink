# GymLink working rules

- Use the local `gymlink-architecture` skill for every non-trivial GymLink task; read its `references/index.md` before changing code.
- Treat `architectureReference/` as read-only. Reuse patterns selectively and never copy its product domain, seed data, UI, dead code, or generated output.
- Use an ExecPlan or the relevant phase plan for complex, cross-cutting, or multi-step work.
- Use English for code identifiers. UI copy may remain Bosnian where it follows the approved mockups.
- Keep controllers fff thin: no business logic and no direct controller-to-`DbContext` access.
- Do not hardcode secrets or environment-specific values.
- Do not create migrations until the domain model and EF Core configurations have been reviewed and approved.
- Do not add generated, dead, placeholder, or unused template code.
- End every task with relevant build/test verification, exact commands/results, and a concise changed-files summary.
