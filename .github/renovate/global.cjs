module.exports = {
  platform: 'github',
  repositories: ['hcoona/three'],
  onboarding: false,
  requireConfig: 'required',
  allowedCommands: [
    String.raw`^pwsh -NoLogo -NoProfile -NonInteractive -File eng/scripts/Update-RenovateGlobalJsonArtifacts\.ps1$`,
  ],
};
