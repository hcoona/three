module.exports = {
  platform: 'github',
  repositories: ['hcoona/three'],
  onboarding: false,
  requireConfig: 'required',
  allowedCommands: [
    String.raw`^pwsh -NoLogo -NoProfile -NonInteractive -File eng/scripts/Update-RenovateGlobalJsonArtifacts\.ps1$`,
    String.raw`^pkl eval -f json global\.pkl -o global\.json$`,
    String.raw`^mise install --locked pnpm$`,
    String.raw`^mise run --skip-tools --force update-pnpm-lockfiles$`,
    String.raw`^mise install --locked uv$`,
    String.raw`^mise run --skip-tools --force update-uv-lock$`,
    String.raw`^uv run --no-project --python 3\.13 python eng/scripts/node_runtime_facts\.py update-renovate-lock --expected-node \d+\.\d+\.\d+$`,
  ],
};
