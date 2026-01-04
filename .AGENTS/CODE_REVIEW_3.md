# Code Review: `.github` changes (origin/main...HEAD)

<!-- markdownlint-disable MD013 -->
<!-- markdownlint-disable MD044 -->

Date: 2026-01-04
Branch: dev/shuaizhang/refactor-buddy-official
Scope: `/.github/workflows/*.yml` (root workflows only)

Changed files (from `git diff origin/main...HEAD --name-only -- .github`):

- `.github/workflows/buddy.yml`
- `.github/workflows/official.yml`
- `.github/workflows/release-build-node-pack.yml` (new)
- `.github/workflows/release-build-python.yml` (new)
- `.github/workflows/release-build-wxt.yml` (new)
- `.github/workflows/release-resolve.yml` (new)

Decision references consulted:

- `.AGENTS/CLARIFY_0.md` – `.AGENTS/CLARIFY_4.md`
- `.AGENTS/CLARIFY_CR_0.md` (confirmed)
- `.AGENTS/CLARIFY_CR_1.md` (confirmed)
- `.AGENTS/CLARIFY_CR_2_5.md` (confirmed)

Diff hygiene:

- `git diff --check origin/main...HEAD -- <files>`: **no whitespace errors** observed.

---

## 总体结论

这次改动整体方向正确：

- 把“解析 release 输入/识别项目/规范化输出”集中到可复用的 `release-resolve.yml`，符合 `workflow_call` 场景的明确输入契约（见 `CLARIFY_3.md`）。
- 统一产物目录为 `${GITHUB_WORKSPACE}/out`，并以 `out/*` 作为 release asset 合同，符合 `CLARIFY_1.md`/`CLARIFY_0.md`。
- Node 流程实现 **pack first、publish from tarball**（并将 tarball 重命名为固定名 `out/gpr.tgz` / `out/npmjs.tgz`），符合 `CLARIFY_4.md`。
- dist-tag 通过 NBGV `PrereleaseVersionNoLeadingHyphen` 推导并严格校验，符合 `CLARIFY_1.md` 与 `CLARIFY_CR_0.md`。
- buddy 增加 non-clobber guard，并将 guard 前置为 build/pack/publish 的 gate，符合 `CLARIFY_CR_2_5.md`。
- official 增加独立 attestation jobs，并让 GitHub Release creation 同时依赖 publish 与 attest，符合 `CLARIFY_CR_0.md` #3 与 `CLARIFY_CR_1.md` #4。

但有 1 个非常值得警惕的点（见下方“必须修复”）：它可能导致 **official tag push 路径在运行时直接报错**。

---

## 必须修复（Blocking）

### 1) `official.yml` 在 `push` 触发路径中引用 `inputs.*` 的兼容性风险

**文件：** `.github/workflows/official.yml`

**现象：**

- workflow 同时支持 `push`（tag）和 `workflow_dispatch`。
- 其中多处表达式直接使用 `inputs.project` / `inputs.version` / `inputs.target` / `inputs.force_update_tag`，例如：
    - `concurrency.group`：`...-${{ inputs.project || 'push' }}`
    - `resolve` job 的 `with:`：`project: ${{ inputs.project }}` 等

**风险：**

- 在 GitHub Actions 的上下文模型里，`inputs` 主要用于 `workflow_dispatch` / `workflow_call`。
- 对于 `push` 触发，`inputs` 在部分情况下可能被判定为“不可用上下文”，从而导致表达式解析/运行时报错（而不是得到空值）。
- 这会直接影响最关键的 official tag push 发布路径。

**建议修复方向（不需要人工决策，属于工程修复）：**

- 在同时支持 `push` + `workflow_dispatch` 的 workflow 中，优先使用 `github.event.inputs.*` 读取 dispatch 输入（对 `push` 事件通常会安全地返回空/未定义），避免直接引用 `inputs.*`。
- `force_update_tag` 需要保证传入 `workflow_call` 的类型是 boolean，可用：`github.event.inputs.force_update_tag == 'true'` 规避 stringly typed。

---

## 重要问题（建议修复 / 强烈建议确认）

### 2) `release-build-wxt.yml` 的 fallback 依赖 `scripts/nbgv-version.mjs`：需要明确“WXT 项目最低要求”

**文件：** `.github/workflows/release-build-wxt.yml`

**当前行为：**

- 优先跑 `zip:<browser>` 脚本；Chrome 允许 `zip` 但必须显式包含 `wxt zip` 且带 `-b chrome`。
- 若脚本不存在，则要求包内存在 `./scripts/nbgv-version.mjs` 并通过它运行 `wxt zip -b <browser>`。

**优点：**

- 符合 `CLARIFY_CR_1.md` #1（显式 Chrome/Firefox/Edge）与 #2（质量检查）。
- 很可能也符合 repo 的“打包时 stamp 版本”的现实需要（不少包可能有 placeholder version）。

**潜在问题：**

- 对新/未迁移的 WXT 项目，若既没有 `zip:<browser>` 脚本也没有 `scripts/nbgv-version.mjs`，将 hard fail。

**建议：**

- 在仓库文档或贡献说明中补一条“WXT 发布最低要求”：
    - 必须提供 `zip:chrome|zip:firefox|zip:edge`（或受限的 `zip`）脚本，**或**提供 `scripts/nbgv-version.mjs`。

（这不是必须阻断合并的点，但最好让失败模式更可预期。）

---

## 逐文件评审要点

### `.github/workflows/release-resolve.yml`（new）

做得好的点：

- 明确 `source=tag|manual` 输入契约，不依赖 `github.event_name`（符合 `CLARIFY_3.md`）。
- `run_url` 作为输入透传，确保链接指向 entry workflow run（符合 `CLARIFY_3.md` #2）。
- 在解析出 `target` 后 `git checkout --detach "${target}"`，确保后续的 detection/validation 针对目标提交而不是当前 HEAD。
- 输出中的布尔值（例如 `force_update_tag`）规范化为 `'true'|'false'` 字符串，便于下游 job 严格比较。

可改进：

- `checkout(fetch-depth: 0)` 之后又 `git fetch --tags` + `git fetch --all`，功能正确但可能偏重；若未来考虑性能，可缩小 fetch 范围（不影响正确性）。

### `.github/workflows/release-build-node-pack.yml`（new）

做得好的点：

- dist-tag 基于 NBGV prerelease metadata，且当 `version` 看似 prerelease 但 metadata 为空时 fail fast（符合 `CLARIFY_4.md` #2）。
- 默认跑 `lint/typecheck/test/build`（`--if-present`），符合 `CLARIFY_1.md`。
- pack 出确定性文件名（`out/gpr.tgz`, `out/npmjs.tgz`），符合 `CLARIFY_4.md`。
- pack/publish 分离，为 official/buddy 统一“从 tarball 发布”提供了强约束。

注意点：

- 使用 `npm pack --ignore-scripts` 是安全偏向选择；需要确保仓库内 Node 包的“打包前构建”都通过 `prepack`（或显式 build 脚本）已完成，否则可能出现 tarball 缺少构建产物。

### `.github/workflows/release-build-python.yml`（new）

做得好的点：

- `uv build --out-dir out` + `verify_python_artifact_version.py` 校验产物版本与 tag 一致。
- 权限最小化。

### `.github/workflows/release-build-wxt.yml`（new）

做得好的点：

- 显式浏览器矩阵 `chrome firefox edge`（符合 `CLARIFY_CR_1.md` #1）。
- 默认跑质量检查（符合 `CLARIFY_CR_1.md` #2）。
- 只收集 `.output/*.zip`（浅层），并做 basename collision 检测（符合 `CLARIFY_1.md` #5）。

### `.github/workflows/buddy.yml`

做得好的点：

- `guard-non-clobber` 通过 `gh api .../releases/tags/<tag>` 检查 `prerelease`，对 `prerelease=false` fast fail（符合 `CLARIFY_CR_0.md` #5）。
- guard 被加入 build/pack/publish 的 `needs`，提前阻断副作用（符合 `CLARIFY_CR_2_5.md`）。
- GPR publish 采用 step-scoped `.npmrc` + `NPM_CONFIG_USERCONFIG`（符合 `CLARIFY_CR_1.md` #3）。
- buddy Node 从 tarball 发布（`out/gpr.tgz`），符合 `CLARIFY_0.md` #8。

可改进：

- `HTTP 404` 的错误匹配依赖 `gh` 文案，可能略脆；若后续希望更稳健，可改为解析 HTTP 状态码（但不是必须）。

### `.github/workflows/official.yml`

做得好的点：

- PyPI/nmpjs publish 仍留在 `official.yml` 并使用对应 environment（符合 `CLARIFY_0.md` #4/#5/#6）。
- official attestation 独立成 job，并且 release job 依赖 publish + attest（符合 `CLARIFY_CR_0.md` #3、`CLARIFY_CR_1.md` #4）。
- Node publish 从 pack workflow 产出的 tarball 发布，保持一致性与可审计性。

关键风险：

- 见“必须修复 #1”：`push` 路径中 `inputs.*` 的上下文可用性。

---

## 建议的验证清单

- 用 `actionlint` 或等价工具做一次 workflow 语法与表达式上下文检查（重点盯 `official.yml` 的 `inputs.*`）。
- 跑 4 个关键场景（最好在 fork 或测试仓库验证）：
    - official tag push（python）：build → publish → attest → create release
    - official tag push（node）：pack → publish(GPR+npmjs OIDC) → attest → create release
    - buddy manual（node）：guard 触发与不触发两种路径；确认不会在 guard fail 时 publish
    - WXT：确认会产出 chrome/firefox/edge 的 zip，且 `.output/*.zip` 被正确收集进 `out/`
