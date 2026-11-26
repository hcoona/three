# AzureAuth Unofficial V2 Upstream Provenance

## Source

- **Repository:** <https://github.com/AzureAD/microsoft-authentication-cli>
- **Upstream branch:** `main`
- **Audited baseline:** `de20930c34b3b86c8a0ed7bbdeeca3f662dae918`
- **Tag `0.9.5`:** `21258ff3a2cbb01d6891243114a55abe9ae3587e`
- **Tag `0.9.6`:** `8ef1b8b00782bf20a51de078289819a79c3cba70`
- **Baseline revalidated:** 2026-08-28
- **License:** MIT

The GitHub repository remains a fork of the upstream repository, but `main-v2` is an
orphan line with independent Git history. The reset is intended to prevent v1
compatibility and file layout from becoming implicit v2 requirements. It does not hide
or replace upstream provenance.

## Import Policy

Do not merge `upstream/main` into `main-v2`.

## Imported Source Inventory

No production source file has been imported into `main-v2`.

| V2 path | Upstream path | Upstream commit | Disposition |
| --- | --- | --- | --- |
| `LICENSE.txt` | `LICENSE.txt` | `de20930c34b3b86c8a0ed7bbdeeca3f662dae918` | Preserved MIT license and copyright notice. |

An upstream change may be imported only when:

1. it provides a mechanism, security fix, test, or operational behavior needed by an
   accepted v2 requirement;
2. the exact upstream commit and imported files are recorded in the change;
3. copyright and license notices are preserved;
4. the code is adapted to v2 contracts rather than reintroducing v1 orchestration;
5. platform, security, and behavioral validation accompanies the import.

Every copied or substantially derived file must retain the notices required by the MIT
License. Imported code remains subject to independent review; upstream origin is not
evidence that it satisfies v2 invariants.

### Security Modeling Template

[`docs/security/authentication-engine.tm7`](docs/security/authentication-engine.tm7)
embeds the public `default.tb7` template from
[`microsoft/threat-modeling-templates` at `0ece9c71b6f3710b10d497bd1ef63e57805e7c3e`](https://github.com/microsoft/threat-modeling-templates/blob/0ece9c71b6f3710b10d497bd1ef63e57805e7c3e/default.tb7).
Its Microsoft copyright and MIT permission notice are retained in the model's XML
comment. The project-specific diagram and dispositions are architecture records, not
imported production implementation.

## Developer Tooling Sources and Notices

These are development dependencies, not imported production authentication code. The
manifests and native locks own dependency selection; generated local deployments retain
upstream content. [CONTRIBUTING.md](CONTRIBUTING.md#net-and-agent-tooling) owns
contributor setup; the [tooling evidence](docs/research/experiments/developer-tooling.md)
records the bounded installation and discovery observations.

| Source | Fixed reference | Use and license |
| --- | --- | --- |
| [microsoft/apm](https://github.com/microsoft/apm/tree/v0.29.0) | v0.29.0 | APM executable and repository projections; [MIT](https://github.com/microsoft/apm/blob/v0.29.0/LICENSE). |
| [dotnet/skills](https://github.com/dotnet/skills/tree/8a5a42d3e392b402768fc29416831643b79e402b) | `8a5a42d3e392b402768fc29416831643b79e402b` | Three selected MSBuild Skills and six referenced Markdown files; .NET Foundation and Contributors, MIT notice below. |
| [microsoftdocs/mcp](https://github.com/microsoftdocs/mcp/tree/f8ffde185dfd232dbf5d187c22ce299eadc3d583) | `f8ffde185dfd232dbf5d187c22ce299eadc3d583` | Official `microsoft-docs` and `microsoft-code-reference` Skills, unchanged; documentation licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) per upstream [LICENSE](https://github.com/microsoftdocs/mcp/blob/f8ffde185dfd232dbf5d187c22ce299eadc3d583/LICENSE). Microsoft owns the source. Code uses [MIT](https://github.com/microsoftdocs/mcp/blob/f8ffde185dfd232dbf5d187c22ce299eadc3d583/LICENSE-CODE); upstream [third-party notices](https://github.com/microsoftdocs/mcp/blob/f8ffde185dfd232dbf5d187c22ce299eadc3d583/ThirdPartyNotices.md) continue to apply. |
| [Microsoft Learn CLI](https://www.npmjs.com/package/@microsoft/learn-cli/v/1.0.0) | 1.0.0 | Official CLI, MIT; transitive dependency identities and integrity values are in `tools/learn-cli/package-lock.json`. Keep package notices; the CLI archive omits a separate license file, so its upstream code notice is reproduced below. |
| [hcoona/three](https://github.com/hcoona/three/tree/516af0e99dfa4327e72c7e9bc2878b8dd26aad2f) | `516af0e99dfa4327e72c7e9bc2878b8dd26aad2f` | Reference for conventional C#/.NET, mise, and APM organization. No project license headers, package inventory, monorepo framework, or build extension is imported. |

The SDK archives are Microsoft's .NET SDK 10.0.403 distributions, and Node.js is the
22.22.2 distribution. Their archive identities are in `mise.lock`; retain
the license and third-party notices shipped with those installations. Selecting these
tools does not relicense their contents under this repository's own MIT license.

The resolved Learn graph contains 98 package entries licensed under MIT, ISC,
BSD-2-Clause, or BSD-3-Clause. Its published Node minimums are satisfied by 22.22.2.
Some packages declare development prepare/prepublish commands; installation disables
all lifecycle scripts and does not execute them. No locked entry declares an install
script. The CLI's bundled setup Skill remains package data; it is not installed into
either client's Skill directory.

### Selected .NET Skills MIT Notice

The following upstream notice accompanies the selected MSBuild Skills and their support
files, including the generated local copies:

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Microsoft Learn CLI MIT Notice

```text
The MIT License (MIT)
Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Update Policy

The fork does not promise continuous synchronization with upstream. Security and
mechanism-level fixes should be evaluated promptly, but every import must remain
intentional and reviewable.

Mutable upstream facts are rechecked only through the typed triggers and outcomes in
[`docs/research/rechecks.yaml`](docs/research/rechecks.yaml). Record the resulting
conclusion or disposition in the affected current record and its pull request. This file
remains the authority for upstream provenance, import policy, and imported-source
inventory; it is not a parallel recheck ledger.

Use the local `upstream` remote for source comparison:

```text
origin   https://github.com/hcoona/microsoft-authentication-cli.git
upstream https://github.com/AzureAD/microsoft-authentication-cli.git
```

Do not publish internal or nonpublic evidence when explaining why an upstream change is
or is not imported.
