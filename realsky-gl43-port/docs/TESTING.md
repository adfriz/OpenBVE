# Testing

## Build verification

```bash
cd source/OpenBVE
dotnet build -c Release 2>&1 | tee build.log
```

Expected:
- `Build succeeded. 0 Error(s)`
- No warnings from the new files (`RealSkyComputeShader.cs`,
  `RealSkyPass.cs`) other than possible XML-doc comment warnings
  on inherited members (harmless).

If you see errors like `The type or namespace name 'TextureAccess' could
not be found`, ensure `04c_OpenTKCompatibility_cs.patch` applied
correctly — that patch adds the enum.

## Runtime verification

Run OpenBVE once. After the splash screen, close it and open
`OpenBVE.log` (location varies by OS — see `fileSystem.GetDataFolder()`
output for the exact path). Look for these lines:

### On GL 4.3+ hardware (compute path)

```
[RealSky] GL 4.6 | Compute=True ImageLoad=True → UseComputePath=True
[RealSky] Initialized compute path: 1920x1080 RGBA16F image, unit 8
```

If you see these lines, the compute path is active. The sky image is
allocated at screen resolution and bound to texture unit 8.

### On GL 3.3 / 4.2 hardware (fallback path)

```
[RealSky] GL 4.2 | Compute=False ImageLoad=False → UseComputePath=False
[RealSky] Initialized fragment-shader fallback path (GL < 4.3)
```

### If initialization failed

```
[RealSky] Init failed: <exception message>
```

Common causes:
- Shader compile error — check the message for the GLSL line number.
  Most likely cause: missing `#version 430 core` directive or a typo
  in a uniform name.
- `GL.BindImageTexture` not found in driver — your driver is older than
  advertised; force the fallback by setting `UseComputePath = false`
  manually in `RealSkyPass.Initialize()`.

## Visual verification

1. **Sky renders** — load any route. The sky should show:
   - A blue gradient from horizon (lighter) to zenith (darker)
   - A bright sun disk in the position determined by
     `RealSkyAzimuth` / `RealSkyElevation`
   - Animated volumetric clouds (off by default; visible when looking
     towards the horizon with the camera above ground level)

2. **Sun position changes** — open `options.cfg`, change
   `RealSkyAzimuth` from `180` to `90`, restart OpenBVE. The sun disk
   should move from South to East.

3. **Cloud animation** — leave OpenBVE running for 30 seconds. The cloud
   shapes should drift slowly across the sky (driven by `uRealSkyTime`).

4. **Reflections in default shader** — if you have a route with water
   surfaces, the water should show a faint sky reflection. This requires
   extending `default.frag` to sample `uSkyTexture` (see
   `docs/INTEGRATION.md`).

5. **Performance** — the compute dispatch at 1080p with 8x8 workgroups
   should take <2 ms on a GTX 1660. If you see >5 ms, check:
   - `gl_GlobalInvocationID.xy` bounds guard is in place (it is — line
     `if (px.x >= int(uRealSkyResolution.x) || px.y >= int(uRealSkyResolution.y)) return;`)
   - The sky image isn't being reallocated every frame (check
     `AllocateSkyImage` — it short-circuits when the size matches)

## Regression tests

After applying this port, verify that these existing systems still work:

| System | How to verify |
|---|---|
| Clustered Forward Rendering | Load a route with many `SceneLight` objects. The `[CFR]` log lines should appear unchanged. Light counts on the debug HUD should match pre-patch values. |
| Cascaded Shadow Maps | Shadows should render correctly. The `[CSM]` log lines should appear unchanged. |
| Background cross-fade | The original background image should still render. With RealSky enabled, the background draw runs after `BindRealSkyToDefaultShader()`, so the default shader may sample the sky texture — but since `uSkyEnabled` defaults to `false` until `BindSkyToShader` runs, there's no risk of sampling an unbound texture. |
| Route viewer / Object viewer | These don't use `NewRenderer` (they have their own `NewRendererR` / `NewRendererS`). They should be completely unaffected. |
| Legacy renderer | Users on `IsUseNewRenderer = false` never hit `PerformRealSkyPass()` (the call is in `NewRenderer.RenderScene` only). No change to legacy path. |

## Test matrix

Run through this matrix on at least one configuration per row:

| OS | GPU | GL version | Expected path | Visual check |
|---|---|---|---|---|
| Windows 11 | NVIDIA RTX 30xx | 4.6 | Compute | Sky + clouds render correctly |
| Windows 11 | Intel Iris Xe | 4.6 | Compute | Sky + clouds render correctly |
| Windows 10 | AMD Radeon RX 580 | 4.6 | Compute | Sky + clouds render correctly |
| Linux (Mesa) | Intel Skylake GT2 | 4.6 | Compute | Sky + clouds render correctly |
| Windows 11 (VMware) | SVGA3D | 4.1 | Fallback | Sky renders via skybox draw (no compute) |
| Windows 10 (Parallels) | Generic | 3.3 | Fallback | Sky renders via skybox draw |

## Logs to attach when reporting a bug

If RealSky doesn't render correctly, please attach:

1. The `OpenBVE.log` file (with `[RealSky]` lines visible)
2. The output of `glxinfo | grep -i opengl` (Linux) or GPU Caps Viewer
   output (Windows)
3. A screenshot of the sky
4. The contents of `options.cfg`'s `[RealSky]` section
5. The route file (or a minimal reproducer) so we can replay the scene
