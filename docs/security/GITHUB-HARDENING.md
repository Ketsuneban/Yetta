# GitHub repository hardening checklist

The repository-level files and workflows can be maintained in Git, but some repository security controls are account/repository settings and must be enabled in GitHub Settings.

## Branch protection for `main`

Create a branch protection rule or repository ruleset for `main` with:

- Require a pull request before merging.
- Require the **Validate static site and documentation** status check.
- Require conversation resolution.
- Block force pushes.
- Block branch deletion.
- Keep administrators subject to the rule where practical.
- Do not require an approval count that makes the single-owner project impossible to maintain; add review requirements when additional maintainers exist.

## Security features

In **Settings → Code security and analysis**, enable the features available to this public repository:

- Dependabot alerts.
- Dependabot security updates.
- Secret scanning and push protection, if available.
- Private vulnerability reporting.

A Dependabot configuration for GitHub Actions is already committed at `.github/dependabot.yml`. GitHub Actions uses read-only repository permissions in the current validation workflow.

## Current automation

- `.github/workflows/site-validation.yml` validates JavaScript syntax, HTML structure, local asset references, and JSON parsing.
- `.github/CODEOWNERS` assigns the repository owner as code owner.
- `.github/PULL_REQUEST_TEMPLATE.md` includes security and validation reminders.

## Status

The repository connector used for this setup can create and inspect repository files and pull requests, but it does not expose the GitHub Settings write operation for branch protection or security-analysis toggles. Those settings are therefore documented here for the owner to enable manually. Do not describe them as enabled until GitHub confirms the settings.
