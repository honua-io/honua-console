ci: document every action and reusable-workflow pin with its version

## Summary

All workflow-fixable Scorecard and Dependabot findings in this repo are already closed. Trunk 543f4e9 (2026-10-20) has 0 Dependabot alerts and only the repo-policy Scorecard checks open (BranchProtection, CodeReview, CII-Best-Practices, Fuzzing). The org pin checker (honua-release `tools/check_action_pins.py`) still rejects three pins, so this PR brings them into line with the convention the other honua-io repos use. No SHA changes.

## Changes Made

- `ci.yml`: the checkout pin that had no comment gets `# v7`, matching the other ten uses of that SHA.
- `security.yml`: the Trivy and Scorecard reusable-workflow pins were labelled `# main`. They now name what they run: `# v0.36.0 Trivy workflow snapshot` (both `aebfacd` and `e26b67e` run trivy-action v0.36.0) and `# v2.4.4 Scorecard workflow snapshot`.

## Breaking Changes

None.

## Testing

- honua-release `tools/check_action_pins.py` (trunk 4642e4d, with the reusable-workflow reachability check enabled) passes on all workflows. It failed on three lines before this change.
- actionlint 1.7.12 passes.
- Read the reusable workflows at both pinned SHAs and confirmed the trivy-action and scorecard-action versions they run.
- `git diff --check` passes.

No alert was dismissed.
