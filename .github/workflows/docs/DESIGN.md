# GitHub Workflow 架构设计与最佳实践

针对多语言 monorepo（C#, Python, JS/TS）以及现有的工具链（`mise`, `hk`），本设计采用 **“枢纽驱动（Hub-and-Spoke） + 权限隔离 + 按需执行”** 的 GitHub Workflow 组合方案。

这套方案的核心原则是：**入口尽量轻量（声明触发意图），核心逻辑高度复用（Reusable Workflows），环境严格隔离。**

## 1. 核心引擎：复合动作与可复用工作流

为了避免 `ci.yml`、`buddy.yml`、`official.yml` 出现重复代码，提取出“引擎”层代码结构：

### 1.1 `actions/setup-environment/action.yml` (Composite Action)

- **包含**: `actions/checkout` -> `jdx/mise-action` (安装本地工具链) -> 运行基础的全局环境配置缓存。
- **作用**: 保证所有的机器（开发机、CI runner）跑的工具版本完全一致。

### 1.2 `workflows/_reusable_build_test.yml` (Reusable Workflow)

- **输入**: `project_path`, `language` (csharp/python/javascript)
- **逻辑**:
    1. 调用 `setup-environment`
    2. 执行当前项目的 `hk` 静态分析拦截 (`hk check` 等)
    3. 编译与单元测试 (例如 `dotnet test`, `uv run pytest`)
    4. 打包产物 (生成 `.nupkg`, `.whl` 等) 并 `actions/upload-artifact` 上传供后续部署使用。

### 1.3 `workflows/_reusable_publish.yml` (Reusable Workflow)

- **输入**: `environment` (buddy_env / prod_env), `package_kind` (nuget, msix, pypi, npm)
- **逻辑**: 下载 artifact，根据 `package_kind` 将包通过 OIDC 或对应的 Secret 推送至非生产环境（如 GPR, TestPyPI）或生产环境（如 NuGet Gallery, PyPI）。

## 2. 三大对外暴露的入口工作流

### 2.1 `ci.yml` (Pull Request 触发)

**目标**：将 CI 左移并且只按需跑测试，省时省力。典型的“动态矩阵生成（Dynamic Matrix）”模式。

- **Job 1: `detect-changes`**
  使用路径过滤工具判断 PR 中修改了哪些子目录或语言。将产生变更的项目列表输出为 JSON 数组。
- **Job 2: `build-and-test`**
    - **Runner 选择**: C# 动态落到 `windows-latest`，Python/TS 落在 `ubuntu-latest`。
    - **执行**: 使用 `strategy: matrix` 并通过调用 `_reusable_build_test.yml` 仅对真正变更的项目执行检查。

### 2.2 `buddy.yml` (用户手工触发，发布非正式环境)

**目标**：手工发布独立项目、生成追溯 Tag、部署至非正式环境。

- **触发**: `workflow_dispatch`，用户填写 `project_name`, `language`, `package_kind` (如 `csharp-nuget` 或 `csharp-desktop`), `version_suffix` (如 `beta`)。
- **Job 1: `build-and-package`** -> 调用 `_reusable_build_test.yml` 构建产物。
- **Job 2: `create-buddy-tag`** (需在前置步骤成功后运行)
    - 使用 GitHub API/CLI 打预发布 Tag。为避免污染正式 Tag 并区分语义，建议命名为 `buddy/<project>/v<version>-beta.<run_id>`。
- **Job 3: `publish-non-prod`**
    - 绑定 GitHub Environment: `buddy-release`（可配置无需 review 自动放行）。
    - 调用 `_reusable_publish.yml` 传入 `buddy` 环境。通过 `package_kind` 分支路由：若是 `csharp-nuget` 此时推至 GitHub Packages，若是 `csharp-desktop` 将 exe 上传为该 Tag 的 Release Draft asset。

### 2.3 `official.yml` (Git Tag 触发，发布正式环境)

**目标**：监听标准生产 Tag，执行严格的生产级部署。

- **触发**: `on: push: tags: ['release/*/v*']`
- **Job 1: `parse-tag`** -> 动态解析这段 Tag 属于哪个 `project`, `language`, `version`。
- **Job 2: `build-and-package`** -> **强制从头再构建一次**（复用 `_reusable_build_test.yml`）。因为 tag 是 immutable 的，这确保了源码完全洁净，不依赖 buddy 阶段的构建物。
- **Job 3: `publish-prod`**
    - 绑定 GitHub Environment: `production-release`（配置 Required Reviewers，需要人工点同意才能继续）。
    - 调用 `_reusable_publish.yml` 进行 OIDC 认证推送（例如 NuGet 官方源，PyPI 官方源）。

## 3. 关键设计优势总结

1. **Buddy 和 Official 的 Tag 隔离设计**：
   使用 `buddy/*` 和 `release/*` 前缀切分触发边界。人工发起 Buddy 生成 `buddy/` 前缀 tag，不会误触发 `official.yml`。只有明确晋升到 `release/` tag 时，才会走最终发布，易于追溯。
2. **多语言与多类型打包解耦**：
   构建工作流（如 C# Desktop，或 Nuget）仅产生文件放入 artifact。真正的分发全靠 `package_kind` 参数在 Publish 阶段路由，后续新增语言或项目类型极易扩展。
3. **安全与资源管控**：
   利用 GitHub Environment 的审核机制来隔离生产与非生产环境发布，结合 `mise` 和 `hk` 控制统一的构建版本环境，能够大大降低“在我机器上能跑”带来的误差风险。
