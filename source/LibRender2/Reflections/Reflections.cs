// Simplified BSD License (BSD-2-Clause)
//
// Copyright (c) 2024, Aditiya Afrizal, The OpenBVE Project
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using LibRender2.Objects;
using LibRender2.Shaders;
using OpenBveApi.Graphics;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Reflections
{
    /// <summary>
    /// Manages the Dual-Paraboloid Reflection Mapping (DPM) system.
    ///
    /// Architecture:
    ///   - Two capture textures per probe: front hemisphere (face 0, +Z) and back hemisphere (face 1, -Z).
    ///   - One shared FBO with color and depth attachments.
    ///   - Probe position = camera position at capture time (follows the player).
    ///   - Resolution is controlled by <see cref="OpenBveApi.Interface.ReflectionResolution"/> option.
    ///   - Time-slicing: alternates front/back capture every other frame to halve the per-frame cost.
    ///
    /// Usage:
    ///   1. Call <see cref="Initialize"/> once after GL context is ready.
    ///   2. Call <see cref="RenderPass"/> each frame (before the main scene pass) to update the probe textures.
    ///   3. Call <see cref="BindToShader"/> before drawing reflective meshes to supply the probe textures.
    /// </summary>
    public class Reflections
    {
        // ── State ─────────────────────────────────────────────────────────────
        private readonly BaseRenderer renderer;

        /// <summary>Whether reflections are currently enabled and ready.</summary>
        public bool Enabled;

        /// <summary>Current probe resolution in pixels (both axes).</summary>
        public int Resolution;

        // ── GL Resources ──────────────────────────────────────────────────────
        /// <summary>Shared FBO for the capture pass.</summary>
        private int fbo;
        /// <summary>OpenGL texture for the front hemisphere (+Z).</summary>
        internal int FrontTexture;
        /// <summary>OpenGL texture for the back hemisphere (-Z).</summary>
        internal int BackTexture;
        /// <summary>Shared depth renderbuffer attached to the FBO.</summary>
        private int depthRbo;

        // ── Shader ────────────────────────────────────────────────────────────
        internal ReflectionShader CaptureShader;

        // ── Time-slicing ──────────────────────────────────────────────────────
        /// <summary>Which hemisphere to update this frame (0 = front, 1 = back).</summary>
        private int currentFace;

        // ── Probe ─────────────────────────────────────────────────────────────
        private const float NearPlane = 0.1f;
        private const float FarPlane  = 500.0f;

        // ── Ctor ──────────────────────────────────────────────────────────────
        public Reflections(BaseRenderer renderer)
        {
            this.renderer = renderer;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates (or recreates) all GPU resources needed for the DPM system.
        /// Safe to call multiple times; disposes previous resources first.
        /// </summary>
        public void Initialize()
        {
            var opts = renderer.currentOptions;

            if (!opts.ReflectionsEnabled)
            {
                Dispose();
                renderer.fileSystem.AppendToLogFile("[DPM] Reflections disabled by user setting.");
                return;
            }

            int resolution = Math.Max(128, (int)opts.ReflectionResolution);
            Resolution = resolution;

            try
            {
                // Dispose existing resources if resizing.
                DisposeGlResources();

                // ── Capture textures ─────────────────────────────────────────
                FrontTexture = CreateColorTexture(resolution);
                BackTexture  = CreateColorTexture(resolution);

                // ── Depth renderbuffer ───────────────────────────────────────
                depthRbo = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, resolution, resolution);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

                // ── FBO ──────────────────────────────────────────────────────
                fbo = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRbo);
                FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    throw new Exception($"Reflection FBO incomplete: {status}");
                }

                // ── Shader ───────────────────────────────────────────────────
                if (CaptureShader == null)
                {
                    CaptureShader = new ReflectionShader(renderer, "reflective", "reflective", true);
                }

                Enabled = true;
                renderer.fileSystem.AppendToLogFile($"[DPM] Initialized: {resolution}×{resolution}, near={NearPlane}m, far={FarPlane}m.");
            }
            catch (Exception ex)
            {
                renderer.fileSystem.AppendToLogFile($"[DPM] Init failed: {ex.Message}");
                Enabled = false;
                GL.GetError(); // drain
            }
        }

        /// <summary>
        /// Renders one hemisphere of the dual-paraboloid probe into the FBO.
        /// Call once per frame, before the main scene render pass.
        /// Alternates between front (face 0) and back (face 1) each frame to halve cost.
        /// </summary>
        public void RenderPass()
        {
            if (!Enabled || CaptureShader == null)
            {
                return;
            }

            // Probe position follows the camera.
            Vector3 probePos = renderer.Camera.AbsolutePosition;

            // Deactivate whatever is currently active.
            renderer.CurrentShader?.Deactivate();

            // Bind the FBO and attach the correct hemisphere texture.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            int targetTex = (currentFace == 0) ? FrontTexture : BackTexture;
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, targetTex, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

            GL.Viewport(0, 0, Resolution, Resolution);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Setup GL state for capture.
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);
            // Paraboloid maps wrap-around causes issues with standard culling; disable it.
            GL.Disable(EnableCap.CullFace);

            // Activate the capture shader and feed per-frame uniforms.
            CaptureShader.Activate();
            CaptureShader.SetProbePosition(probePos);
            CaptureShader.SetFace(currentFace);
            CaptureShader.SetClipPlanes(NearPlane, FarPlane);
            CaptureShader.SetTexture(0);

            // Draw the visible scene into the probe.
            lock (renderer.VisibleObjects.LockObject)
            {
                RenderFaces(renderer.VisibleObjects.OpaqueFaces);
                RenderFaces(renderer.VisibleObjects.AlphaFaces);
            }

            // Restore default framebuffer and GL state.
            CaptureShader.Deactivate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, renderer.Screen.Width, renderer.Screen.Height);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Front);
            GL.DepthFunc(DepthFunction.Lequal);

            // Reset texture state so the next pass re-binds correctly.
            renderer.LastBoundTexture = null;

            // Alternate hemisphere next frame.
            currentFace = 1 - currentFace;
        }

        /// <summary>
        /// Binds the two paraboloid hemisphere textures to the specified texture units
        /// so that the main scene shader can sample them for reflective surfaces.
        /// </summary>
        /// <param name="frontUnit">GL texture unit index for the front hemisphere (+Z).</param>
        /// <param name="backUnit">GL texture unit index for the back hemisphere (-Z).</param>
        public void BindProbeTextures(int frontUnit, int backUnit)
        {
            if (!Enabled)
            {
                return;
            }

            GL.ActiveTexture(TextureUnit.Texture0 + frontUnit);
            GL.BindTexture(TextureTarget.Texture2D, FrontTexture);

            GL.ActiveTexture(TextureUnit.Texture0 + backUnit);
            GL.BindTexture(TextureTarget.Texture2D, BackTexture);

            GL.ActiveTexture(TextureUnit.Texture0);
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void RenderFaces(IEnumerable<FaceState> faces)
        {
            int lastVAO = -1;

            foreach (var face in faces)
            {
                if (face.Object.Prototype.Mesh.VAO == null)
                {
                    continue;
                }

                ObjectState state = face.Object;
                var material = state.Prototype.Mesh.Materials[face.Face.Material];

                // Skip additive/no-shadow materials (they don't contribute to the environment).
                if (material.BlendMode == MeshMaterialBlendMode.Additive)
                {
                    continue;
                }

                // Upload animation matrices if present.
                if (state.Matricies != null && state.Matricies.Length > 0)
                {
                    CaptureShader.SetCurrentAnimationMatricies(state);
                    GL.BindBufferBase(BufferTarget.UniformBuffer, 0, state.MatrixBufferIndex);
                }

                // Bind diffuse texture if available.
                if (material.DaytimeTexture != null
                    && renderer.currentHost.LoadTexture(ref material.DaytimeTexture,
                        (OpenGlTextureWrapMode)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)))
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D,
                        material.DaytimeTexture.OpenGlTextures[(int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)].Name);
                    CaptureShader.SetHasTexture(true);
                }
                else
                {
                    CaptureShader.SetHasTexture(false);
                }

                CaptureShader.SetTextureMatrix(state.TextureTranslation);

                // Draw.
                VertexArrayObject vao = (VertexArrayObject)state.Prototype.Mesh.VAO;
                if (vao.handle != lastVAO)
                {
                    vao.Bind();
                    lastVAO = vao.handle;
                }

                PrimitiveType drawMode = renderer.GetPrimitiveType(face.Face.Flags);
                vao.Draw(drawMode, face.Face.IboStartIndex, face.Face.Vertices.Length);
            }
        }

        private static int CreateColorTexture(int size)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private void DisposeGlResources()
        {
            if (fbo != 0)
            {
                GL.DeleteFramebuffer(fbo);
                fbo = 0;
            }

            if (FrontTexture != 0)
            {
                GL.DeleteTexture(FrontTexture);
                FrontTexture = 0;
            }

            if (BackTexture != 0)
            {
                GL.DeleteTexture(BackTexture);
                BackTexture = 0;
            }

            if (depthRbo != 0)
            {
                GL.DeleteRenderbuffer(depthRbo);
                depthRbo = 0;
            }
        }

        /// <summary>Disposes all reflection GPU resources and disables the system.</summary>
        public void Dispose()
        {
            DisposeGlResources();
            CaptureShader?.Dispose();
            CaptureShader = null;
            Enabled = false;
        }
    }
}
