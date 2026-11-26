# AzureAuth Unofficial V2

> [!IMPORTANT]
> **AzureAuth Unofficial V2** is an independent, unofficial, pre-release authentication
> engine originally created as a clean-room `main-v2` line in a fork of
> [Microsoft Authentication CLI](https://github.com/AzureAD/microsoft-authentication-cli).
> It is not a Microsoft release and receives no Microsoft support or sponsorship. See
> [Project Identity and Posture](docs/project.md) for the canonical identity, support,
> and release posture.

## Documentation

Start with:

- [Product purpose and boundary](docs/product/vision.md)
- [User goals and primary journey](docs/product/user-stories.md)
- [Current authorized work](../../../../docs/delivery-wave.md)
- [Project record index](docs/README.md)
- [Human contribution workflow](../../../../CONTRIBUTING.md)
- [Security reporting](docs/project.md#security-reporting)

## Windows Portable Package

The Windows x64 command is `azureauth-unofficial`, built as an unsigned portable zip
that you extract into a directory of your choice. Historical `0.1.0-alpha` prereleases
through `0.1.0-alpha.37` were published on
[`hcoona/microsoft-authentication-cli` Releases](https://github.com/hcoona/microsoft-authentication-cli/releases);
future monorepo releases remain deferred until Workflow Delivery v3 onboarding. The
[package README](packaging/windows/README.md) covers installation, use from Windows and
WSL, the bundled Visual Studio compatibility Profile and its limits, and removal.

## Upstream and License

See [`UPSTREAM.md`](UPSTREAM.md) for the canonical upstream relationship and import
policy. The project is licensed under
`LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`
([`COPYING`](../../../../COPYING), [`COPYING.LESSER`](../../../../COPYING.LESSER), and
[`LGPL-3.0-linking-exception.txt`](../../../../LICENSES/LGPL-3.0-linking-exception.txt)).
