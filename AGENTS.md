# AGENTS.md instructions

This repository uses harnessize for thin, progressive agent guidance.

## Start Here

1. Run `npx -y harnessize@latest context` as required session bootstrap before repository work.
2. Follow the root context to choose any focused topic supported by the installed harnessize version.
3. Align semantic use cases with the user before durable changes, then use the agreed cases as TDD
   input when behavior or contracts change.
4. Inspect the relevant code, tests, and durable docs before making changes.
5. Keep repository changes minimal and preserve user work.
6. Keep design background, dependencies, key decisions, and semantic logic in code-adjacent comments
   when the implementation would otherwise be unclear.
7. Use standalone docs only for large or cross-cutting architecture knowledge, externally consumed
   feature state, or durable process that cannot live cleanly in tests and comments.
8. Run relevant verification before reporting implementation work as complete.
9. Do not write secrets, credentials, tokens, personal data, or non-public local machine or
   workspace data into repository files. Redact or summarize sensitive information when it matters.

If conversation context has been compacted or your short-term memory no longer contains the
harnessize root context, run `npx -y harnessize@latest context` again before continuing repository
work.

## Multi-Agent Adapters

Keep durable harness guidance in this file. Adapter files such as `CLAUDE.md` or
`.cursor/rules/harnessize.mdc` should only point agents back here so the repository has one source
of truth.
