# GitHub Workflows Design (v2)

This document describes the GitHub Actions workflow architecture for the three monorepo.

## 1. 架构总览设计 (Hub-and-Spoke 模式)

为了避免在三个主 Workflow 中写大量重复的构建和部署代码，强烈建议引入**可重用工作流 (Reusable Workflows)** 作为底层支撑：

- **入口层 (Entry Workflows)**: `ci.yml`, `buddy.yml`, `official.yml`
- **执行层 (Reusable Workflows, 存放在 `.github/workflows/` 下)**:
    - `_build-test-csharp.yml` (跑在 `windows-latest`)
    - `_build-test-python.yml` (跑在 `ubuntu-latest`)
    - `_build-test-jsts.yml` (跑在 `ubuntu-latest`)
    - `_publish-target.yml` (处理具体的推送动作，如推 GitHub Packages, GitHub Releases, 或 NuGet.org)

## 2. `ci.yml` (PR 验证流：精准并发，左移拦截)

**触发条件**: `on: pull_request`

为了提高效率，CI 不应该每次全量编译，而是利用路径过滤（如 `dorny/paths-filter`）来实现精准并发。

**工作流作业 (Jobs) 编排**：

1. **`static-analysis`**: 启动最快的 Ubuntu Runner，直接在全局运行 `jdx/hk`（执行 `hk check`）。因为 HK 自动识别文件类型，这里作为第一道防线即可拦截格式、Lint 等失败。
2. **`detect-changes`**: 使用 `paths-filter` 插件，检测 PR 修改的文件类型。
    - `csharp`: `['**/*.cs', '**/*.csproj', 'global.json', 'Directory.*.props']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', 'package.json', 'pnpm-workspace.yaml']`
3. **`test-csharp` / `test-python` / `test-jsts` (并发执行)**:
    - 依赖: `needs: [detect-changes, static-analysis]`
    - 条件判断: 例如 `if: needs.detect-changes.outputs.csharp == 'true'`。
    - 各自调用对应的 Reusable Workflow。C# 明确使用 `windows-latest` 跑单元测试，另两个使用 Ubuntu。

## 3. `buddy.yml` (非正式发布流：动态矩阵，打 Tag 隔离)

**触发条件**: `on: workflow_dispatch`
**输入参数**: 用户手工输入/选择 `project-name`。

因为即便同语言也有不同打包发布方式（EXE, NuGet 等），这里必须解析项目配置来生成**发布矩阵**。

**工作流作业 (Jobs) 编排**：

1. **`resolve-context`**:
    - 跑一个脚本（复用你现有的 `eng/scripts/find_*_project_path`），基于输入的 `project-name` 获取：语言类型、项目路径、版本号 (通过 NBGV)。
    - **核心设计**：读取项目的发布配置文件（或项目文件中的 Meta 信息），输出一个需发布的 Target JSON 数组（例如 `["gpr", "github_release"]`）。
2. **`static-analysis`**: 针对提取出的项目路径，精准跑一次 `hk` 检查。
3. **`build-and-pack`**:
    - 依赖: `needs: [resolve-context, static-analysis]`
    - 根据语言动态调用对应的 Reusable Workflow，将构建产物（`.nupkg`, `.whl`, `.exe` 等）上传到 CI Artifacts。
4. **`publish-unofficial` (动态矩阵)**:
    - 策略: `strategy.matrix.target: ${{ fromJson(needs.resolve-context.outputs.targets) }}`
    - 此 Job 根据矩阵目标，将产物同时、并发地发布到 GitHub Packages 和 GitHub Releases 等非正式环境。
5. **`create-traceability-tag`**:
    - 依赖: `needs: publish-unofficial`
    - 按你的格式组装并推送 Git Tag: `release/<project-name>/v<version>`。
    - **关键机制**：使用系统自带的 `${{ secrets.GITHUB_TOKEN }}` 执行 `git push origin <tag>`。已知事实表明，GitHub 官方 Token 推出的 Tag **绝对不会**触发其他依赖 Tag 的 Workflow（防止递归死循环机制）。这完美实现了非正式版 Buddy 标记了源码，但不会引爆 `official.yml`！

## 4. `official.yml` (正式环境生产发布流)

**触发条件**:

```yaml
on:
    push:
        tags:
            - 'release/*/v*'
```

正式发布的触发必须足够可信（比如通过具有特定权限的 PAT 或者专门部署脚本打的 Tag 才能触发此流）。

**工作流作业 (Jobs) 编排**：

1. **`parse-tag`**: 解析 `${{ github.ref_name }}`，无缝拆解出 `project-name` 和 `version`。同时判定需要向哪些官方渠道库（Official Registry）发布。
2. **`clean-build`**:
    - 为了确保供应链安全，不要复用任何旧包，从 Tag 指向的历史 Commit 从头再执行一次强隔离的构建与测试。调用对应的语言 Reusable Workflow（Windows / Ubuntu）。
3. **`publish-official`**:
    - 采取矩阵策略并发推送到官方环境（如 NuGet.org, PyPI, npmjs）。
    - **安全建议**：在此 Job 挂载 GitHub Actions 环境 (`environment: production`)，以便进行人工审核卡点，并利用 OIDC (`id-token: write`) 进行免密、高安全的发布（如 PyPI Trusted Publishers 或 NPM Provenance）。

### 总结优势：

1. **PR 速度最大化**：只修改前端 JS 的 PR，再也不会去排队等漫长的 Windows C# 构建。
2. **防误触与可追溯性兼得**：`buddy.yml` 结尾打 Tag 既做到了历史版本代码的强追溯，又利用了 GitHub Token 的免递归特性，完美阻断了向 `official.yml` 的泄露。
3. **高度解耦和可配性**：多形态发布（exe / nupkg 共存）问题通过 `buddy.yml` 中的 **动态 JSON Matrix** 完美解决，不管一个项目有 1 个还是 3 个发布目标，都能开箱即用地散发给并行 Job 去推送。
