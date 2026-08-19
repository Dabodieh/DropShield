# Security policy

DropShield is a proof of concept. It has undergone one internal review pass and targeted
remediation, and real Adobe Commerce / Mage-OS runtime validation for the connector (see
[docs/adobe-commerce.md](docs/adobe-commerce.md)), but it has not been independently
penetration-tested and is not production-hardened. Treat findings against it as PoC-level
issues, not as a claim about any deployed system.

## Reporting a vulnerability

Please use [GitHub's private vulnerability reporting](../../security/advisories/new) for this
repository rather than opening a public issue. If that is not available, open an issue that
states only that a security report exists, without exploit details, and ask for a private
contact channel.

Do not include live credentials, tokens, or working exploit payloads in a public issue, pull
request, or discussion.

There is no fixed response-time guarantee. This is a personal/PoC project, not a supported
product with an SLA.

## Scope

This repository must not be used to test, scan, or send traffic to any third-party system,
including any retailer referenced in the documentation for architectural research purposes. All
automated traffic generation and load testing here targets only localhost or infrastructure the
tester owns — see the safety boundary in [README.md](README.md). Reports describing testing
against a third party without authorization will not be treated as responsible disclosure.
