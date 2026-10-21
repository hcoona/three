# Workflow Delivery Browser Store Integration Contract

This contract owns the concrete browser-extension Release integration: the
`steam-account-history-to-csv` preparation, the Chrome Web Store, Firefox AMO
and Microsoft Edge Add-ons destinations, their completion conditions and the
owner setup they need. The [requirements](../requirements.md#release-delivery)
and the MLD's [workflow boundaries](../middle-level-design.md#workflow-permission-and-transfer-boundaries)
remain the authority; this section adds no obligation beyond the code it cites.
Changes follow the [shared review core](../middle-level-design.md#release-integration-review).

## Caller and Channel

The caller is
[`workflow-delivery-v3-browser-store-release.yml`](../../../../../../.github/workflows/workflow-delivery-v3-browser-store-release.yml).
It takes `store` (`chrome-web-store`, `firefox-amo` or `edge-addons`),
`channel` (`official` or `buddy`) and `dry-run` (default `true`). The three
stores are Official only; a Buddy request is accepted only as a dry run, and a
live Official request only from `refs/heads/main`
([Official channel scope](common.md#official-channel-scope)). Concurrency is
one group per store and product, never cancelled.

| Job             | Capability                                                     | Work                                                                                                                                                         |
| --------------- | -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `prepare`       | `contents: read`                                               | Build control first, install frozen dependencies with `--ignore-scripts`, run `release prepare-browser-store` once and retain the selected store's originals |
| `observe`       | `contents: read`, `id-token: write` (live CWS read-only token) | `observe-browser-store`, `job-inputs browser-store state` and, on dry run, `describe-browser-store`                                                          |
| `publish`       | Environment `workflow-delivery-v3-<store>`; store credentials  | Fresh observation, the store's native upload/submit operations and, for AMO and Edge, the post-submission observation                                        |
| `verify-result` | `contents: read`, `id-token: write` (CWS read-only token)      | Final CWS `observe-browser-store`, then `complete-browser-store` for every store                                                                             |

The publisher runs only for a live request whose observation needs a write.
A live CWS request that is already satisfied completes without entering the
gate ([WD-REL-007](../requirements.md#release-delivery)). AMO and Edge
observation needs the publish credentials, so a live AMO or Edge request
always enters the gate.

A dry run, from any ref and either channel, acquires no store credential, not
even the CWS read-only token: that token comes from the one write-capable
service account, which is a publication capability
([WD-CHN-004](../requirements.md#buddy-and-official-channels)). It still
builds, qualifies and describes the hypothetical operations, but observes no
store; its observation is `Unobserved` and its result states that the store
was not observed. Only a live Official request on `main` authenticates.

## Preparation

[`BrowserStorePreparation`](../../Release/BrowserStorePreparation.cs) reads the
committed [release unit](../../../../../public/lib/steam-account-history-to-csv/workflow-delivery.release-unit.yml)
and runs, from the checkout, the product's own WXT scripts once per required
output: `pnpm run zip` (Chrome), `zip:firefox` (the Firefox zip and the AMO
sources zip) and `zip:edge`. Each store's preparation retains only its own
outputs (`chrome-zip`; `firefox-zip` and `firefox-sources`; `edge-zip`).

The manifest version is NBGV `Major.Minor.Build.VersionHeight` with each part
at most 65,535; the control projects it independently
([`NbgvInputReader.BrowserVersion`](../../Repository/NbgvInputReader.cs)) and
requires the generated manifest to match. `version_name`, when present, must
equal the NBGV npm version. The clean-consumption check
([`BrowserExtensionArchive`](../../Ecosystems/BrowserExtensionArchive.cs))
rejects unsafe or duplicate entries and requires a root `manifest.json` with
the expected name, version and manifest version whose referenced files exist.
The sources zip is rooted at the workspace so an AMO reviewer can rebuild the
Firefox zip from the product's
[build instructions](../../../../../public/lib/steam-account-history-to-csv/README.md#rebuild-from-the-amo-sources-archive);
it must contain `pnpm-lock.yaml`, `pnpm-workspace.yaml` and the product's
`package.json` and `wxt.config.ts`, and no `node_modules` or `.output`
content.

## Destinations and Completion

Completion is the store reporting the prepared version as received, in review
or published ([WD-REL-009](../requirements.md#release-delivery)); updates apply
only to an existing listing. A missing listing, a later or other in-flight
version, or a rejected, disabled or taken-down version is a conflict that
blocks writes. An unreadable state is unknown. A request whose initial
observation needed a write but that has no publisher result is unknown, with
its mutation unknown, even when the final observation shows the version.

| Store            | Authentication                                                                                        | Observation                                                                                                                                                    | Publication                                                                                                                                  |
| ---------------- | ----------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| Chrome Web Store | Workload Identity Federation from GitHub OIDC to one service account; short-lived OAuth access tokens | API v2 `fetchStatus` with `chromewebstore.readonly` in `observe` and `verify-result`: `STAGED` is received, `PENDING_REVIEW` in review, `PUBLISHED*` published | API v2 `upload` (polling `fetchStatus` while in progress) and `publish` with the `chromewebstore` scope; no completion read in the publisher |
| Firefox AMO      | JWT issuer and secret (Environment secrets); one HS256 token per request                              | In the publisher: add-on `versions/v<version>/` (`unreviewed` in review, `public` published); add-on 404 is a conflict                                         | API v5 listed upload, upload validation polling, then version creation with the sources zip; re-observes the version                         |
| Edge Add-ons     | API v1.1 `Authorization: ApiKey` and `X-ClientID` (Environment secrets)                               | None: the API has no read endpoint, so state is `Unobserved` before submission                                                                                 | Package upload and submission, polling each operation; a succeeded submission is received, a failed one a conflict                           |

Edge fails closed. Without a read endpoint, a live Edge request cannot tell
whether an earlier request already submitted the version, so it never retries
or resubmits on its own: an upload or submission whose operation fails or does
not finish leaves the request unknown or conflicting and returns it to the
owner, and a rerun of the same request is rejected (WD-OPS-002 and WD-OPS-003
in [Observation, Retry, and Recovery](../requirements.md#observation-retry-and-recovery)).
Before dispatching a new live Edge request after an unknown outcome, the owner
checks the product in Partner Center and dispatches only if the version was not
received.

The adapters live in [`Destinations/`](../../Destinations/) (`ChromeWebStore`,
`FirefoxAmo`, `EdgeAddons` and the shared bounded `StoreHttp`).
[`BrowserStoreRelease`](../../Release/BrowserStoreRelease.cs) owns describe,
publish and complete. Scenario tests use HTTP fakes in
[`BrowserStoreReleaseTests`](../../../../../../tests/private/app/workflow-delivery/Release/BrowserStoreReleaseTests.cs)
and fake native builds in
[`BrowserStorePreparationTests`](../../../../../../tests/private/app/workflow-delivery/Release/BrowserStorePreparationTests.cs).
[`BrowserStorePreparationNativeTests`](../../../../../../tests/private/app/workflow-delivery/Release/BrowserStorePreparationNativeTests.cs)
(category `BrowserStoreNativePreparation`) installs the locked product graph
with `--ignore-scripts` in a clean clone and runs the real preparation, which
builds and qualifies all four zips with WXT under the locked Node and PNPM.
Ordinary CI runs it in the Linux `native-preparation-tests` job when
`ci native-preparation-plan` selects `browser`: a change to the managed
control, the shared CI and tool inputs, the root PNPM workspace files, the root
`version.json` or the product directory.
[`test_browser_store_release_caller.py`](../../../../../../tests/eng/test_browser_store_release_caller.py)
checks the caller's credential and gate boundaries. None of them establishes
live store acceptance.

## Owner Setup

The workflow needs these before a live run. A dry run uses no credential; it
needs only the selected store's ID variable and, for CWS, the publisher ID.

### Repository Variables

| Variable                                           | Value                                                                                          |
| -------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `WORKFLOW_DELIVERY_CWS_WORKLOAD_IDENTITY_PROVIDER` | `projects/<project-number>/locations/global/workloadIdentityPools/<pool>/providers/<provider>` |
| `WORKFLOW_DELIVERY_CWS_SERVICE_ACCOUNT`            | The service account email, for example `cws-publisher@<project-id>.iam.gserviceaccount.com`    |
| `WORKFLOW_DELIVERY_CWS_PUBLISHER_ID`               | The Chrome Web Store publisher ID from the Developer Dashboard                                 |
| `WORKFLOW_DELIVERY_CWS_EXTENSION_ID`               | The existing Chrome Web Store item ID                                                          |
| `WORKFLOW_DELIVERY_AMO_ADDON_ID`                   | The existing AMO add-on GUID, slug or numeric ID                                               |
| `WORKFLOW_DELIVERY_EDGE_PRODUCT_ID`                | The existing Edge Add-ons product ID from Partner Center                                       |

### Environments and Secrets

Create each Environment with deployment branch policy `main` only and the
required reviewers the owner chooses.

| Environment                             | Environment secrets                                                                                             |
| --------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `workflow-delivery-v3-chrome-web-store` | None                                                                                                            |
| `workflow-delivery-v3-firefox-amo`      | `AMO_JWT_ISSUER` and `AMO_JWT_SECRET` from the AMO Developer Hub API credentials page                           |
| `workflow-delivery-v3-edge-addons`      | `EDGE_API_KEY` and `EDGE_CLIENT_ID` from the Partner Center Edge publish API page (API v1.1 key, not v1 secret) |

### Google Cloud Workload Identity Federation

1. In a Google Cloud project, enable the Chrome Web Store API
   (`chromewebstore.googleapis.com`) and the IAM Service Account Credentials API
   (`iamcredentials.googleapis.com`).
2. Create a workload identity pool, for example `github`.
3. Create an OIDC provider in it with issuer
   `https://token.actions.githubusercontent.com`, attribute mapping
   `google.subject=assertion.sub`, `attribute.repository=assertion.repository`
   and `attribute.workflow_ref=assertion.workflow_ref`, and exactly this
   required attribute condition:

    ```text
    assertion.repository == 'hcoona/three' && assertion.workflow_ref == 'hcoona/three/.github/workflows/workflow-delivery-v3-browser-store-release.yml@refs/heads/main'
    ```

    It admits only this caller run from `main`. It does not require
    `assertion.environment`: the read-only `observe` and `verify-result` jobs
    have no Environment.

4. Create a service account, for example `cws-publisher`, with no project
   roles.
5. Grant `roles/iam.workloadIdentityUser` on that service account only to the
   principal set that follows the condition:
   `principalSet://iam.googleapis.com/projects/<project-number>/locations/global/workloadIdentityPools/<pool>/attribute.workflow_ref/hcoona/three/.github/workflows/workflow-delivery-v3-browser-store-release.yml@refs/heads/main`.
   Do not bind the wider `attribute.repository` principal set.
6. In the Chrome Web Store Developer Dashboard, open the publisher's Account
   page and add the service account email. A publisher accepts one service
   account.
7. Set the repository variables above from the provider resource name and the
   service account email.

The read and write scopes are requested per job, but Workload Identity
Federation cannot restrict a scope to a job. The residual risk is that the
non-publish jobs of this same caller on `main` (`observe` and
`verify-result`) can obtain a write-scoped token. The Chrome Web Store allows
one service account per publisher, so a separate read-only identity is not
available and this caller-on-`main` binding is the narrowest available scope.

### Store Listings

Each store must already list the extension; the caller only updates existing
listings. AMO needs the add-on's listed channel; Edge needs a published or
draft product in Partner Center.

## Owner Decisions

- `version_name` carries the NBGV npm prerelease string (`1.2.0-dev.<height>`)
  on Official, which Chrome and Edge display. The owner keeps this existing
  product behavior.
