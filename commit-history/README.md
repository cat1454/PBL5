# Commit History

This folder stores one Markdown snapshot per commit.

Each snapshot is created automatically by the repo's `pre-commit` hook.

Snapshot contents:

- created time
- current branch
- staged file list
- staged diff stat

Hook setup for this clone:

```powershell
git config core.hooksPath .githooks
```

The current repo has been configured locally to use that hooks path.
