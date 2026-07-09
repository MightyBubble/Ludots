# Control Plane Projection Showcase

RFC-0065 A1 includes a CEF WebApp panel built from `WebApp` and packaged under:

`assets/control-plane-app/index.html`

DataPlane contract:

- topic: `ludots.showcase.control_plane.state`
- command: `toggleProxy`
- session: `control-plane-projection-showcase`

Build and verify the panel:

```powershell
cd WebApp
npm install
npm run test
npm run build
```
