# Architecture

## Design goals

1. **Bring RealSky onto the GL 4.3 compute path** used by the rest of the
   `next-gen` branch (CFR, SSAO).
2. **Mirror the existing `ClusterEngine` pattern** so the lifecycle,
   capability detection, and integration points are familiar to anyone
   who has read the CFR code.
3. **Preserve the original fragment-shader skybox as a fallback** for
   GL 3.3 / 4.2 hardware so the feature degrades gracefully.
4. **Make the sky image available as a sampler** to downstream passes
   (default.frag lighting, future reflection / fog passes).

## Class diagram

```
                       ┌──────────────────────────┐
                       │       BaseRenderer       │
                       │  (LibRender2 root)       │
                       └────────────┬─────────────┘
                                    │ owns
              ┌─────────────────────┼──────────────────────┐
              │                     │                      │
   ┌──────────▼─────────┐  ┌────────▼─────────┐  ┌─────────▼──────────┐
   │      Shadows       │  │   ClusterEngine  │  │    RealSkyPass     │  ← NEW
   │   (CSM shadows)    │  │      (CFR)       │  │   (atmosphere)     │
   └──────────┬─────────┘  └────────┬─────────┘  └─────────┬──────────┘
              │                     │                      │
              │                     │ has-a                │ has-a
              │                     │                      │
              │            ┌────────▼──────────┐  ┌─────────▼──────────┐
              │            │ClusterCullingShader│  │RealSkyComputeShader│  ← NEW
              │            │  (.comp wrapper)   │  │  (.comp wrapper)   │
              │            └───────────────────┘  └────────────────────┘
              │                     │                      │
              │             uses SSBOs (4×)         uses image2D (1×)
              │                     │                      │
   ┌──────────▼─────────────────────▼──────────────────────▼──────────┐
   │                    DefaultShader (Shader.cs)                     │
   │  uSkyEnabled, uSkyTexture uniforms (NEW)                         │
   │  uShadowMap0..3, uClusteringEnabled (existing)                   │
   └───────────────────────────────────────────────────────────────────┘
```

## Lifecycle comparison

| Stage | `Shadows` (CSM) | `ClusterEngine` (CFR) | `RealSkyPass` (NEW) |
|---|---|---|---|
| Field on `BaseRenderer` | `Shadows` | `ClusterEngine` | `RealSkyPass` |
| Created in | `Initialize()` | `Initialize()` | `Initialize()` |
| GL capability detection | always-on | `gl43Plus \|\| ARB_*` | `gl43Plus \|\| ARB_*` |
| Per-frame call | `PerformCSMShadowPass()` | `PerformCFRCullAndUpload()` | `PerformRealSkyPass()` |
| Per-frame bind | `BindCSMToDefaultShader()` | `BindCFRToDefaultShader()` | `BindRealSkyToDefaultShader()` |
| Dispose in | `DeInitialize()` | `DeInitialize()` | `DeInitialize()` |

## Per-frame render loop (NewRenderer.RenderScene)

```
Step  Action                                    GPU resources touched
────  ───────────────────────────────────────── ────────────────────────
1     GL.Clear                                   framebuffer
2     Compute CurrentViewMatrix                  —
3     PerformCSMShadowPass()                     shadow depth FBO
4     PerformCFRCullAndUpload()                  4× SSBO (light data, grid, indices, AABBs)
                                                + compute dispatch (cluster_culling.comp)
5     PerformRealSkyPass(t, sunDir)              ← NEW
       ↳ if UseComputePath:                     sky image (RGBA16F, write)
           GL.BindImageTexture(0, skyImage, …)
           computeShader.Activate()
           computeShader.Set*(…)
           computeShader.Dispatch(w/8, h/8, 1)
           GL.MemoryBarrier(ImageAccess | TextureFetch)
           GL.ActiveTexture(Texture0 + 8)
           GL.BindTexture(Texture2D, skyImage)
       ↳ else (fallback):
           InitializeFallback() if first call
           fallbackShader.Activate()
           GL.DrawArrays(Triangles, 0, 36)  ← skybox cube
6     DefaultShader.Activate()
7     BindCSMToDefaultShader()                   shadow map samplers on units 4..7
8     BindCFRToDefaultShader()                   cluster SSBOs on bindings 0..3
9     BindRealSkyToDefaultShader()               ← NEW
       ↳ shader.SetSkyEnabled(true)
       ↳ shader.SetSkyTextureUnit(8)
10    UpdateBackground(...)                      background cross-fade
11    opaque / alpha / overlay draws             DefaultShader samples sky on unit 8
```

## Compute shader pipeline

```glsl
#version 430 core
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, binding = 0) writeonly uniform image2D uSkyImage;

uniform vec3  uRealSkySunDirection;
uniform vec3  uRealSkyCameraPos;
uniform float uRealSkyTime;
uniform vec2  uRealSkyResolution;
uniform mat4  uRealSkyInvViewProj;
uniform float uRealSkyExposure;

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    if (px.x >= width || px.y >= height) return;

    // Reconstruct world-space view dir from NDC pixel
    vec3 viewDir = worldViewDir(px);

    // Atmospheric scattering (sky color)
    vec3 sky = getAtmosphere(viewDir);

    // Raymarched volumetric clouds (16 steps, FBM density)
    vec3  cloudColor = vec3(0.0);
    float alpha = 0.0;
    if (viewDir.y > 1e-4) { /* 16-step loop */ }

    vec3 finalColor = mix(sky, cloudColor, alpha);
    finalColor *= uRealSkyExposure;
    finalColor  = finalColor / (finalColor + 1.0); // Reinhard tonemap

    imageStore(uSkyImage, px, vec4(finalColor, 1.0));
}
```

The 8×8 workgroup size matches SSAO's compute pass, giving good occupancy
on Intel / AMD / NVIDIA iGPUs alike. One workgroup covers 64 pixels.

## Fallback path (GL 3.3 – 4.2)

When `UseComputePath == false`, `RealSkyPass` lazily loads the original
`RealSky.vert` + `RealSky.frag` from the `realsky` branch (preserved in
`assets/Shaders/Atmosphere/`). Resources are created on the first
`RenderFallback()` call so the fallback path consumes no GPU memory when
the compute path is in use.

The fallback uses the same uniform layout fields
(`RealSkySunDirection`, `RealSkyTime`, `RealSkyResolution`, `RealSkyCameraPos`)
that the `realsky` branch added to `ShaderLayout.cs`.

## Memory layout

| Resource | Compute path | Fallback path |
|---|---|---|
| Sky image (RGBA16F, screen-sized) | Allocated in `RealSkyPass.Initialize()`, reallocated on screen resize | Not used |
| Skybox VAO + 36-vertex cube | Not used | Allocated lazily in `InitializeFallback()` |
| Compute shader program | Loaded from embedded `LibRender2.RealSky.comp` | Not used |
| Fragment shader program | Not used | Loaded from `assets/Shaders/Atmosphere/RealSky.vert` + `.frag` |
| Texture unit 8 | Sky sampler for `DefaultShader` | Not used |
| Texture units 0..7 | Unchanged (scene textures, shadow maps) | Unchanged |

## Capability detection matrix

| GL version | Extensions present | Path | Log line |
|---|---|---|---|
| 4.3+ | (none required) | **Compute** | `[RealSky] GL 4.x \| Compute=True ImageLoad=True → UseComputePath=True` |
| 4.2 | `ARB_compute_shader` + `ARB_shader_image_load_store` | **Compute** | Same as above |
| 4.2 | Missing one or both extensions | **Fallback** | `[RealSky] GL 4.2 \| Compute=False ImageLoad=False → UseComputePath=False` |
| 3.3 | Either extension present | **Compute** (if both) | Same pattern |
| 3.3 | Neither extension | **Fallback** | Same as above |
| < 3.3 | — | **Disabled** | `[RealSky] Init failed: …` |

## Why the `Matrix4.Invert` / `operator *` additions

`OpenTKCompatibility.cs` defines its own minimal `OpenTK.Matrix4` struct
that previously had no `Invert` method or `*` operator. The compute path
needs `inverse(view * projection)` to unproject screen pixels back to
world-space view directions in the shader, so both members were added to
the struct. The implementation is the standard cofactor/adjugate inverse
(matching OpenTK 3.x semantics), with a singular-matrix fallback that
returns identity.
