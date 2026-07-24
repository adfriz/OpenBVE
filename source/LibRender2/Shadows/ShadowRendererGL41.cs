using LibRender2.Shaders;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.ShadowMapping
{
    public class ShadowRendererGL41 : ShadowRendererBase
    {
        public ShadowRendererGL41(BaseRenderer renderer) : base(renderer) { }

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
        }
    }
}
