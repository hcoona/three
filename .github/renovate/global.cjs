module.exports = {
  platform: 'github',
  repositories: ['hcoona/three'],
  onboarding: false,
  requireConfig: 'required',
  allowedCommands: [
    String.raw`^pwsh -NoLogo -NoProfile -NonInteractive -File eng/scripts/Update-RenovateGlobalJsonArtifacts\.ps1$`,
    String.raw`^mise run --skip-tools update-global-json$`,
    String.raw`^mise run --skip-tools --force update-uv-lock$`,
    String.raw`^mise install pnpm && mise run --skip-tools --force update-pnpm-lockfiles$`,
    String.raw`^mise install uv && mise run --skip-tools --force update-uv-lock$`,
  ],
};
