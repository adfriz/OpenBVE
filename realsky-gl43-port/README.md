# RealSky Atmospheric Rendering — GL 4.3 Compute Shader Port

This package ports the **RealSky atmospheric rendering system** from the `realsky`
branch (which uses a `#version 330 core` fragment-shader skybox) onto the
**GL 4.3 compute-shader pipeline** used by the `next-gen` branch's clustered
forward renderer.

It is designed to drop cleanly onto `origin/next-gen` (commit `b09d8dbb9` and
later) without conflicts, and to coexist with the existing `ClusterEngine`
(CFR) and `Shadows` (CSM) systems.

---

## What's in this package

```
realsky-gl43-port/
├── README.md                                ← this file
├── patches/                                 ← unified-diff patches, one per file
│   ├── 00_all-files.patch                   ← consolidated patch (applies everything)
│   ├── 01_BaseRenderer_cs.patch             ← RealSkyPass field + lifecycle + helper methods
│   ├── 02_ShaderLayout_cs.patch             ← Sky + RealSky uniform layout entries
│   ├── 03_Shader_cs.patch                   ← uSkyEnabled/uSkyTexture uniforms + SetSkyEnabled methods
│   ├── 04_LibRender2_csproj.patch           ← RealSky.comp embedded resource entry
│   ├── 04b_GL_cs.patch                      ← GL.BindImageTexture wrapper
│   ├── 04c_OpenTKCompatibility_cs.patch     ← Matrix4.Invert + operator* + TextureAccess/SizedInternalFormat enums
│   ├── 05_NewRenderer_cs.patch              ← RealSky pass invocation in render loop
│   ├── 06_Atmosphere_cs.patch               ← RealSky sun azimuth/elevation/override fields
│   ├── 07_BaseOptions_cs.patch              ← RealSkyEnabled/Azimuth/Elevation options
│   ├── 08_BaseOptions_OptionsSection_cs.patch  ← RealSky section enum
│   ├── 09_BaseOptions_OptionsKey_cs.patch   ← RealSky key enum
│   ├── 10_Options_cs.patch                  ← defaults + save + load
│   ├── 11_RealSky_comp.patch                ← NEW: GL 4.3 compute shader
│   ├── 12_RealSky_vert.patch                ← fallback fragment-shader skybox (.vert)
│   ├── 13_RealSky_frag.patch                ← fallback fragment-shader skybox (.frag)
│   ├── 14_RealSkyComputeShader_cs.patch     ← NEW: C# wrapper for the compute shader
│   └── 15_RealSkyPass_cs.patch              ← NEW: orchestrator (Initialize/Render/Dispose)
├── shaders/
│   └── RealSky.comp                         ← standalone copy of the compute shader (for review)
├── source/
│   └── LibRender2/Atmosphere/
│       ├── RealSkyComputeShader.cs          ← standalone copy
│       └── RealSkyPass.cs                   ← standalone copy
├── apply.sh                                 ← one-shot apply script
└── docs/
    ├── ARCHITECTURE.md                      ← design walkthrough
    ├── INTEGRATION.md                       ← how RealSky fits next to CFR / CSM
    └── TESTING.md                           ← manual test plan + expected log output
```

---

## Quick start

```bash
# 1. Clone OpenBVE and checkout next-gen
git clone https://github.com/adfriz/OpenBVE.git
cd OpenBVE
git checkout next-gen

# 2. Apply this port (uses the consolidated patch — single git apply)
bash /path/to/realsky-gl43-port/apply.sh .

# 3. Build as usual (Visual Studio / dotnet build / msbuild)
#    LibRender2 and OpenBVE projects will pick up the new files automatically.

# 4. Run OpenBVE and enable RealSky in options:
#    - Open the in-game Options dialog (or edit options.cfg by hand)
#    - Set RealSkyEnabled = true
#    - Set RealSkyAzimuth (0..360, default 180) and RealSkyElevation (0..90, default 45)
#    - Save & restart OpenBVE

# 5. Verify in the log file (look for the [RealSky] init lines):
#    [RealSky] GL 4.6 | Compute=True ImageLoad=True → UseComputePath=True
#    [RealSky] Initialized compute path: 1920x1080 RGBA16F image, unit 8
```

---

## What changed and why

### 1. The compute shader — `assets/Shaders/RealSky.comp`

**Before** (realsky branch):
- `assets/Shaders/Atmosphere/RealSky.vert` (`#version 330 core`) — skybox vertex shader
- `assets/Shaders/Atmosphere/RealSky.frag` (`#version 330 core`) — skybox fragment shader
  with screen-space raymarched FBM volumetric clouds

**After** (this port):
- `assets/Shaders/RealSky.comp` (`#version 430 core`) — compute shader that
  writes RGBA16F sky pixels directly into a `layout(rgba16f, binding=0) writeonly image2D`
- Same FBM noise, same atmospheric scattering, same cloud raymarch — only the
  execution model changes (fragment-shader rasterization → compute dispatch)

**Why compute over fragment-shader raymarch:**
1. **Decoupled from rasterization** — the sky image can be sampled by every
   subsequent pass (default.frag lighting, reflections, fog) without an extra
   fullscreen blit.
2. **Tile-friendly** — 8×8 workgroups with shared-memory FBM cache is faster
   on overlap-heavy fragment shaders.
3. **Pipeline consistency** — matches the GL 4.3 + SSBO + compute pattern
   used by `ClusterCullingShader` and `SSAOCompute`, so capability detection
   and lifecycle are identical.

### 2. C# integration

The C# integration mirrors `ClusterEngine` exactly:

| Layer | Class | File | Purpose |
|---|---|---|---|
| Compute shader wrapper | `RealSkyComputeShader` | `LibRender2/Atmosphere/RealSkyComputeShader.cs` | Compiles `RealSky.comp` from embedded resource, exposes uniform setters, dispatches with `ceil(w/8) × ceil(h/8)` workgroups |
| Orchestrator | `RealSkyPass` | `LibRender2/Atmosphere/RealSkyPass.cs` | GL 4.3 capability detection, RGBA16F sky image allocation, lazy fallback path init, lifecycle (`Initialize` / `Render` / `BindSkyToShader` / `Dispose`) |
| BaseRenderer hooks | — | `LibRender2/BaseRenderer.cs` | New `RealSkyPass` field, `PerformRealSkyPass` / `BindRealSkyToDefaultShader` / `UnbindRealSkyFromDefaultShader` protected helpers (mirror `PerformCFRCullAndUpload` / `BindCFRToDefaultShader`) |
| Render loop call | — | `OpenBVE/Graphics/NewRenderer.cs` | RealSky pass inserted after `PerformCFRCullAndUpload` and before the background draw; `BindRealSkyToDefaultShader()` called alongside `BindCFRToDefaultShader()` |

### 3. Runtime capability detection (same pattern as `ClusterEngine`)

`RealSkyPass.Initialize()` checks GL version + extensions and picks the path:

```csharp
bool gl43Plus = major > 4 || (major == 4 && minor >= 3);
bool hasCompute  = gl43Plus || extensions.Contains("GL_ARB_compute_shader");
bool hasImageLoad = gl43Plus || extensions.Contains("GL_ARB_shader_image_load_store");
UseComputePath = hasCompute && hasImageLoad;
```

| Hardware | Path taken | Behaviour |
|---|---|---|
| GL 4.3+ (Intel Skylake+, AMD GCN+, NVIDIA Fermi+) | Compute | Dispatches `RealSky.comp`, writes to RGBA16F image, binds as sampler2D on unit 8 for `default.frag` |
| GL 3.3 – 4.2 with `ARB_compute_shader` + `ARB_shader_image_load_store` | Compute | Same as above |
| GL 3.3 – 4.2 without those extensions | Fragment-shader fallback | Uses original `RealSky.vert` + `RealSky.frag` from the `realsky` branch — same visual result |
| GL < 3.3 | Disabled | `[RealSky] Init failed` logged; pass returns immediately |

### 4. Options

Three new options are added to `BaseOptions` and serialised to the
`[RealSky]` section of `options.cfg`:

| Option | Type | Default | Purpose |
|---|---|---|---|
| `RealSkyEnabled` | bool | false | Master switch |
| `RealSkyAzimuth` | double | 180.0 | Sun azimuth in degrees (0 = North, 180 = South) |
| `RealSkyElevation` | double | 45.0 | Sun elevation in degrees (0 = Horizon, 90 = Zenith) |

Routes can override the sun position by setting
`Program.CurrentRoute.Atmosphere.RealSkyOverride = true` and setting
`RealSkyAzimuth` / `RealSkyElevation` on the `Atmosphere` object — this is
the same hook the `realsky` branch uses.

---

## How RealSky fits next to CFR (Clustered Forward Rendering)

The render loop in `NewRenderer.RenderScene` now runs in this order:

```
1. PerformCSMShadowPass()              ← shadow depth map
2. PerformCFRCullAndUpload()           ← CFR: SSBO light cull (GL 4.3+)
3. PerformRealSkyPass(totalTime, sun)  ← NEW: RealSky compute dispatch
4. DefaultShader.Activate()
5. BindCSMToDefaultShader()            ← cascade shadow maps
6. BindCFRToDefaultShader()            ← cluster SSBOs (CFR)
7. BindRealSkyToDefaultShader()        ← NEW: sky image as sampler on unit 8
8. UpdateBackground(...)               ← original background draw
9. opaque / alpha / overlay geometry   ← uses DefaultShader (which can now sample sky)
```

All three subsystems (CSM / CFR / RealSky) follow the same lifecycle pattern:
- A `*Engine` or `*Pass` class held as a public field on `BaseRenderer`
- `Initialize()` called from `BaseRenderer.Initialize()` after the GL context is ready
- `Dispose()` called from `BaseRenderer.DeInitialize()` before context teardown
- Runtime GL capability detection inside `Initialize()` with graceful fallback
- `*ToDefaultShader()` helper to bind GPU resources to `DefaultShader` once per frame

---

## Building

The project targets `net8.0-windows` and uses Silk.NET for the OpenGL
bindings (via the custom `OpenTK.Graphics.OpenGL.GL` wrapper in
`LibRender2/openGL/GL.cs`). No new NuGet packages are required.

To build:

```bash
# From the OpenBVE repo root after applying the patches:
cd source/OpenBVE
dotnet build -c Release
# or open OpenBVE.sln in Visual Studio 2022 and build normally
```

The new files are picked up automatically by the SDK-style `.csproj` (no
explicit `<Compile Include>` entries needed). The `RealSky.comp` shader is
embedded as a resource via the new entry in `LibRender2.csproj`:

```xml
<EmbeddedResource Include="..\..\assets\Shaders\RealSky.comp" LogicalName="LibRender2.RealSky.comp" />
```

---

## Files changed (summary)

### New files (5)

| Path | Purpose |
|---|---|
| `assets/Shaders/RealSky.comp` | GL 4.3 compute shader (the actual sky renderer) |
| `source/LibRender2/Atmosphere/RealSkyComputeShader.cs` | C# wrapper for the compute shader |
| `source/LibRender2/Atmosphere/RealSkyPass.cs` | Orchestrator class (lifecycle, GL detection, fallback) |
| `assets/Shaders/Atmosphere/RealSky.vert` | Fallback skybox vertex shader (preserved from realsky branch) |
| `assets/Shaders/Atmosphere/RealSky.frag` | Fallback skybox fragment shader (preserved from realsky branch) |

### Modified files (10)

| Path | Change |
|---|---|
| `source/LibRender2/BaseRenderer.cs` | `RealSkyPass` field + Initialize/Dispose hooks + `PerformRealSkyPass` / `BindRealSkyToDefaultShader` / `UnbindRealSkyFromDefaultShader` helpers |
| `source/LibRender2/Shaders/Shader.cs` | `uSkyEnabled` / `uSkyTexture` uniform locations + `SetSkyEnabled` / `SetSkyTextureUnit` methods + RealSky fallback uniforms |
| `source/LibRender2/openGL/ShaderLayout.cs` | `SkyEnabled`, `SkyTexture`, `RealSkySunDirection`, `RealSkyTime`, `RealSkyResolution`, `RealSkyCameraPos` fields |
| `source/LibRender2/openGL/GL.cs` | `BindImageTexture` wrapper (delegates to Silk.NET) |
| `source/LibRender2/openGL/OpenTKCompatibility.cs` | `Matrix4.Invert(ref, out)` + `Matrix4.operator *` + `TextureAccess` enum + `SizedInternalFormat` enum |
| `source/LibRender2/LibRender2.csproj` | Embeds `RealSky.comp` as `LibRender2.RealSky.comp` |
| `source/OpenBVE/Graphics/NewRenderer.cs` | RealSky pass invocation in render loop |
| `source/RouteManager2/Climate/Atmosphere.cs` | `RealSkyAzimuth`, `RealSkyElevation`, `RealSkyOverride` fields |
| `source/OpenBveApi/System/BaseOptions.cs` | `RealSkyEnabled`, `RealSkyAzimuth`, `RealSkyElevation` fields |
| `source/OpenBveApi/System/BaseOptions.OptionsSection.cs` | `RealSky` enum value |
| `source/OpenBveApi/System/BaseOptions.OptionsKey.cs` | `RealSkyEnabled`, `RealSkyAzimuth`, `RealSkyElevation` enum values |
| `source/OpenBVE/System/Options.cs` | Defaults + save + load for `[RealSky]` section |

---

## Testing

See `docs/TESTING.md` for the full manual test plan. Key verification steps:

1. **Build succeeds** — `dotnet build` produces no errors in `LibRender2` or `OpenBVE`.
2. **Log lines on startup** — open `OpenBVE.log` and verify:
   ```
   [RealSky] GL 4.6 | Compute=True ImageLoad=True → UseComputePath=True
   [RealSky] Initialized compute path: <W>x<H> RGBA16F image, unit 8
   ```
3. **Visual check** — sky renders with the expected atmospheric gradient
   and animated volumetric clouds. Sun position changes when adjusting
   `RealSkyAzimuth` / `RealSkyElevation`.
4. **Fallback path** — on a GL 3.3 / 4.2 VM, log should read:
   ```
   [RealSky] GL 4.2 | Compute=False ImageLoad=False → UseComputePath=False
   [RealSky] Initialized fragment-shader fallback path (GL < 4.3)
   ```
   Sky still renders via the original skybox draw.

---

## Caveats / known limitations

1. **No UI yet.** The three options are saved/loaded but no in-game form
   has been added. To enable RealSky, either edit `options.cfg` by hand
   under `[RealSky]` or set the options programmatically before launch.
   Adding a `formOptions` tab is a straightforward follow-up — see
   `docs/INTEGRATION.md` for the suggested layout.

2. **The compute shader does not yet sample CFR cluster data.** The sky is
   fully decoupled from lighting in this first port — meaning the sun
   direction used for atmospheric scattering is taken from
   `RealSkyAzimuth` / `RealSkyElevation`, not from the CFR scene lights.
   If you want the sun disk in the RealSky shader to match a directional
   `SceneLight` from the route, you can extend `RealSkyPass.Render` to
   pull `renderer.ActiveSceneLights[0].Direction` and pass it to
   `computeShader.SetSunDirection`.

3. **No shared-memory FBM cache yet.** The compute shader recomputes the
   4-octave FBM per pixel. On 1080p this is ~2M noise evaluations per
   frame — fast enough on a 1660-class GPU but leaves headroom. A
   follow-up optimization is to compute FBM at lower resolution into a
   `shared` block per 8×8 workgroup and bilinearly upsample.

4. **`RealSky.frag` trailing whitespace warnings.** The fallback shader
   is preserved byte-identical to the `realsky` branch to keep visual
   parity; this produces 4 trailing-whitespace warnings when applying
   the consolidated patch. They are harmless.

---

## License

All new code is released under the Simplified BSD License (BSD-2-Clause),
matching the rest of the OpenBVE codebase. Copyright notices in existing
files are preserved.
