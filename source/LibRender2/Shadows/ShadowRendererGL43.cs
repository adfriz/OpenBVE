using System;
using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.ShadowMapping
{
    public class ShadowRendererGL43 : ShadowRendererBase
    {
        private int shadowMaskTexture;
        private int shadowMaskShader;

        public ShadowRendererGL43(BaseRenderer renderer) : base(renderer) { }

        public override void Initialize()
        {
            shadowMaskTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, shadowMaskTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
                Math.Max(1, Renderer.Screen.Width), Math.Max(1, Renderer.Screen.Height), 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            Renderer.fileSystem.AppendToLogFile("[CSM] Shadow mask texture created — GL 4.3 compute path ready.");

            base.Initialize();
        }

        protected override void OnBeforeDepthPass()
        {
            GL.Enable(EnableCap.PolygonOffsetFill);
        }

        protected override void SetupPolygonOffset(int cascadeIndex)
        {
            GL.PolygonOffset(Caster.PolygonOffsetFactors[cascadeIndex], Caster.PolygonOffsetUnits[cascadeIndex]);
        }

        protected override void OnAfterDepthPass()
        {
            GL.Disable(EnableCap.PolygonOffsetFill);
            DispatchShadowMask();
        }

        private void DispatchShadowMask()
        {
            // Compute shader dispatch will be implemented here
            // 1. Bind shadow mask as image store (GL.BindImageTexture)
            // 2. Activate compute shader program
            // 3. Set uniforms (light-space matrices, cascade data, etc.)
            // 4. GL.DispatchCompute(...)
            // 5. GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit)
        }

        public override void BindToMainShader(Shader shader)
        {
            if (!Enabled || Map == null || Caster == null)
            {
                shader.SetShadowEnabled(false);
                BindNullDepthMaps();
                return;
            }

            shader.Activate();
            shader.SetShadowEnabled(true);
            shader.SetShadowStrength((float)Renderer.currentOptions.ShadowStrength);
            shader.SetCurrentViewMatrix(Renderer.CurrentViewMatrix);

            Map.BindAllCascadesForReading(TextureUnit.Texture4);

            int cascadeCount = Caster.CascadeCount;
            for (int i = 0; i < cascadeCount; i++)
            {
                shader.SetCascadeLightSpaceMatrix(i, Caster.LightSpaceMatrices[i]);
                shader.SetCascadeShadowMapUnit(i, 4 + i);
                shader.SetShadowSplitDistance(i, (float)Caster.SplitDistances[i]);
                shader.SetCascadeBias(i, (float)Renderer.currentOptions.ShadowBias);
                shader.SetNormalBias(i, (float)Renderer.currentOptions.ShadowNormalBias);
            }

            for (int i = cascadeCount; i < 4; i++)
            {
                shader.SetShadowSplitDistance(i, 0.0f);
            }
            shader.SetShadowCascadeCount(cascadeCount);

            // Bind shadow mask texture for use by main shader (when compute path is active)
            GL.ActiveTexture(TextureUnit.Texture8);
            GL.BindTexture(TextureTarget.Texture2D, shadowMaskTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        protected override void Cleanup()
        {
            if (shadowMaskTexture != 0)
            {
                GL.DeleteTexture(shadowMaskTexture);
                shadowMaskTexture = 0;
            }
            if (shadowMaskShader != 0)
            {
                GL.DeleteProgram(shadowMaskShader);
                shadowMaskShader = 0;
            }
            base.Cleanup();
        }
    }
}
