# Project preferences

- After making and validating updates, install the relevant updated files into the local SPT installation at `F:\SPT 4.1.x` as part of completing the task. The user has authorized this by default; do not stop at building or ask for installation permission again.
- Back up installed files before replacing them, preserve configuration and profile data, and verify that installed files match the validated build. For isolated UI changes, install the updated UI assembly without deploying unrelated work in progress. For changes spanning components, install the required matching components together.
- Use `dotnet msbuild build.proj` to build, validate and install matching components through MSBuild. Paths default to the game two directories above this checkout. For an isolated UI assembly update, use `-p:DeploymentScope=UI`. Packaging also installs the validated update; staging alone does not complete local work.
- Never stop, start, restart, terminate or launch any servers or clients, including test instances. Do not manage their processes or run startup verification. The user controls application lifecycles.
- The isolated test server is retired from the workflow. Do not create, copy, launch or use a test runtime, and do not run the historical server fixtures or redirect them to installed profiles. Use offline contracts, assembly checks and file-only deployment checks.
- If a server or client locks required files, keep the validated update ready and report the blocked installation. The user must close the locking application before installation can finish. State which application needs a manual restart for installed assemblies to take effect; never perform it.


# Codex project instructions

For complex coding tasks, use the `astra-orchestrator` skill when its trigger conditions match.

The root agent owns architecture, scope decisions, delegation, integration, and final verification.
Prefer specialized subagents for bounded exploration, implementation, testing, review, and technical research.

Treat orchestration as adaptive routing, not a fixed pipeline:
- For small, localized work: the root handles it directly.
- For bounded work that benefits from separation: one capable worker may be enough.
- For risky or cross-cutting work: expand into investigation, implementation, verification, and independent review.

Workers get bounded ownership and should finish their assignment rather than repeatedly handing it back.
Specialists (tester, reviewer, researcher) are conditional, not mandatory pipeline stages.
Review should be proportional to risk rather than automatically invoking the full topology.

Do not delegate trivial work merely for parallelism.
Do not let multiple implementation agents edit the same files without explicit ownership boundaries.
Model and reasoning assignments live in `.codex/config.toml` and `.codex/agents/*.toml`; this file defines behavior and boundaries rather than duplicating configuration.
User instructions always take precedence over this orchestration policy.
