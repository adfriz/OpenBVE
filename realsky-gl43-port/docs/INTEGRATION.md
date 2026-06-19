# Integration Notes

## Where RealSky sits in the rendering pipeline

```
Frame start
  │
  ├─ 1. CSM shadow depth pass         (existing — unchanged)
  ├─ 2. CFR light cull + SSBO upload  (existing — unchanged)
  ├─ 3. RealSky compute dispatch       ← NEW
  ├─ 4. Default shader activate
  ├─ 5. Bind CSM shadow maps           (existing)
  ├─ 6. Bind CFR cluster SSBOs         (existing)
  ├─ 7. Bind RealSky sky texture       ← NEW
  ├─ 8. Background cross-fade draw     (existing)
  └─ 9. Opaque / alpha / overlay geometry draws
```

The RealSky pass runs **after** the shadow + CFR setup (so it doesn't
disturb any SSBO bindings or shadow map state) and **before** the
background draw (so the sky image is already in the sampler when
`DefaultShader` first runs).

## Adding a UI tab (suggested follow-up)

The three options (`RealSkyEnabled`, `RealSkyAzimuth`, `RealSkyElevation`)
are saved/loaded but there is no in-game form to edit them yet. A minimal
follow-up patch would add a tab to `formOptions.cs`:

```csharp
// In formOptions.cs — add a new TabPage 'tabRealSky'
private CheckBox checkBoxRealSkyEnabled;
private NumericUpDown numericRealSkyAzimuth;
private NumericUpDown numericRealSkyElevation;

private void tabRealSky_Load(object sender, EventArgs e)
{
    checkBoxRealSkyEnabled.Checked = CurrentOptions.RealSkyEnabled;
    numericRealSkyAzimuth.Value = (decimal)CurrentOptions.RealSkyAzimuth;
    numericRealSkyElevation.Value = (decimal)CurrentOptions.RealSkyElevation;
}

private void tabRealSky_Save(object sender, EventArgs e)
{
    CurrentOptions.RealSkyEnabled = checkBoxRealSkyEnabled.Checked;
    CurrentOptions.RealSkyAzimuth = (double)numericRealSkyAzimuth.Value;
    CurrentOptions.RealSkyElevation = (double)numericRealSkyElevation.Value;
}
```

If the user changes RealSky options at runtime, you'll also want to call
`Program.Renderer.RealSkyPass?.Initialize()` to rebuild GPU resources
(mostly to recompile the shader if the user toggled between paths — the
compute shader itself doesn't depend on the sun angles, so re-init is
optional for sun-angle-only changes).

## Hooking RealSky to scene lights

By default the RealSky sun direction comes from
`RealSkyAzimuth` / `RealSkyElevation` in the user's options (or route
override). If you want the sun disk in the sky shader to match a
directional `SceneLight` placed by the route, you can replace the sun
direction computation in `NewRenderer.RenderScene`:

```csharp
// Current (default):
double ra = az * 0.0174532925199433;
double re = el * 0.0174532925199433;
Vector3 sunDir = new Vector3(Math.Sin(ra) * Math.Cos(re), Math.Sin(re), Math.Cos(ra) * Math.Cos(re));

// Optional: use first directional scene light instead
if (Program.Renderer.ActiveSceneLights.Count > 0)
{
    var firstLight = Program.Renderer.ActiveSceneLights[0];
    if (firstLight.Type == LightType.Directional)
    {
        sunDir = firstLight.Direction;
        sunDir.Normalize();
    }
}
```

This keeps the atmospheric scattering visually aligned with whatever
directional light the route places, which matters once CFR is rendering
many lights and the sun is no longer the only thing illuminating the scene.

## Using the sky image in a future reflection pass

Once the sky image exists on texture unit 8, any subsequent shader can
sample it. For example, to add sky reflection to water in a future
`default.frag` extension:

```glsl
// In default.frag — add at the top
uniform bool  uSkyEnabled;
uniform sampler2D uSkyTexture;

// At the bottom of main(), for water materials:
if (uSkyEnabled && isWater) {
    vec3 reflectedDir = reflect(-viewDir, normal);
    vec2 skyUv = reflectedDir.xy * 0.5 + 0.5;
    vec3 skyReflection = texture(uSkyTexture, skyUv).rgb;
    finalColor = mix(finalColor, skyReflection, 0.4);
}
```

The `uSkyEnabled` / `uSkyTexture` uniforms are already wired up by this
port — `SetSkyEnabled(true)` is called from `RealSkyPass.BindSkyToShader`
and `SetSkyTextureUnit(8)` is called immediately after.

## Coexistence with CFR

RealSky and CFR are completely independent subsystems:

| Concern | CFR | RealSky |
|---|---|---|
| Uses SSBOs | Yes (4×: light data, light grid, light indices, cluster AABBs) | No |
| Uses image textures | No | Yes (1×: RGBA16F sky image) |
| Uses compute shaders | Yes (`cluster_culling.comp`) | Yes (`RealSky.comp`) |
| Texture units used | None directly (SSBOs use bindings 0..3) | Unit 8 (sky sampler) |
| Shadow map units | 4..7 (CSM) | — |
| Requires GL 4.3 | With fallback to UBO path | With fallback to fragment-shader skybox |

There is no shared state to worry about. The `MemoryBarrier` call after
the RealSky dispatch only affects the sky image — it does not touch CFR's
SSBOs.

## Coexistence with CSM (Cascaded Shadow Maps)

The RealSky pass runs with `GL.DepthMask(false)` and `GL.Disable(DepthTest)`
in the fallback path. On the compute path it doesn't touch the depth
buffer at all (it writes to an image, not the framebuffer). CSM state
is therefore unaffected.

## Disabling RealSky at runtime

If the user sets `RealSkyEnabled = false`, the early-return guard in
`NewRenderer.RenderScene` skips the `PerformRealSkyPass()` call entirely.
The `RealSkyPass` instance still exists on the renderer (it was created
during `Initialize()`) but does no work per frame.

To free GPU resources entirely (e.g. when the user explicitly wants to
disable RealSky in low-power mode), call:

```csharp
Program.Renderer.RealSkyPass?.Dispose();
Program.Renderer.RealSkyPass = null;
```

To re-enable later, recreate the pass:

```csharp
Program.Renderer.RealSkyPass = new Atmosphere.RealSkyPass(Program.Renderer);
Program.Renderer.RealSkyPass.Initialize();
```

This mirrors the pattern used by `ReloadShadowSettings()` for CSM.

## Why the sky image is RGBA16F

The compute shader produces HDR sky colors (sun disk + atmospheric glow
can exceed 1.0). RGBA16F gives 5 stops of headroom without banding, and
is the same format used by the SSAO post-processing pass on the
`test-SSAO` branch — keeping the format consistent makes future
integration with HDR output pipelines trivial.

If you need lower memory bandwidth on integrated GPUs, RGBA8 (LDR) is a
viable alternative at the cost of visible banding in the sun disk.
Change `SizedInternalFormat.Rgba16f` → `SizedInternalFormat.Rgba8` in
both `RealSkyPass.AllocateSkyImage()` and `RealSkyPass.RenderCompute()`
(the `GL.BindImageTexture` call).
