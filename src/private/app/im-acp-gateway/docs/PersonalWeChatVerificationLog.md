# Personal WeChat Verification Log — IM-ACP Gateway

## Provenance

- Kind: iterative validation log for early research item 1.
- Purpose: record real-stack validation attempts for the Personal WeChat path on
  the intended setup without overloading the main early-research note.
- Related source document:
  `src/private/app/im-acp-gateway/docs/EarlyResearchVerification.md`

This document is intended to grow over multiple rounds. Each run should record
what was attempted, what was observed, what remains unknown, and what should be
done next.

## Scope of this log

This log is only for the first early-research verification item:

- QR-code login,
- login-state persistence,
- inbound text receipt,
- outbound text reply,
- reply-correlation metadata on real WeChat traffic.

It is not the place for gateway design decisions or broader product planning.

## Validation artifact used so far

The current validation helper is the dedicated POC at:

- `src/private/app/im-acp-gateway/poc/wechat-ilink-verifier`

That POC talks directly to the documented iLink HTTP endpoints and is designed
to validate transport primitives before any broader gateway assumptions are
treated as confirmed.

## Run log

### Run 1 — 2026-03-24

#### Goal

Start the first real-stack verification attempt for the Personal WeChat path and
see whether QR bootstrap and login completion work on the intended setup.

#### Process

1. the POC requested a QR-code login artifact from
   `https://ilinkai.weixin.qq.com`,
2. the platform returned a real `qrcode` identifier,
   `47b2fa7447a9c6abff49eb129e1b92f1`,
3. the returned artifact resolved to a real login URL under
   `https://liteapp.weixin.qq.com/...`,
4. the user scanned the QR code successfully from WeChat,
5. the user reported that WeChat platform-side authentication failed, and
6. the POC later observed the QR status transition from `wait` to `expired`.

#### Observed result

- the QR bootstrap path is real and reachable from the intended environment,
- a real scan attempt can be initiated successfully,
- however, login completion is **not yet validated** because the platform-side
  authentication step did not complete successfully.

#### Still unverified after this run

- successful login confirmation,
- token persistence across restart after a successful login,
- inbound message polling on authenticated traffic,
- outbound text reply on authenticated traffic,
- reply-metadata availability on real post-login traffic.

#### Current conclusion

The first verification item remains open. This run strengthens confidence that
the QR bootstrap path is real, but it does not satisfy the minimum success
signal for the Personal WeChat validation track.

#### Recommended next action

Retry login on the intended account/device setup and capture any platform-side
rejection details if the same failure happens again. Only after a successful
login should the POC continue to inbound polling, outbound reply, and
reply-metadata inspection.

## Open status summary

As of this document revision:

- verification item 1 is still open,
- one live run has been attempted,
- QR bootstrap has been observed,
- login completion has not yet been validated.
