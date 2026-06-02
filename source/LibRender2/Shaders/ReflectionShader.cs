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

using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.Shaders
{
    /// <summary>
    /// Shader program used for the Dual-Paraboloid reflection capture pass.
    /// Each frame (or time-sliced interval) this shader renders the surrounding scene
    /// from the probe's viewpoint into a front and back paraboloid texture.
    /// </summary>
    public class ReflectionShader : AbstractShader
    {
        // ── Uniform locations ──────────────────────────────────────────────────
        private readonly int uProbePosition;
        private readonly int uFace;
        private readonly int uNear;
        private readonly int uFar;
        private readonly int uTexture;
        private readonly int uHasTexture;
        private readonly int uTextureMatrix;
        private readonly int uModelMatrix;

        // Uniform block binding for animation matrices (same as shadow_depth).
        private const int MatrixBlockBinding = 0;

        public ReflectionShader(BaseRenderer renderer, string vertexShaderName, string fragmentShaderName, bool isFromStream = false)
            : base(renderer, vertexShaderName, fragmentShaderName, isFromStream, false)
        {
            // Bind the animation-matrix UBO to binding point 0.
            int matrixBlockIndex = GL.GetUniformBlockIndex(Handle, "matrices");
            if (matrixBlockIndex != -1)
            {
                GL.UniformBlockBinding(Handle, matrixBlockIndex, MatrixBlockBinding);
            }

            // Cache uniform locations.
            uProbePosition = GL.GetUniformLocation(Handle, "uProbePosition");
            uFace          = GL.GetUniformLocation(Handle, "uFace");
            uNear          = GL.GetUniformLocation(Handle, "uNear");
            uFar           = GL.GetUniformLocation(Handle, "uFar");
            uTexture       = GL.GetUniformLocation(Handle, "uTexture");
            uHasTexture    = GL.GetUniformLocation(Handle, "uHasTexture");
            uTextureMatrix = GL.GetUniformLocation(Handle, "uTextureMatrix");
            uModelMatrix   = GL.GetUniformLocation(Handle, "uModelMatrix");

            // Safe defaults.
            GL.ProgramUniform1(Handle, uFace, 0);
            GL.ProgramUniform1(Handle, uNear, 0.1f);
            GL.ProgramUniform1(Handle, uFar, 200.0f);
            GL.ProgramUniform1(Handle, uTexture, 0);
            GL.ProgramUniform1(Handle, uHasTexture, 0);
        }

        // ── Setters ───────────────────────────────────────────────────────────

        /// <summary>Sets the world-space position of the reflection probe.</summary>
        public void SetProbePosition(Vector3 position)
        {
            GL.Uniform3(uProbePosition, (float)position.X, (float)position.Y, (float)position.Z);
        }

        /// <summary>Selects the capture hemisphere: 0 = front (+Z), 1 = back (-Z).</summary>
        public void SetFace(int face)
        {
            GL.Uniform1(uFace, face);
        }

        /// <summary>Sets near and far clipping distances for the paraboloid projection.</summary>
        public void SetClipPlanes(float near, float far)
        {
            GL.Uniform1(uNear, near);
            GL.Uniform1(uFar, far);
        }

        /// <summary>Sets which texture unit holds the albedo texture.</summary>
        public void SetTexture(int unit)
        {
            GL.Uniform1(uTexture, unit);
        }

        /// <summary>Tells the shader whether there is an albedo texture to sample.</summary>
        public void SetHasTexture(bool hasTexture)
        {
            GL.Uniform1(uHasTexture, hasTexture ? 1 : 0);
        }

        /// <summary>Sets the texture coordinate transformation matrix.</summary>
        public void SetTextureMatrix(Matrix4D m)
        {
            OpenTK.Matrix4 matrix = ConvertToMatrix4(m);
            GL.UniformMatrix4(uTextureMatrix, false, ref matrix);
        }

        /// <summary>Sets the model transformation matrix.</summary>
        public void SetModelMatrix(Matrix4D m)
        {
            OpenTK.Matrix4 matrix = ConvertToMatrix4(m);
            GL.UniformMatrix4(uModelMatrix, false, ref matrix);
        }

        /// <summary>Uploads and binds animation matrices for animated objects.</summary>
        public void SetCurrentAnimationMatricies(ObjectState objectState)
        {
            OpenTK.Matrix4[] mats = new OpenTK.Matrix4[objectState.Matricies.Length];
            for (int i = 0; i < objectState.Matricies.Length; i++)
            {
                mats[i] = ConvertToMatrix4(objectState.Matricies[i]);
            }

            unsafe
            {
                if (objectState.MatrixBufferIndex == 0)
                {
                    objectState.MatrixBufferIndex = GL.GenBuffer();
                }

                GL.BindBuffer(BufferTarget.UniformBuffer, objectState.MatrixBufferIndex);
                GL.BufferData(BufferTarget.UniformBuffer, sizeof(OpenTK.Matrix4) * mats.Length, mats, BufferUsageHint.StaticDraw);
            }
        }

        // ── Internal helper ───────────────────────────────────────────────────
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
