# Security Policy

## Supported versions

Security fixes are provided for the latest stable minor release in the current major line.
Pre-release builds and older major lines may receive a fix at the maintainers' discretion.

| Version | Security support |
|---|---|
| Latest `1.x` | Supported |
| Older `1.x` | Upgrade to the latest `1.x` |
| `0.x` and previews | Not supported |

Target-framework support follows the [versioning and support policy](https://thomhurst.github.io/Kevlar/docs/support-policy).

## Reporting a vulnerability

Do not open a public issue. Use GitHub's private
[security advisory form](https://github.com/thomhurst/Kevlar/security/advisories/new) and include:

- affected package and version;
- target framework and runtime;
- impact and realistic attack scenario;
- minimal reproduction or proof of concept; and
- any known mitigation.

You should receive an acknowledgement within seven days. The maintainer will validate the report,
coordinate a fix and disclosure date, and credit the reporter unless anonymity is requested.
Please allow a reasonable remediation window before public disclosure.

Security advisories and patched releases are published through GitHub. General bugs and support
questions belong in the public issue templates.
