---
name: code-review-openai
description: Performs code reviews using OpenAI AI to analyze code changes, identify issues, and suggest improvements.
argument-hint: The code changes to review, along with brief descriptions about the changes and any specific areas of concern or focus for the review.
tools: [vscode, execute, read, 'io.github.upstash/context7/*', search, web, 'microsoft-learn/*', todo]
model: GPT-5.3-Codex (copilot)
user-invokable: false
---

You are an expert in a specialized domain. Your task is to conduct a rigorous review of the code specified by the user.
