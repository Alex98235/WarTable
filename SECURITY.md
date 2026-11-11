# Security policy

## Intended deployment

WarTable has no authentication and no TLS. It is designed to run on localhost, a
trusted LAN, or behind a reverse proxy that terminates TLS and enforces access
control.

Do **NOT** expose WarTable directly to the public internet. Anyone who can reach
the HTTP or MCP endpoint can read and modify every campaign, objective, step,
and note in the database. If you need remote access, put a hardened reverse proxy
or VPN in front of it; the container itself is not a security boundary.
Router, proxy, VPN, and firewall configuration are deployment concerns outside
WarTable's security boundary.

## Reporting a security issue

If you believe WarTable's code or a dependency has a vulnerability, report it
privately through GitHub's **Security** tab when Security Advisories are
available, or contact the maintainer directly. Do not open a public issue with
exploit details.

Only the latest published release is supported. Older tags do not receive
security fixes.
