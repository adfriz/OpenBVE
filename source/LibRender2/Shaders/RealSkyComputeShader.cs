//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, Aditiya Afrizal, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
//AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
//IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
//FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
//DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
//SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
//CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
//OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
//OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Graphics.OpenGL;

// NOTE: In OpenTK 3.x (which OpenBVE uses via the OpenTK-OpenBVE 1.0.4
// NuGet package), all OpenGL 1.0-4.6 functions and enums are exposed
// through the OpenTK.Graphics.OpenGL namespace. If your fork of OpenTK
// does NOT expose GL.DispatchCompute / GL.BindImageTexture / etc. in
// this namespace, simply add `using OpenTK.Graphics.OpenGL4;` below
// and qualify the 4.3-specific calls with the GL4.GL.* prefix.

namespace LibRender2.Shaders
{
    /// <summary>
    /// OpenGL 4.3 compute-shader program used by the RealSky atmospheric system.
    /// </summary>
    /// <remarks>
    /// Encapsulates everything that the new OpenGL 4.3 path needs:
    /// <list type="bullet">
    ///   <item>A compute-only program (loaded from <c>RealSky.comp</c>).</item>
    ///   <item>An RGBA16F texture that the compute shader writes to via
    ///         <c>imageStore</c> (image load / store, GL 4.2).</item>
    ///   <item>A Shader Storage Buffer Object carrying cloud parameters
    ///         (SSBO, GL 4.3).</item>
    ///   <item>A <see cref="Dispatch"/> helper that issues the compute call
    ///         and the appropriate memory barrier.</item>
    /// </list>
    /// The class is intentionally self-contained: the base renderer just
    /// has to <see cref="Activate"/> / <see cref="Dispatch"/> / <see cref="Deactivate"/>
    /// and then bind the output texture when drawing the skybox.
    /// </remarks>
    public sealed class RealSkyComputeShader : IDisposable
    {
        /// <summary>OpenGL handle to the linked compute program.</summary>
        public int Handle { get; private set; }

        /// <summary>OpenGL handle to the RGBA16F image2D output texture.</summary>
        public int OutputTexture { get; private set; }

        /// <summary>OpenGL handle to the Shader Storage Buffer Object.</summary>
        public int ParametersBuffer { get; private set; }

        /// <summary>OpenGL handle to the 3D FBM noise texture.</summary>
        public int NoiseTexture { get; private set; }

        /// <summary>Current width of the output texture (matches the screen).</summary>
        public int Width { get; private set; }

        /// <summary>Current height of the output texture (matches the screen).</summary>
        public int Height { get; private set; }

        /// <summary>
        /// Cloud-layer parameters that are uploaded to the SSBO every frame.
        /// Kept as a public field so the renderer can tweak them at runtime.
        /// </summary>
        public CloudParams Parameters = new CloudParams
        {
            // cloudMinHeight, cloudMaxHeight, stepSize, maxDistance
            CloudMinHeight = 1500.0f,
            CloudMaxHeight = 2500.0f,
            StepSize       = 100.0f,
            MaxDistance    = 20000.0f,
            Steps          = 32.0f,
            LightSteps     = 6.0f,
        };

        // Work-group size declared in the compute shader (16x16x1).
        private const int WorkGroupX = 16;
        private const int WorkGroupY = 16;

        // Cached uniform locations.
        private readonly int uInvViewProjLocation;
        private readonly int uSunDirectionLocation;
        private readonly int uCameraPosLocation;
        private readonly int uTimeLocation;
        private readonly int uResolutionLocation;

        private bool disposed;

        /// <summary>
        /// Loads and links the compute shader from disk, allocates the
        /// output texture and SSBO, and queries uniform locations.
        /// </summary>
        /// <param name="computeShaderPath">Absolute path to <c>RealSky.comp</c>.</param>
        public RealSkyComputeShader(string computeShaderPath)
        {
            Handle = GL.CreateProgram();

            int shader = GL.CreateShader(ShaderType.ComputeShader);
            string source = File.ReadAllText(computeShaderPath, System.Text.Encoding.UTF8);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new ApplicationException("RealSky compute shader compile error: " + log);
            }

            GL.AttachShader(Handle, shader);
            GL.LinkProgram(Handle);
            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
            {
                string log = GL.GetProgramInfoLog(Handle);
                GL.DeleteShader(shader);
                throw new Exception("RealSky compute shader link error: " + log);
            }
            // Safe to delete the shader object after linking.
            GL.DeleteShader(shader);

            // Query uniform locations (explicit-location uniforms are used in
            // the .comp file too, but this is more robust against future edits).
            uInvViewProjLocation   = GL.GetUniformLocation(Handle, "uInvViewProj");
            uSunDirectionLocation  = GL.GetUniformLocation(Handle, "uRealSkySunDirection");
            uCameraPosLocation     = GL.GetUniformLocation(Handle, "uRealSkyCameraPos");
            uTimeLocation          = GL.GetUniformLocation(Handle, "uRealSkyTime");
            uResolutionLocation    = GL.GetUniformLocation(Handle, "uRealSkyResolution");

            AllocateOutputTexture(16, 16);
            AllocateParametersBuffer();

            // Generate and allocate 3D noise texture
            NoiseTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture3D, NoiseTexture);
            byte[] noiseData = Generate3DNoiseFBM();
            GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.R8, 128, 128, 128, 0, PixelFormat.Red, PixelType.UnsignedByte, noiseData);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.Repeat);
            GL.BindTexture(TextureTarget.Texture3D, 0);
        }

        private static byte[] Generate3DNoiseFBM()
        {
            int size = 128;
            byte[] data = new byte[size * size * size];
            Random rand = new Random(42);
            int gridSide = 32;
            float[] grid = new float[gridSide * gridSide * gridSide];
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = (float)rand.NextDouble();
            }

            float SampleGrid(int x, int y, int z)
            {
                x = (x % gridSide + gridSide) % gridSide;
                y = (y % gridSide + gridSide) % gridSide;
                z = (z % gridSide + gridSide) % gridSide;
                return grid[x + y * gridSide + z * gridSide * gridSide];
            }

            float Noise(float x, float y, float z)
            {
                int ix = (int)Math.Floor(x);
                int iy = (int)Math.Floor(y);
                int iz = (int)Math.Floor(z);
                float fx = x - ix;
                float fy = y - iy;
                float fz = z - iz;

                float ux = fx * fx * (3.0f - 2.0f * fx);
                float uy = fy * fy * (3.0f - 2.0f * fy);
                float uz = fz * fz * (3.0f - 2.0f * fz);

                float n000 = SampleGrid(ix, iy, iz);
                float n100 = SampleGrid(ix + 1, iy, iz);
                float n010 = SampleGrid(ix, iy + 1, iz);
                float n110 = SampleGrid(ix + 1, iy + 1, iz);
                float n001 = SampleGrid(ix, iy, iz + 1);
                float n101 = SampleGrid(ix + 1, iy, iz + 1);
                float n011 = SampleGrid(ix, iy + 1, iz + 1);
                float n111 = SampleGrid(ix + 1, iy + 1, iz + 1);

                float lx0 = n000 + ux * (n100 - n000);
                float lx1 = n010 + ux * (n110 - n010);
                float ly0 = lx0 + uy * (lx1 - lx0);

                float lx2 = n001 + ux * (n101 - n001);
                float lx3 = n011 + ux * (n111 - n011);
                float ly1 = lx2 + uy * (lx3 - lx2);

                return ly0 + uz * (ly1 - ly0);
            }

            float Fbm(float x, float y, float z)
            {
                float value = 0.0f;
                float amplitude = 0.5f;
                float frequency = 1.0f;
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * Noise(x * frequency, y * frequency, z * frequency);
                    frequency *= 2.0f;
                    amplitude *= 0.5f;
                }
                return value;
            }

            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x / size * gridSide;
                        float fy = (float)y / size * gridSide;
                        float fz = (float)z / size * gridSide;

                        float f = Fbm(fx, fy, fz);
                        int val = (int)(f * 255.0f);
                        data[x + y * size + z * size * size] = (byte)Math.Max(0, Math.Min(255, val));
                    }
                }
            }
            return data;
        }

        /// <summary>
        /// (Re)allocates the RGBA16F output texture for the requested dimensions.
        /// No-op if the size has not changed.
        /// </summary>
        public void AllocateOutputTexture(int width, int height)
        {
            if (width == Width && height == Height && OutputTexture != 0)
            {
                return;
            }
            if (OutputTexture == 0)
            {
                OutputTexture = GL.GenTexture();
            }
            Width = width;
            Height = height;

            GL.BindTexture(TextureTarget.Texture2D, OutputTexture);
            // TexStorage2D is the immutable-texture allocation introduced in
            // GL 4.2; required for binding as an image2D.
            GL.TexStorage2D(TextureTarget2d.Texture2D, 1, (SizedInternalFormat)PixelInternalFormat.R11fG11fB10f, Width, Height);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>Allocates the SSBO that backs the <c>CloudParams</c> block.</summary>
        private void AllocateParametersBuffer()
        {
            ParametersBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, ParametersBuffer);
            // 8 floats = 32 bytes. Dynamic-draw because we update it every frame.
            GL.BufferData(BufferTarget.ShaderStorageBuffer, 8 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
        }

        /// <summary>Uploads and binds the SSBO to the binding declared in the shader (binding = 1).</summary>
        private void BindParametersBuffer()
        {
            float[] packed = new float[]
            {
                Parameters.CloudMinHeight,
                Parameters.CloudMaxHeight,
                Parameters.StepSize,
                Parameters.MaxDistance,
                Parameters.Steps,
                Parameters.LightSteps,
                Parameters.Padding1,
                Parameters.Padding2,
            };
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, ParametersBuffer);
            GL.BufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, packed.Length * sizeof(float), packed);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

            // Bind to SSBO binding point 1 (matches the GLSL `binding = 1`).
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, ParametersBuffer);
        }

        /// <summary>Installs the compute program as active.</summary>
        public void Activate()
        {
            GL.UseProgram(Handle);
        }

        /// <summary>
        /// Dispatches the compute shader so that every screen pixel gets one
        /// invocation, then issues a <see cref="MemoryBarrierFlags.ShaderImageAccessBarrierBit"/>
        /// so the subsequent fragment-shader texture sample observes the writes.
        /// </summary>
        /// <param name="invViewProj">Inverse (view * projection) matrix in row-major form (we transpose=false).</param>
        /// <param name="sunDirection">Normalised sun direction.</param>
        /// <param name="cameraPos">World-space camera position.</param>
        /// <param name="time">Current simulation time in seconds.</param>
        public void Dispatch(ref Matrix4 invViewProj, OpenBveApi.Math.Vector3 sunDirection, OpenBveApi.Math.Vector3 cameraPos, float time)
        {
            Activate();

            // Resize the output texture to match the screen if needed.
            if (Width != rendererWidth || Height != rendererHeight)
            {
                AllocateOutputTexture(rendererWidth, rendererHeight);
            }

            // Bind the output image at binding = 0 (write-only, R11F_G11F_B10F).
            GL.BindImageTexture(0, OutputTexture, 0, false, 0, TextureAccess.WriteOnly, (SizedInternalFormat)PixelInternalFormat.R11fG11fB10f);

            // Bind the 3D noise texture to texture unit 2.
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture3D, NoiseTexture);

            // Upload SSBO parameters.
            BindParametersBuffer();

            // Uniforms (use DSA-style ProgramUniform* so the program does not
            // have to be active — consistent with the rest of LibRender2).
            GL.ProgramUniformMatrix4(Handle, uInvViewProjLocation, false, ref invViewProj);
            GL.ProgramUniform3(Handle, uSunDirectionLocation, (float)sunDirection.X, (float)sunDirection.Y, (float)sunDirection.Z);
            GL.ProgramUniform3(Handle, uCameraPosLocation,    (float)cameraPos.X,    (float)cameraPos.Y,    (float)cameraPos.Z);
            GL.ProgramUniform1(Handle, uTimeLocation, time);
            GL.ProgramUniform2(Handle, uResolutionLocation, (float)Width, (float)Height);

            // Dispatch with enough work groups to cover the whole screen.
            int groupsX = (Width  + WorkGroupX - 1) / WorkGroupX;
            int groupsY = (Height + WorkGroupY - 1) / WorkGroupY;
            GL.DispatchCompute(groupsX, groupsY, 1);

            // Make sure the imageStore writes are visible to the fragment
            // shader that samples uSkyTexture.
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        /// <summary>Deactivates the compute program.</summary>
        public void Deactivate()
        {
            GL.UseProgram(0);
        }

        // --- Screen-size tracking (set by BaseRenderer before dispatch) ---
        private int rendererWidth;
        private int rendererHeight;

        /// <summary>Updates the cached screen dimensions used to size the output texture.</summary>
        public void SetScreenSize(int width, int height)
        {
            rendererWidth = width;
            rendererHeight = height;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            if (OutputTexture != 0)
            {
                GL.DeleteTexture(OutputTexture);
                OutputTexture = 0;
            }
            if (NoiseTexture != 0)
            {
                GL.DeleteTexture(NoiseTexture);
                NoiseTexture = 0;
            }
            if (ParametersBuffer != 0)
            {
                GL.DeleteBuffer(ParametersBuffer);
                ParametersBuffer = 0;
            }
            if (Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = 0;
            }
            GC.SuppressFinalize(this);
            disposed = true;
        }

        ~RealSkyComputeShader()
        {
            Dispose();
        }

        /// <summary>
        /// Plain-old-data struct matching the GLSL <c>CloudParam</c> struct.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CloudParams
        {
            public float CloudMinHeight;
            public float CloudMaxHeight;
            public float StepSize;
            public float MaxDistance;
            public float Steps;
            public float LightSteps;
            public float Padding1;
            public float Padding2;
        }
    }
}
