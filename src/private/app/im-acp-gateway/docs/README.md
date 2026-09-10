# IM-ACP Gateway Research and Analysis

Start with [Telegram-first requirements-analysis input](RequirementsAnalysisInput-TelegramFirst.md).
It identifies the narrowed analysis scope and open product decisions; it is not
a final requirements specification or implementation authorization. Project
authors maintain this documentation for the owner, requirements authors, and
domain reviewers.

- [Gateway and WeChat research](Research.md) retains the original broader
  investigation and its source references.
- [Telegram research](Research-Telegram.md) evaluates the alternative channel.
- [WeChat verification criteria](EarlyResearchVerification.md) and the
  [observed WeChat attempt](PersonalWeChatVerificationLog.md) remain relevant
  to the deferred channel. QR bootstrap does not prove completed login.
- [Telegram verification criteria](EarlyResearchVerification-Telegram.md) and
  [Telegram run evidence](TelegramBotVerificationLog.md) support the narrowed
  analysis input while preserving the permission, cancellation, group, and
  webhook limitations recorded there.

The [WeChat verifier](../poc/wechat-ilink-verifier/README.md),
[Telegram verifier](../poc/telegram-bot-verifier/README.md), and
[topic/session bridge](../poc/telegram-topic-session-bridge/README.md) retain
their own experimental usage interfaces. Their existence and past observations
do not extend the working scope into supported product behavior.

Research recommendations and run-log next actions are evidence context.
Current work authorization and generic review procedure follow the
[repository record system](../../../../../docs/governance/record-system.md)
and accepted [Delivery Wave](../../../../../docs/delivery-wave.md).
