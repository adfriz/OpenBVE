"""
Apply the RealSky GL 4.3 port to the next-gen branch in-place.
Each step uses unique anchor strings (not line numbers) so the changes
are deterministic. After applying, we generate clean unified diffs.
"""
import os
import re
import shutil
import subprocess

REPO = "/home/z/my-project/OpenBVE"

def read(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return f.read()

def write(path, content):
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)

def replace_once(path, old, new):
    content = read(path)
    count = content.count(old)
    if count == 0:
        raise RuntimeError(f"Anchor not found in {path}:\n---ANCHOR---\n{old[:300]}\n---END---")
    if count > 1:
        raise RuntimeError(f"Anchor found {count} times in {path} (expected 1):\n---ANCHOR---\n{old[:300]}\n---END---")
    write(path, content.replace(old, new))
    print(f"  patched {path}")

modified_files = []

# ============================================================
# 1. BaseRenderer.cs
# ============================================================
print("[1/10] BaseRenderer.cs")
p = f"{REPO}/source/LibRender2/BaseRenderer.cs"

replace_once(p,
    "using Vector3 = OpenBveApi.Math.Vector3;\n",
    "using Vector3 = OpenBveApi.Math.Vector3;\nusing Vector4 = OpenBveApi.Math.Vector4;\n"
)

replace_once(p,
    "\t\t/// <summary>Manages Clustered Forward Rendering (CFR). Null until Initialize() completes.</summary>\n\t\tpublic ClusterEngine ClusterEngine;\n\n\t\t/// <summary>Whether shadows are enabled.</summary>",
    "\t\t/// <summary>Manages Clustered Forward Rendering (CFR). Null until Initialize() completes.</summary>\n\t\tpublic ClusterEngine ClusterEngine;\n\n\t\t/// <summary>\n\t\t/// Manages the RealSky atmospheric rendering pass.\n\t\t/// Null until Initialize() completes. GL 4.3+ uses a compute shader; older GL falls back to the original fragment-shader skybox.\n\t\t/// </summary>\n\t\tpublic Atmosphere.RealSkyPass RealSkyPass;\n\n\t\t/// <summary>Whether shadows are enabled.</summary>"
)

replace_once(p,
    "\t\t\tClusterEngine = new ClusterEngine(this);\n\t\t\tClusterEngine.Initialize();\n\t\t}",
    "\t\t\tClusterEngine = new ClusterEngine(this);\n\t\t\tClusterEngine.Initialize();\n\n\t\t\t// Initialize RealSky atmospheric pass after CFR (GL context fully ready)\n\t\t\tRealSkyPass = new Atmosphere.RealSkyPass(this);\n\t\t\tRealSkyPass.Initialize();\n\t\t}"
)

replace_once(p,
    "\t\t\tClusterEngine?.Dispose();\n\t\t\tClusterEngine = null;\n\t\t}",
    "\t\t\tClusterEngine?.Dispose();\n\t\t\tClusterEngine = null;\n\n\t\t\t// Dispose RealSky resources before GL context teardown\n\t\t\tRealSkyPass?.Dispose();\n\t\t\tRealSkyPass = null;\n\t\t}"
)

replace_once(p,
    "\t\tprotected void BindCFRToDefaultShader()\n\t\t{\n\t\t\tif (ClusterEngine != null && DefaultShader != null)\n\t\t\t\tClusterEngine.BindToShader(DefaultShader);\n\t\t}\n\n\t\tinternal PrimitiveType GetPrimitiveType(FaceFlags flags)",
    "\t\tprotected void BindCFRToDefaultShader()\n\t\t{\n\t\t\tif (ClusterEngine != null && DefaultShader != null)\n\t\t\t\tClusterEngine.BindToShader(DefaultShader);\n\t\t}\n\n\t\t/// <summary>\n\t\t/// Performs the RealSky atmospheric compute / skybox pass.\n\t\t/// On GL 4.3+ this dispatches the compute shader and binds the sky\n\t\t/// image to a sampler unit for the rest of the frame.\n\t\t/// On older GL this draws the fragment-shader skybox.\n\t\t/// Safe to call even if RealSkyPass is null or disabled.\n\t\t/// </summary>\n\t\t/// <param name=\"time\">Total elapsed time in seconds (drives cloud animation).</param>\n\t\t/// <param name=\"sunDirection\">Normalized sun direction in world space.</param>\n\t\tprotected void PerformRealSkyPass(double time, Vector3 sunDirection)\n\t\t{\n\t\t\tRealSkyPass?.Render(time, sunDirection);\n\t\t}\n\n\t\t/// <summary>Binds the RealSky sky texture to the default shader for the current frame (compute path only).</summary>\n\t\tprotected void BindRealSkyToDefaultShader()\n\t\t{\n\t\t\tif (RealSkyPass != null && DefaultShader != null)\n\t\t\t{\n\t\t\t\tRealSkyPass.BindSkyToShader(DefaultShader);\n\t\t\t}\n\t\t}\n\n\t\t/// <summary>Unbinds the RealSky sky texture from the default shader after the opaque pass completes.</summary>\n\t\tprotected void UnbindRealSkyFromDefaultShader()\n\t\t{\n\t\t\tRealSkyPass?.UnbindSkyFromShader(DefaultShader);\n\t\t}\n\n\t\tinternal PrimitiveType GetPrimitiveType(FaceFlags flags)"
)
modified_files.append("source/LibRender2/BaseRenderer.cs")

# ============================================================
# 2. ShaderLayout.cs
# ============================================================
print("[2/10] ShaderLayout.cs")
p = f"{REPO}/source/LibRender2/openGL/ShaderLayout.cs"
replace_once(p,
    "\t\t/// <summary>\n\t\t/// The handle of \"uCurrentViewMatrix\" within the shader\n\t\t/// </summary>\n\t\tpublic short CurrentViewMatrix = -1;\n\t}",
    "\t\t/// <summary>\n\t\t/// The handle of \"uCurrentViewMatrix\" within the shader\n\t\t/// </summary>\n\t\tpublic short CurrentViewMatrix = -1;\n\n\t\t// --- RealSky atmospheric system ---\n\t\t/// <summary>The handle of \"uSkyEnabled\" within the shader (1 = sample sky texture, 0 = ignore).</summary>\n\t\tpublic short SkyEnabled = -1;\n\n\t\t/// <summary>The handle of \"uSkyTexture\" within the shader (sampler2D unit that holds the RealSky image).</summary>\n\t\tpublic short SkyTexture = -1;\n\n\t\t/// <summary>The handle of \"uRealSkySunDirection\" within the shader (fallback path only; compute path uses image uniforms).</summary>\n\t\tpublic short RealSkySunDirection = -1;\n\n\t\t/// <summary>The handle of \"uRealSkyTime\" within the shader (fallback path only).</summary>\n\t\tpublic short RealSkyTime = -1;\n\n\t\t/// <summary>The handle of \"uRealSkyResolution\" within the shader (fallback path only).</summary>\n\t\tpublic short RealSkyResolution = -1;\n\n\t\t/// <summary>The handle of \"uRealSkyCameraPos\" within the shader (fallback path only).</summary>\n\t\tpublic short RealSkyCameraPos = -1;\n\t}"
)
modified_files.append("source/LibRender2/openGL/ShaderLayout.cs")

# ============================================================
# 3. Shader.cs
# ============================================================
print("[3/10] Shader.cs")
p = f"{REPO}/source/LibRender2/Shaders/Shader.cs"

replace_once(p,
    "\t\tprivate readonly int uCurrentViewMatrixLocation;\n\t\tprivate readonly int uDynamicLightCountLocation;\n\t\tprivate readonly int[] uDynamicLightTypeLocation = new int[16];",
    "\t\tprivate readonly int uCurrentViewMatrixLocation;\n\t\tprivate readonly int uDynamicLightCountLocation;\n\n\t\t// --- RealSky atmospheric system ---\n\t\tprivate readonly int uSkyEnabledLocation;\n\t\tprivate readonly int uSkyTextureLocation;\n\n\t\tprivate readonly int[] uDynamicLightTypeLocation = new int[16];"
)

replace_once(p,
    "\t\t\tuCurrentViewMatrixLocation = GL.GetUniformLocation(Handle, \"uCurrentViewMatrix\");\n\t\t\tuDynamicLightCountLocation = GL.GetUniformLocation(Handle, \"uDynamicLightCount\");\n\t\t\tfor (int i = 0; i < 16; i++)",
    "\t\t\tuCurrentViewMatrixLocation = GL.GetUniformLocation(Handle, \"uCurrentViewMatrix\");\n\t\t\tuDynamicLightCountLocation = GL.GetUniformLocation(Handle, \"uDynamicLightCount\");\n\n\t\t\t// RealSky atmospheric system uniforms\n\t\t\tuSkyEnabledLocation  = GL.GetUniformLocation(Handle, \"uSkyEnabled\");\n\t\t\tuSkyTextureLocation  = GL.GetUniformLocation(Handle, \"uSkyTexture\");\n\t\t\t// Default: sky sampling disabled until RealSkyPass binds a texture\n\t\t\tif (uSkyEnabledLocation >= 0)\n\t\t\t{\n\t\t\t\tGL.ProgramUniform1(Handle, uSkyEnabledLocation, 0);\n\t\t\t}\n\t\t\tfor (int i = 0; i < 16; i++)"
)

replace_once(p,
    "\t\t\t\tCurrentViewMatrix = (short)GL.GetUniformLocation(Handle, \"uCurrentViewMatrix\"),\n\t\t\t};",
    "\t\t\t\tCurrentViewMatrix = (short)GL.GetUniformLocation(Handle, \"uCurrentViewMatrix\"),\n\n\t\t\t\t// RealSky uniforms (for fallback fragment-shader path)\n\t\t\t\tRealSkySunDirection = (short)GL.GetUniformLocation(Handle, \"uRealSkySunDirection\"),\n\t\t\t\tRealSkyTime         = (short)GL.GetUniformLocation(Handle, \"uRealSkyTime\"),\n\t\t\t\tRealSkyResolution   = (short)GL.GetUniformLocation(Handle, \"uRealSkyResolution\"),\n\t\t\t\tRealSkyCameraPos    = (short)GL.GetUniformLocation(Handle, \"uRealSkyCameraPos\"),\n\t\t\t};"
)

replace_once(p,
    "\t\t#endregion\n\t}\n}",
    "\t\t#endregion\n\n\t\t#region RealSky\n\n\t\t/// <summary>\n\t\t/// Enables or disables sky texture sampling in the fragment shader.\n\t\t/// When true, the shader samples <see cref=\"uSkyTexture\"/> for the sky colour\n\t\t/// (compute path); when false, the existing background colour is used.\n\t\t/// </summary>\n\t\tpublic void SetSkyEnabled(bool enabled)\n\t\t{\n\t\t\tif (uSkyEnabledLocation < 0) return;\n\t\t\tGL.ProgramUniform1(Handle, uSkyEnabledLocation, enabled ? 1 : 0);\n\t\t}\n\n\t\t/// <summary>\n\t\t/// Sets the texture unit that the sky image is bound to.\n\t\t/// The texture itself is bound separately by <see cref=\"Atmosphere.RealSkyPass\"/>.\n\t\t/// </summary>\n\t\t/// <param name=\"unit\">Zero-based texture unit index (e.g. 8 for TextureUnit.Texture8).</param>\n\t\tpublic void SetSkyTextureUnit(int unit)\n\t\t{\n\t\t\tif (uSkyTextureLocation < 0) return;\n\t\t\tGL.ProgramUniform1(Handle, uSkyTextureLocation, unit);\n\t\t}\n\n\t\t#endregion\n\t}\n}"
)
modified_files.append("source/LibRender2/Shaders/Shader.cs")

# ============================================================
# 4. LibRender2.csproj
# ============================================================
print("[4/10] LibRender2.csproj")
p = f"{REPO}/source/LibRender2/LibRender2.csproj"
replace_once(p,
    '    <EmbeddedResource Include="..\\..\\assets\\Shaders\\cluster_culling.comp" LogicalName="LibRender2.cluster_culling.comp" />\n',
    '    <EmbeddedResource Include="..\\..\\assets\\Shaders\\cluster_culling.comp" LogicalName="LibRender2.cluster_culling.comp" />\n    <EmbeddedResource Include="..\\..\\assets\\Shaders\\RealSky.comp" LogicalName="LibRender2.RealSky.comp" />\n'
)
modified_files.append("source/LibRender2/LibRender2.csproj")

# ============================================================
# 4b. GL.cs — add BindImageTexture wrapper + TextureAccess enum
#     (needed by RealSkyPass to bind the sky image for compute writes)
# ============================================================
print("[4b/10] GL.cs — add BindImageTexture")
p = f"{REPO}/source/LibRender2/openGL/GL.cs"
# Insert BindImageTexture just before UseProgram
replace_once(p,
    "        public static void UseProgram(int program) => gl.UseProgram((uint)program);",
    """        // BindImageTexture — required by GL 4.3 compute shaders that write to image2D
        // (RealSky atmospheric compute pass).
        // Mirrors Silk.NET.OpenGL.GL.BindImageTexture — arguments are passed through
        // with only the texture handle widened to uint and the access / format enums
        // mapped to Silk.NET.OpenGL.GLEnum.
        public static unsafe void BindImageTexture(uint unit, int texture, int level, bool layered, int layer, TextureAccess access, SizedInternalFormat format)
        {
            gl.BindImageTexture(unit, (uint)texture, (uint)level, layered, (uint)layer, (Silk.NET.OpenGL.GLEnum)access, (Silk.NET.OpenGL.GLEnum)format);
        }

        public static void UseProgram(int program) => gl.UseProgram((uint)program);"""
)
modified_files.append("source/LibRender2/openGL/GL.cs")

# Add the TextureAccess + SizedInternalFormat enums to OpenTKCompatibility.cs
# (these don't exist in the custom OpenTK.Graphics.OpenGL namespace today)
print("[4c/10] OpenTKCompatibility.cs — add TextureAccess + SizedInternalFormat enums + Matrix4.Invert + operator*")
p = f"{REPO}/source/LibRender2/openGL/OpenTKCompatibility.cs"
# First: extend the Matrix4 struct with Invert() and operator*
# Find the Matrix4 struct closing brace
mat4_close_anchor = "            M41 = m30; M42 = m31; M43 = m32; M44 = m33;\n        }\n    }"
mat4_close_replacement = """            M41 = m30; M42 = m31; M43 = m32; M44 = m33;
        }

        /// <summary>Multiplies two Matrix4 values (row-major, post-multiply convention).
        /// Note: this struct uses M[r][c] naming where M11 = row 1 col 1, etc.
        /// Added for the RealSky compute pass which needs inverse(view * projection).
        /// </summary>
        public static Matrix4 operator *(Matrix4 left, Matrix4 right)
        {
            return new Matrix4(
                left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31 + left.M14 * right.M41,
                left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32 + left.M14 * right.M42,
                left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33 + left.M14 * right.M43,
                left.M11 * right.M14 + left.M12 * right.M24 + left.M13 * right.M34 + left.M14 * right.M44,

                left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31 + left.M24 * right.M41,
                left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32 + left.M24 * right.M42,
                left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33 + left.M24 * right.M43,
                left.M21 * right.M14 + left.M22 * right.M24 + left.M23 * right.M34 + left.M24 * right.M44,

                left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31 + left.M34 * right.M41,
                left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32 + left.M34 * right.M42,
                left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33 + left.M34 * right.M43,
                left.M31 * right.M14 + left.M32 * right.M24 + left.M33 * right.M34 + left.M34 * right.M44,

                left.M41 * right.M11 + left.M42 * right.M21 + left.M43 * right.M31 + left.M44 * right.M41,
                left.M41 * right.M12 + left.M42 * right.M22 + left.M43 * right.M32 + left.M44 * right.M42,
                left.M41 * right.M13 + left.M42 * right.M23 + left.M43 * right.M33 + left.M44 * right.M43,
                left.M41 * right.M14 + left.M42 * right.M24 + left.M43 * right.M34 + left.M44 * right.M44
            );
        }

        /// <summary>
        /// Computes the inverse of a 4x4 matrix using cofactor expansion.
        /// Returns identity if the matrix is singular.
        /// Added for the RealSky compute pass which needs inverse(view * projection).
        /// </summary>
        public static void Invert(ref Matrix4 m, out Matrix4 result)
        {
            // 4x4 matrix inverse via cofactors / adjugate.
            // Uses the existing M[r][c] field names (M11 = row 1 col 1).
            // Source: standard 4x4 inverse derivation, matches OpenTK 3.x semantics.
            // First, remap the row-major M11..M44 fields to canonical m00..m33 names
            // so the cofactor algebra below reads cleanly.
            float m00 = m.M11, m01 = m.M12, m02 = m.M13, m03 = m.M14;
            float m10 = m.M21, m11 = m.M22, m12 = m.M23, m13 = m.M24;
            float m20 = m.M31, m21 = m.M32, m22 = m.M33, m23 = m.M34;
            float m30 = m.M41, m31 = m.M42, m32 = m.M43, m33 = m.M44;

            float b00 = m00 * m11 - m01 * m10;
            float b01 = m00 * m12 - m02 * m10;
            float b02 = m00 * m13 - m03 * m10;
            float b03 = m01 * m12 - m02 * m11;
            float b04 = m01 * m13 - m03 * m11;
            float b05 = m02 * m13 - m03 * m12;
            float b06 = m20 * m31 - m21 * m30;
            float b07 = m20 * m32 - m22 * m30;
            float b08 = m20 * m33 - m23 * m30;
            float b09 = m21 * m32 - m22 * m31;
            float b10 = m21 * m33 - m23 * m31;
            float b11 = m22 * m33 - m23 * m32;

            float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;

            if (System.Math.Abs(det) < 1e-12f)
            {
                result = new Matrix4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                return;
            }

            float invDet = 1.0f / det;

            result = new Matrix4(
                ( m11 * b11 - m12 * b10 + m13 * b09) * invDet,
                (-m01 * b11 + m02 * b10 - m03 * b09) * invDet,
                ( m31 * b05 - m32 * b04 + m33 * b03) * invDet,
                (-m21 * b05 + m22 * b04 - m23 * b03) * invDet,
                (-m10 * b11 + m12 * b08 - m13 * b07) * invDet,
                ( m00 * b11 - m02 * b08 + m03 * b07) * invDet,
                (-m30 * b05 + m32 * b02 - m33 * b01) * invDet,
                ( m20 * b05 - m22 * b02 + m23 * b01) * invDet,
                ( m10 * b10 - m11 * b08 + m13 * b06) * invDet,
                (-m00 * b10 + m01 * b08 - m03 * b06) * invDet,
                ( m30 * b04 - m31 * b02 + m33 * b00) * invDet,
                (-m20 * b04 + m21 * b02 - m23 * b00) * invDet,
                (-m10 * b09 + m11 * b07 - m12 * b06) * invDet,
                ( m00 * b09 - m01 * b07 + m02 * b06) * invDet,
                (-m30 * b03 + m31 * b01 - m32 * b00) * invDet,
                ( m20 * b03 - m21 * b01 + m22 * b00) * invDet
            );
        }
    }"""

with open(p, "r", encoding="utf-8-sig") as f:
    content = f.read()

if mat4_close_anchor not in content:
    raise RuntimeError(f"Matrix4 close anchor not found in {p}")
content = content.replace(mat4_close_anchor, mat4_close_replacement)

# Now append the TextureAccess + SizedInternalFormat enums before the final namespace close
last_brace = content.rfind("}")
if last_brace == -1:
    raise RuntimeError(f"No closing brace in {p}")
enum_block = """
    // --- GL 4.3 image-load-store enums (added for RealSky compute pass) ---

    /// <summary>Access mode for image textures (GL_READ_ONLY / GL_WRITE_ONLY / GL_READ_WRITE).</summary>
    public enum TextureAccess : int
    {
        ReadOnly = 0x88B8,
        WriteOnly = 0x88B9,
        ReadWrite = 0x88BA,
    }

    /// <summary>
    /// Sized internal formats used by GL 4.3 image textures. Only the subset
    /// required by the RealSky compute pass is defined here — extend as needed.
    /// Values match OpenGL / Silk.NET.OpenGL.GLEnum.
    /// </summary>
    public enum SizedInternalFormat : int
    {
        Rgba32f = 0x8814,
        Rgba16f = 0x881A,
        Rg32f = 0x8230,
        Rg16f = 0x822F,
        R11fG11fB10f = 0x8C3A,
        R32f = 0x822E,
        R16f = 0x822D,
        Rgba8 = 0x8058,
        Rgba8Snorm = 0x8F97,
        R8 = 0x8229,
        R8Snorm = 0x8F94,
        Rgba32ui = 0x8D70,
        Rgba16ui = 0x8D76,
        Rgb10A2ui = 0x906F,
        Rgba8ui = 0x8D7C,
        R32ui = 0x8236,
        R16ui = 0x8234,
        R8ui = 0x8232,
        Rgba32i = 0x8D82,
        Rgba16i = 0x8D88,
        Rgba8i = 0x8D8E,
        R32i = 0x8235,
        R16i = 0x8233,
        R8i = 0x8231,
    }
"""
new_content = content[:last_brace] + enum_block + content[last_brace:]
with open(p, "w", encoding="utf-8") as f:
    f.write(new_content)
print(f"  patched (Matrix4 + enums) {p}")
modified_files.append("source/LibRender2/openGL/OpenTKCompatibility.cs")

# ============================================================
# 5. NewRenderer.cs
# ============================================================
print("[5/10] NewRenderer.cs")
p = f"{REPO}/source/OpenBVE/Graphics/NewRenderer.cs"

replace_once(p,
    "\t\tprivate Overlays overlays;\n\t\tinternal Touch Touch;\n\n\t\tpublic override void Initialize()",
    "\t\tprivate Overlays overlays;\n\t\tinternal Touch Touch;\n\t\tprivate double totalTime;\n\n\t\tpublic override void Initialize()"
)

replace_once(p,
    "\t\t\tPerformCSMShadowPass();\n\t\t\tPerformCFRCullAndUpload();\n\n            if (Lighting.ShouldInitialize)",
    "\t\t\tPerformCSMShadowPass();\n\t\t\tPerformCFRCullAndUpload();\n\n\t\t\t// RealSky atmospheric pass runs after shadow + CFR setup, before the\n\t\t\t// background draw, so the sky image is available for the rest of the frame.\n\t\t\ttotalTime += TimeElapsed;\n\t\t\tif (Interface.CurrentOptions.RealSkyEnabled || Program.CurrentRoute.Atmosphere.RealSkyOverride)\n\t\t\t{\n\t\t\t\tdouble az = Interface.CurrentOptions.RealSkyAzimuth;\n\t\t\t\tdouble el = Interface.CurrentOptions.RealSkyElevation;\n\t\t\t\tif (Program.CurrentRoute.Atmosphere.RealSkyOverride)\n\t\t\t\t{\n\t\t\t\t\taz = Program.CurrentRoute.Atmosphere.RealSkyAzimuth;\n\t\t\t\t\tel = Program.CurrentRoute.Atmosphere.RealSkyElevation;\n\t\t\t\t}\n\t\t\t\tdouble ra = az * 0.0174532925199433;\n\t\t\t\tdouble re = el * 0.0174532925199433;\n\t\t\t\tVector3 sunDir = new Vector3(Math.Sin(ra) * Math.Cos(re), Math.Sin(re), Math.Cos(ra) * Math.Cos(re));\n\t\t\t\tPerformRealSkyPass(totalTime, sunDir);\n\t\t\t}\n\n            if (Lighting.ShouldInitialize)"
)

replace_once(p,
    "\t\t\tDefaultShader.Activate();\n\t\t\tBindCSMToDefaultShader();\n\t\t\tBindCFRToDefaultShader();\n\n            // render background",
    "\t\t\tDefaultShader.Activate();\n\t\t\tBindCSMToDefaultShader();\n\t\t\tBindCFRToDefaultShader();\n\t\t\t// Bind sky image as a sampler on the default shader (compute path only;\n\t\t\t// fallback path drew directly into the framebuffer above).\n\t\t\tBindRealSkyToDefaultShader();\n\n            // render background"
)
modified_files.append("source/OpenBVE/Graphics/NewRenderer.cs")

# ============================================================
# 6. Atmosphere.cs
# ============================================================
print("[6/10] Atmosphere.cs")
p = f"{REPO}/source/RouteManager2/Climate/Atmosphere.cs"
replace_once(p,
    "\t\tpublic Vector3 LightPosition = new Vector3(0.223606797749979f, 0.86602540378444f, -0.447213595499958f);\n\t\t\n\t\t/// <summary>The diffuse light color</summary>",
    "\t\tpublic Vector3 LightPosition = new Vector3(0.223606797749979f, 0.86602540378444f, -0.447213595499958f);\n\n\t\t/// <summary>The azimuth of the RealSky sun in degrees (0 = North, 180 = South)</summary>\n\t\tpublic double RealSkyAzimuth = 180.0;\n\n\t\t/// <summary>The elevation of the RealSky sun in degrees (0 = Horizon, 90 = Zenith)</summary>\n\t\tpublic double RealSkyElevation = 45.0;\n\n\t\t/// <summary>Whether the RealSky parameters are overridden by the route</summary>\n\t\tpublic bool RealSkyOverride = false;\n\n\t\t/// <summary>The diffuse light color</summary>"
)
modified_files.append("source/RouteManager2/Climate/Atmosphere.cs")

# ============================================================
# 7. BaseOptions.cs
# ============================================================
print("[7/10] BaseOptions.cs")
p = f"{REPO}/source/OpenBveApi/System/BaseOptions.cs"
replace_once(p,
    "\t\tpublic bool AutoReloadObjects;\n\t\t\n\t\t/// <summary>The near clipping plane for scenery</summary>",
    "\t\tpublic bool AutoReloadObjects;\n\n\t\t// --- RealSky atmospheric system ---\n\t\t/// <summary>Whether RealSky atmospheric clouds are enabled (requires New Renderer)</summary>\n\t\tpublic bool RealSkyEnabled;\n\n\t\t/// <summary>The azimuth of the RealSky sun in degrees (0 = North, 180 = South)</summary>\n\t\tpublic double RealSkyAzimuth = 180.0;\n\n\t\t/// <summary>The elevation of the RealSky sun in degrees (0 = Horizon, 90 = Zenith)</summary>\n\t\tpublic double RealSkyElevation = 45.0;\n\n\t\t/// <summary>The near clipping plane for scenery</summary>"
)
modified_files.append("source/OpenBveApi/System/BaseOptions.cs")

# ============================================================
# 8. BaseOptions.OptionsSection.cs
# ============================================================
print("[8/10] BaseOptions.OptionsSection.cs")
p = f"{REPO}/source/OpenBveApi/System/BaseOptions.OptionsSection.cs"
replace_once(p,
    "\t\t/// <summary>Contains loading related options</summary>\n\t\tLoading = 19\n\t}",
    "\t\t/// <summary>Contains loading related options</summary>\n\t\tLoading = 19,\n\t\t/// <summary>Contains RealSky atmospheric rendering options</summary>\n\t\tRealSky = 20\n\t}"
)
modified_files.append("source/OpenBveApi/System/BaseOptions.OptionsSection.cs")

# ============================================================
# 9. BaseOptions.OptionsKey.cs
# ============================================================
print("[9/10] BaseOptions.OptionsKey.cs")
p = f"{REPO}/source/OpenBveApi/System/BaseOptions.OptionsKey.cs"
replace_once(p,
    "\t\tForward,\n\t\tBackward\n    }\n}",
    "\t\tForward,\n\t\tBackward,\n\t\t// RealSky\n\t\tRealSkyEnabled,\n\t\tRealSkyAzimuth,\n\t\tRealSkyElevation\n    }\n}"
)
modified_files.append("source/OpenBveApi/System/BaseOptions.OptionsKey.cs")

# ============================================================
# 10. Options.cs
# ============================================================
print("[10/10] Options.cs")
p = f"{REPO}/source/OpenBVE/System/Options.cs"

replace_once(p,
    "\t\t\t\t\tFont = \"Microsoft Sans Serif\";\n\t\t\t\t\t\tbreak;\n\t\t\t\t}\n\t\t\t}\n\n\t\t\t/// <summary>Saves the options to the specified filename</summary>",
    "\t\t\t\t\tFont = \"Microsoft Sans Serif\";\n\t\t\t\t\t\tbreak;\n\t\t\t\t}\n\n\t\t\t\t// RealSky defaults\n\t\t\t\tRealSkyEnabled = false;\n\t\t\t\tRealSkyAzimuth = 180.0;\n\t\t\t\tRealSkyElevation = 45.0;\n\t\t\t}\n\n\t\t\t/// <summary>Saves the options to the specified filename</summary>"
)

replace_once(p,
    "\t\t\t\tBuilder.AppendLine(\"panel2extendedminsize = \" + Panel2ExtendedMinSize.ToString(Culture));\n\t\t\t\ttry",
    "\t\t\t\tBuilder.AppendLine(\"panel2extendedminsize = \" + Panel2ExtendedMinSize.ToString(Culture));\n\n\t\t\t\t// RealSky atmospheric rendering options\n\t\t\t\tBuilder.AppendLine();\n\t\t\t\tBuilder.AppendLine(\"[RealSky]\");\n\t\t\t\tBuilder.AppendLine(\"RealSkyEnabled = \" + (RealSkyEnabled ? \"true\" : \"false\"));\n\t\t\t\tBuilder.AppendLine(\"RealSkyAzimuth = \" + RealSkyAzimuth.ToString(Culture));\n\t\t\t\tBuilder.AppendLine(\"RealSkyElevation = \" + RealSkyElevation.ToString(Culture));\n\t\t\t\ttry"
)

replace_once(p,
    "\t\t\t\t\t\t\tblock.GetValue(OptionsKey.Panel2Extended, out CurrentOptions.Panel2ExtendedMode);\n\t\t\t\t\t\t\tblock.GetValue(OptionsKey.Panel2ExtendedMinSize, out CurrentOptions.Panel2ExtendedMinSize);\n\t\t\t\t\t\t\tbreak;",
    "\t\t\t\t\t\t\tblock.GetValue(OptionsKey.Panel2Extended, out CurrentOptions.Panel2ExtendedMode);\n\t\t\t\t\t\t\tblock.GetValue(OptionsKey.Panel2ExtendedMinSize, out CurrentOptions.Panel2ExtendedMinSize);\n\t\t\t\t\t\t\tbreak;\n\t\t\t\t\t\tcase OptionsSection.RealSky:\n\t\t\t\t\t\t\tblock.GetValue(OptionsKey.RealSkyEnabled, out CurrentOptions.RealSkyEnabled);\n\t\t\t\t\t\t\tblock.TryGetValue(OptionsKey.RealSkyAzimuth, ref CurrentOptions.RealSkyAzimuth);\n\t\t\t\t\t\t\tblock.TryGetValue(OptionsKey.RealSkyElevation, ref CurrentOptions.RealSkyElevation);\n\t\t\t\t\t\t\tbreak;"
)
modified_files.append("source/OpenBVE/System/Options.cs")

# ============================================================
# Copy new files
# ============================================================
print("[+] Copying new files")
PORT = "/home/z/my-project/realsky-gl43-port"

os.makedirs(f"{REPO}/assets/Shaders", exist_ok=True)
shutil.copy(f"{PORT}/shaders/RealSky.comp", f"{REPO}/assets/Shaders/RealSky.comp")
print(f"  copied assets/Shaders/RealSky.comp")

os.makedirs(f"{REPO}/source/LibRender2/Atmosphere", exist_ok=True)
shutil.copy(f"{PORT}/source/LibRender2/Atmosphere/RealSkyComputeShader.cs",
            f"{REPO}/source/LibRender2/Atmosphere/RealSkyComputeShader.cs")
shutil.copy(f"{PORT}/source/LibRender2/Atmosphere/RealSkyPass.cs",
            f"{REPO}/source/LibRender2/Atmosphere/RealSkyPass.cs")
print(f"  copied source/LibRender2/Atmosphere/RealSkyComputeShader.cs")
print(f"  copied source/LibRender2/Atmosphere/RealSkyPass.cs")

os.makedirs(f"{REPO}/assets/Shaders/Atmosphere", exist_ok=True)
subprocess.run(["git", "show", "origin/realsky:assets/Shaders/Atmosphere/RealSky.vert"],
               cwd=REPO, stdout=open(f"{REPO}/assets/Shaders/Atmosphere/RealSky.vert", "w"),
               check=True)
subprocess.run(["git", "show", "origin/realsky:assets/Shaders/Atmosphere/RealSky.frag"],
               cwd=REPO, stdout=open(f"{REPO}/assets/Shaders/Atmosphere/RealSky.frag", "w"),
               check=True)
print(f"  restored assets/Shaders/Atmosphere/RealSky.vert (fallback)")
print(f"  restored assets/Shaders/Atmosphere/RealSky.frag (fallback)")

# ============================================================
# Generate unified diffs (clean, git-apply-friendly)
# ============================================================
print("[+] Generating unified diff patches")
patches_dir = f"{PORT}/patches"
for f in os.listdir(patches_dir):
    if f.endswith(".patch"):
        os.remove(f"{patches_dir}/{f}")

for relpath in modified_files:
    subprocess.run(["git", "add", relpath], cwd=REPO, check=True)

# Stage the new files too so they show up as additions in the diff
subprocess.run(["git", "add", "assets/Shaders/RealSky.comp"], cwd=REPO, check=True)
subprocess.run(["git", "add", "assets/Shaders/Atmosphere/RealSky.vert"], cwd=REPO, check=True)
subprocess.run(["git", "add", "assets/Shaders/Atmosphere/RealSky.frag"], cwd=REPO, check=True)
subprocess.run(["git", "add", "source/LibRender2/Atmosphere/RealSkyComputeShader.cs"], cwd=REPO, check=True)
subprocess.run(["git", "add", "source/LibRender2/Atmosphere/RealSkyPass.cs"], cwd=REPO, check=True)

result = subprocess.run(
    ["git", "diff", "--cached", "--no-color"],
    cwd=REPO, capture_output=True, text=True, check=True
)
full_diff = result.stdout

# Write the consolidated patch (applies everything in one shot)
# Re-stitch without leading blank line per file block
rebuilt = []
parts = re.split(r'^(diff --git .+)$', full_diff, flags=re.MULTILINE)
i = 1
while i < len(parts):
    header = parts[i]
    content = parts[i+1].lstrip('\n') if i+1 < len(parts) else ""
    rebuilt.append(header)
    rebuilt.append(content)
    i += 2
with open(f"{patches_dir}/00_all-files.patch", "w") as f:
    f.write('\n'.join(rebuilt))
print(f"  wrote {patches_dir}/00_all-files.patch (consolidated)")

# Split per-file
file_diffs = re.split(r'^(diff --git .+)$', full_diff, flags=re.MULTILINE)
i = 1
order_map = {
    'BaseRenderer.cs': '01',
    'ShaderLayout.cs': '02',
    'Shader.cs': '03',
    'LibRender2.csproj': '04',
    'GL.cs': '04b',
    'OpenTKCompatibility.cs': '04c',
    'NewRenderer.cs': '05',
    'Atmosphere.cs': '06',
    'BaseOptions.cs': '07',
    'BaseOptions.OptionsSection.cs': '08',
    'BaseOptions.OptionsKey.cs': '09',
    'Options.cs': '10',
    'RealSky.comp': '11',
    'RealSky.vert': '12',
    'RealSky.frag': '13',
    'RealSkyComputeShader.cs': '14',
    'RealSkyPass.cs': '15',
}
while i < len(file_diffs):
    header = file_diffs[i]
    content = file_diffs[i+1] if i+1 < len(file_diffs) else ""
    m = re.match(r'diff --git a/(\S+) b/(\S+)', header)
    if m:
        filename = m.group(1)
        basename = os.path.basename(filename)
        prefix = order_map.get(basename, '99')
        safe_name = basename.replace('.', '_') + '.patch'
        patch_path = f"{patches_dir}/{prefix}_{safe_name}"
        # Strip leading blank line from content (artifact of regex split)
        content = content.lstrip('\n')
        with open(patch_path, 'w') as f:
            f.write(header + '\n' + content)
        print(f"  wrote {patch_path}")
    i += 2

# Reset the index (keep working tree changes intact)
subprocess.run(["git", "reset", "HEAD"], cwd=REPO, capture_output=True)
print("\nDone. Patches at /home/z/my-project/realsky-gl43-port/patches/")
