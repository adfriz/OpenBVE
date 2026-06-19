// Simplified BSD License (BSD-2-Clause)
//
// Ported / extended for the next-gen branch to bring the RealSky atmospheric
// system onto the GL 4.3 compute path. Original RealSky implementation lived
// on the `realsky` branch as a #version 330 fragment shader.
//
// All rights retained by the OpenBVE Project contributors.

using System;
using LibRender2.Shaders;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Atmosphere
{
        /// <summary>
        /// Orchestrates the RealSky atmospheric rendering pass.
        /// Mirrors the <see cref="Clustering.ClusterEngine"/> and
        /// <see cref="Shadows"/> lifecycle:
        ///   Initialize()  — detect GL capabilities, allocate resources
        ///   Render()      — called once per frame from NewRenderer
        ///   Dispose()     — release GPU resources
        ///
        /// Opsi B (same pattern as ClusterEngine):
        ///   UseComputePath = true  → GL 4.3 compute shader (RealSky.comp)
        ///                             writes to an RGBA16F image, then the
        ///                             image is bound as a sampler for the
        ///                             rest of the frame.
        ///   UseComputePath = false → Fallback to the original fragment-shader
        ///                             skybox draw (RealSky.vert + RealSky.frag
        ///                             at #version 330 core) from the realsky
        ///                             branch.
        /// </summary>
        public class RealSkyPass
        {
                private readonly BaseRenderer renderer;

                // --- Compute path (GL 4.3+) ---
                private RealSkyComputeShader computeShader;
                private int skyImageHandle;        // RGBA16F image2D
                private int skyImageWidth;
                private int skyImageHeight;
                private int skyTextureUnit = 8;    // Bind sky image as sampler2D here for default.frag

                // --- Fallback path (fragment shader skybox draw) ---
                // Loaded lazily only when UseComputePath == false.
                private Shader fallbackShader;
                private VertexArrayObject fallbackSkyboxVao;

                // --- State ---
                /// <summary>Whether the RealSky pass is active. False if init failed or disabled.</summary>
                public bool Enabled;

                /// <summary>
                /// True if using the GL 4.3 compute shader path.
                /// False if using the fragment-shader skybox fallback.
                /// Set by Initialize() after GL capability detection.
                /// </summary>
                public bool UseComputePath;

                /// <summary>GL texture handle of the sky image (compute path) or fallback texture (frag path).</summary>
                public int SkyTextureHandle => skyImageHandle;

                /// <summary>Texture unit the sky sampler is bound to (default.frag reads from here).</summary>
                public int SkyTextureUnit => skyTextureUnit;

                public RealSkyPass(BaseRenderer renderer)
                {
                        this.renderer = renderer;
                }

                // ---------------------------------------------------------------------
                // Initialize
                // ---------------------------------------------------------------------

                /// <summary>
                /// Detects GL capabilities and initializes the appropriate code path.
                /// Must be called after the GL context is fully created.
                /// </summary>
                public void Initialize()
                {
                        try
                        {
                                // --- Runtime GL capability detection (same logic as ClusterEngine) ---
                                string extensions = GL.GetString(StringName.Extensions) ?? string.Empty;
                                int major = GL.GetInteger(GetPName.MajorVersion);
                                int minor = GL.GetInteger(GetPName.MinorVersion);
                                bool gl43Plus = major > 4 || (major == 4 && minor >= 3);

                                bool hasCompute  = gl43Plus || extensions.Contains("GL_ARB_compute_shader");
                                bool hasImageLoad = gl43Plus || extensions.Contains("GL_ARB_shader_image_load_store");

                                UseComputePath = hasCompute && hasImageLoad;

                                renderer.fileSystem.AppendToLogFile(
                                        $"[RealSky] GL {major}.{minor} | Compute={hasCompute} ImageLoad={hasImageLoad} → UseComputePath={UseComputePath}");

                                if (UseComputePath)
                                {
                                        computeShader = new RealSkyComputeShader(renderer);
                                        computeShader.Initialize();

                                        AllocateSkyImage(renderer.Screen.Width, renderer.Screen.Height);

                                        renderer.fileSystem.AppendToLogFile(
                                                $"[RealSky] Initialized compute path: {skyImageWidth}x{skyImageHeight} RGBA16F image, unit {skyTextureUnit}");
                                }
                                else
                                {
                                        // Fallback resources are created lazily in Render()
                                        renderer.fileSystem.AppendToLogFile(
                                                "[RealSky] Initialized fragment-shader fallback path (GL < 4.3)");
                                }

                                Enabled = true;
                        }
                        catch (Exception ex)
                        {
                                renderer.fileSystem.AppendToLogFile($"[RealSky] Init failed: {ex.Message}");
                                Enabled = false;
                                GL.GetError(); // clear GL error state
                        }
                }

                // ---------------------------------------------------------------------
                // AllocateSkyImage — (re)creates the RGBA16F image texture
                // ---------------------------------------------------------------------

                /// <summary>
                /// Allocates (or reallocates) the RGBA16F sky image texture.
                /// Called on init and whenever the screen size changes.
                /// </summary>
                private void AllocateSkyImage(int width, int height)
                {
                        if (width <= 0 || height <= 0) return;

                        if (skyImageHandle != 0 && width == skyImageWidth && height == skyImageHeight)
                        {
                                return; // already the right size
                        }

                        if (skyImageHandle != 0)
                        {
                                GL.DeleteTexture(skyImageHandle);
                                skyImageHandle = 0;
                        }

                        skyImageHandle = GL.GenTexture();
                        GL.BindTexture(TextureTarget.Texture2D, skyImageHandle);
                        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
                                      width, height, 0,
                                      OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                        GL.BindTexture(TextureTarget.Texture2D, 0);

                        skyImageWidth = width;
                        skyImageHeight = height;
                }

                // ---------------------------------------------------------------------
                // Render — called once per frame from NewRenderer.ShowScene
                // ---------------------------------------------------------------------

                /// <summary>
                /// Renders the RealSky atmospheric pass.
                /// On the compute path this dispatches the compute shader and leaves
                /// the sky image bound to <see cref="SkyTextureUnit"/> for the rest
                /// of the frame.
                /// On the fallback path this draws the original skybox using the
                /// fragment shader from the realsky branch.
                /// </summary>
                /// <param name="time">Total elapsed time in seconds (drives cloud animation).</param>
                /// <param name="sunDirection">Normalized sun direction in world space.</param>
                public void Render(double time, Vector3 sunDirection)
                {
                        if (!Enabled) return;

                        if (UseComputePath)
                        {
                                RenderCompute(time, sunDirection);
                        }
                        else
                        {
                                RenderFallback(time, sunDirection);
                        }
                }

                // ---------------------------------------------------------------------
                // RenderCompute
                // ---------------------------------------------------------------------

                private void RenderCompute(double time, Vector3 sunDirection)
                {
                        // Reallocate sky image if the screen resized
                        if (renderer.Screen.Width != skyImageWidth || renderer.Screen.Height != skyImageHeight)
                        {
                                AllocateSkyImage(renderer.Screen.Width, renderer.Screen.Height);
                                if (skyImageHandle == 0) return;
                        }

                        // Bind image for writing
                        GL.BindImageTexture(0, skyImageHandle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

                        // Compute inverse(view * projection)
                        // OpenTK's Matrix4 is row-major and uses the same convention as
                        // LibRender2's Matrix4D (column vectors, post-multiply).
                        OpenTK.Matrix4 view = ConvertToMatrix4(renderer.CurrentViewMatrix);
                        OpenTK.Matrix4 proj = ConvertToMatrix4(renderer.CurrentProjectionMatrix);
                        OpenTK.Matrix4 viewProj = view * proj;
                        OpenTK.Matrix4 invViewProj;
                        OpenTK.Matrix4.Invert(ref viewProj, out invViewProj);

                        computeShader.Activate();
                        computeShader.SetSunDirection((float)sunDirection.X, (float)sunDirection.Y, (float)sunDirection.Z);
                        computeShader.SetCameraPos(
                                (float)renderer.Camera.AbsolutePosition.X,
                                (float)renderer.Camera.AbsolutePosition.Y,
                                (float)renderer.Camera.AbsolutePosition.Z);
                        computeShader.SetTime((float)time);
                        computeShader.SetResolution(skyImageWidth, skyImageHeight);
                        computeShader.SetExposure(1.2f);
                        computeShader.SetInvViewProj(ref invViewProj);

                        // Dispatch (ceil(w/8) x ceil(h/8) x 1)
                        computeShader.Dispatch(skyImageWidth, skyImageHeight);

                        // Make image writes visible to subsequent texture reads
                        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit |
                                         MemoryBarrierFlags.TextureFetchBarrierBit);

                        // Bind the sky image as a regular sampler2D for the default shader
                        GL.ActiveTexture(TextureUnit.Texture0 + skyTextureUnit);
                        GL.BindTexture(TextureTarget.Texture2D, skyImageHandle);
                        GL.ActiveTexture(TextureUnit.Texture0);
                }

                // ---------------------------------------------------------------------
                // RenderFallback — original skybox fragment-shader draw
                // ---------------------------------------------------------------------

                private void RenderFallback(double time, Vector3 sunDirection)
                {
                        // The fallback uses the original RealSky.vert + RealSky.frag
                        // from the realsky branch. Resources are created lazily to avoid
                        // allocating GPU memory when the compute path is in use.
                        if (fallbackShader == null || fallbackSkyboxVao == null)
                        {
                                InitializeFallback();
                        }
                        if (fallbackShader == null) return;

                        fallbackShader.Activate();

                        // Skybox is always centered around camera but follows rotation
                        Matrix4D viewMatrix = renderer.CurrentViewMatrix;
                        viewMatrix.Row3 = new Vector4(0, 0, 0, 1);
                        fallbackShader.SetCurrentModelViewMatrix(viewMatrix);
                        fallbackShader.SetCurrentProjectionMatrix(renderer.CurrentProjectionMatrix);

                        GL.ProgramUniform3(fallbackShader.Handle, fallbackShader.UniformLayout.RealSkySunDirection,
                                           (float)sunDirection.X, (float)sunDirection.Y, (float)sunDirection.Z);
                        GL.ProgramUniform1(fallbackShader.Handle, fallbackShader.UniformLayout.RealSkyTime, (float)time);
                        GL.ProgramUniform2(fallbackShader.Handle, fallbackShader.UniformLayout.RealSkyResolution,
                                           (float)renderer.Screen.Width, (float)renderer.Screen.Height);
                        GL.ProgramUniform3(fallbackShader.Handle, fallbackShader.UniformLayout.RealSkyCameraPos,
                                           (float)renderer.Camera.AbsolutePosition.X,
                                           (float)renderer.Camera.AbsolutePosition.Y,
                                           (float)renderer.Camera.AbsolutePosition.Z);

                        GL.Disable(EnableCap.DepthTest);
                        GL.DepthMask(false);
                        GL.Disable(EnableCap.CullFace);

                        fallbackSkyboxVao.Bind();
                        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
                        fallbackSkyboxVao.UnBind();

                        GL.Enable(EnableCap.CullFace);
                        GL.DepthMask(true);
                        GL.Enable(EnableCap.DepthTest);

                        fallbackShader.Deactivate();
                }

                private void InitializeFallback()
                {
                        try
                        {
                                if (fallbackShader == null)
                                {
                                        string shaderPath = renderer.fileSystem.GetDataFolder("Shaders", "Atmosphere");
                                        fallbackShader = new Shader(renderer,
                                                Path.CombineFile(shaderPath, "RealSky.vert"),
                                                Path.CombineFile(shaderPath, "RealSky.frag"));
                                }
                                if (fallbackSkyboxVao == null)
                                {
                                        // Same skybox cube as the original realsky branch
                                        LibRenderVertex[] vertices = {
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),

                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),

                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),

                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),

                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f,  1.0f, -1.0f)),

                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f, -1.0f)),
                                                new LibRenderVertex(new Vector3f(-1.0f, -1.0f,  1.0f)),
                                                new LibRenderVertex(new Vector3f( 1.0f, -1.0f,  1.0f)),
                                        };
                                        fallbackSkyboxVao = new VertexArrayObject();
                                        fallbackSkyboxVao.Bind();
                                        fallbackSkyboxVao.SetVBO(new VertexBufferObject(vertices, BufferUsageHint.StaticDraw));
                                        fallbackSkyboxVao.SetAttributes(fallbackShader.VertexLayout);
                                        fallbackSkyboxVao.UnBind();
                                }
                        }
                        catch (Exception ex)
                        {
                                renderer.fileSystem.AppendToLogFile($"[RealSky] Fallback init failed: {ex.Message}");
                        }
                }

                // ---------------------------------------------------------------------
                // BindSkyToShader — binds the sky image to a sampler unit for the
                // default scene shader so subsequent geometry passes can sample the sky.
                // ---------------------------------------------------------------------

                /// <summary>
                /// Binds the sky texture to <see cref="SkyTextureUnit"/> and updates
                /// the default shader's uniform so subsequent geometry passes can
                /// sample the sky. Only relevant on the compute path — on the
                /// fallback path the sky is drawn directly into the framebuffer.
                /// </summary>
                public void BindSkyToShader(Shader shader)
                {
                        if (!Enabled || !UseComputePath || shader == null || skyImageHandle == 0) return;

                        GL.ActiveTexture(TextureUnit.Texture0 + skyTextureUnit);
                        GL.BindTexture(TextureTarget.Texture2D, skyImageHandle);
                        GL.ActiveTexture(TextureUnit.Texture0);

                        shader.SetSkyEnabled(true);
                        shader.SetSkyTextureUnit(skyTextureUnit);
                }

                /// <summary>Unbinds the sky sampler and disables sky sampling in the shader.</summary>
                public void UnbindSkyFromShader(Shader shader)
                {
                        if (shader == null) return;
                        shader.SetSkyEnabled(false);
                }

                // ---------------------------------------------------------------------
                // Dispose
                // ---------------------------------------------------------------------

                public void Dispose()
                {
                        computeShader?.Dispose();
                        computeShader = null;

                        if (skyImageHandle != 0)
                        {
                                GL.DeleteTexture(skyImageHandle);
                                skyImageHandle = 0;
                        }

                        fallbackShader?.Dispose();
                        fallbackShader = null;
                        // fallbackSkyboxVao will be cleaned up via BaseRenderer's VAO delete queue

                        Enabled = false;
                }

                // ---------------------------------------------------------------------
                // Helpers
                // ---------------------------------------------------------------------

                /// <summary>Converts LibRender2 Matrix4D to OpenTK Matrix4.</summary>
                private static OpenTK.Matrix4 ConvertToMatrix4(Matrix4D mat)
                {
                        return new OpenTK.Matrix4(
                                (float)mat.Row0.X, (float)mat.Row0.Y, (float)mat.Row0.Z, (float)mat.Row0.W,
                                (float)mat.Row1.X, (float)mat.Row1.Y, (float)mat.Row1.Z, (float)mat.Row1.W,
                                (float)mat.Row2.X, (float)mat.Row2.Y, (float)mat.Row2.Z, (float)mat.Row2.W,
                                (float)mat.Row3.X, (float)mat.Row3.Y, (float)mat.Row3.Z, (float)mat.Row3.W
                        );
                }
        }
}
