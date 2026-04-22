# GitHub Packages Supported Registries

## Summary

This source digest captures the GitHub documentation evidence used to clarify
which package ecosystems GitHub Packages currently supports for the workflow
release requirements discussion.

## Key Points

- GitHub Packages is a family of registries rather than one generic package
  bucket.
- The registries relevant to this repository's release discussion are npm,
  RubyGems, and NuGet.
- The same introduction page also lists Maven, Gradle, and the Container
  registry.
- The page does not list PyPI as a supported GitHub Packages registry.
- GitHub Actions workflows may publish GitHub Packages artifacts with
  `GITHUB_TOKEN` when the package is associated with the workflow repository.
- The GitHub Actions integration page recommends `GITHUB_TOKEN` for GitHub
  Packages publication and access from workflows.

## Important Claims

- GitHub Packages support should be treated as a platform capability boundary,
  not as a repo-wide default target mapping.
- GitHub Packages does not need repository-stored static publishing credentials
  in the common workflow path because `GITHUB_TOKEN` is the documented
  authentication mechanism.
- Python package publication cannot assume a GitHub Packages target path under
  the current documented support matrix.
- The repository's Python release rules therefore need a non-GitHub-Packages
  path when package publication is required.

## Related Pages

- [Workflow Release Requirements Baseline](../analyses/workflow-release-requirements-baseline.md)
- [Workflow Release Requirements Interview](./2026-04-21-workflow-release-requirements-interview.md)

## Source Location

- GitHub Docs: <https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages>
- GitHub Docs: <https://docs.github.com/en/packages/managing-github-packages-using-github-actions-workflows/publishing-and-installing-a-package-with-github-actions>
