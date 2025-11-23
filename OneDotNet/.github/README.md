# Customize AI responses for GitHub Copilot

Please check [Customize AI responses in VS Code](https://code.visualstudio.com/docs/copilot/copilot-customization) for more information on these folders:

1. `.github/chatmodes/`: Contains chat modes that define how AI should respond in different contexts.
2. `.github/prompts/`: Contains prompts that guide AI responses for specific tasks or queries.
3. `.github/instructions/`: Contains instructions that provide guidelines for AI responses in various programming languages or formats.

Currently all the files in these folders are added by `git subtree` commands. They are coming from [Awesome GitHub Copilot Customizations](https://github.com/github/awesome-copilot).

To update these files, you can run the following commands:

```bash
git subtree pull --prefix .github https://github.com/github/awesome-copilot.git main --squash
```
