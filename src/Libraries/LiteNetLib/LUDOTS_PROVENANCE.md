# LiteNetLib source provenance

- Upstream: `https://github.com/RevenantX/LiteNetLib`
- Version: `2.1.4`
- Commit: `4d3de1e93abaead30199bf572f4a3363f854e14b`
- License: MIT, retained in `LICENSE.txt`

Ludots builds this pinned source instead of the NuGet binary because the upstream
release binary compiles deterministic network simulation out of Release builds.
The local patch keeps `SIMULATE_NETWORK` enabled, exposes startup-only seeded
simulation configuration, disables allocation-heavy outbound latency simulation,
and enforces the configured inbound simulation capacity.
