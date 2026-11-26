# AzureAuth Unofficial V2

> [!IMPORTANT]
> **AzureAuth Unofficial V2** is an independent, unofficial, pre-release fork of
> [Microsoft Authentication CLI](https://github.com/AzureAD/microsoft-authentication-cli).
> It is not a Microsoft release and receives no Microsoft support or sponsorship. See
> [Project Governance](docs/governance/project.md) for the canonical identity, support,
> and release posture.

## Documentation

Start with:

- [Product purpose and boundary](docs/product/vision.md)
- [User goals and primary journey](docs/product/user-stories.md)
- [Current authorized work](docs/delivery-wave.md)
- [Project record index](docs/README.md)
- [Human contribution workflow](CONTRIBUTING.md)
- [Security reporting](SECURITY.md)

## Windows Portable Package

The Windows x64 command is `azureauth-unofficial`, built as an unsigned portable zip
that you extract into a directory of your choice. Unsupported `0.1.0-alpha` prereleases
may be published on this repository's GitHub Releases page after a release review. The
[package README](packaging/windows/README.md) covers installation, use from Windows and
WSL, the bundled Visual Studio compatibility Profile and its limits, and removal.

## Upstream and License

See [`UPSTREAM.md`](UPSTREAM.md) for the canonical upstream relationship and import
policy. The source is available under the [MIT License](LICENSE.txt).
