# The Python Mono-repository for myself

OnePython is a mono-repository for all of my Python codes. Please check Google's article [Why Google Stores Billions of Lines of Code in a Single Repository](https://cacm.acm.org/magazines/2016/7/204032-why-google-stores-billions-of-lines-of-code-in-a-single-repository/fulltext) for more details about why this repo born.

## Getting Started

### Prerequisites

Install UV

1. Linux: `curl -LsSf https://astral.sh/uv/install.sh | sh`
2. Windows: `powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"`
3. Pip: `pip install --user uv`

### Workspace layout

- `pyproject.toml` now lives at the repository root so `uv` can manage the entire mono-repo.
- Lab-style sandboxes are under `src/lab/*` (for example `src/lab/azure-document-intelligence-lab`). Every lab has hatch + `nbgv-python` wired up for versioning.
- Reusable libraries and tools remain under `OnePython/packages/*` and share the same workspace and dev tooling.

### Tooling

- **Format & lint:** `uv run ruff format .` and `uv run ruff check .`
- **Type checking:** `uv run pyrefly`
- **Tests:** `uv run pytest`

All three commands pick up the workspace configuration from the root `pyproject.toml`, so they automatically traverse both `src/lab/*` and `OnePython/packages/*`.
