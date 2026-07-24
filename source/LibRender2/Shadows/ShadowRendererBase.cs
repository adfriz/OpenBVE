using System;
using System.Collections.Generic;
using LibRender2.Objects;
using LibRender2.Shaders;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.ShadowMapping
{
    public abstract class ShadowRendererBase : IShadowRenderer
    {
        protected readonly BaseRenderer Renderer;
        internal CascadedShadowMap Map;
        internal CascadedShadowCaster Caster;
        internal ShadowDepthShader DepthShader;

        public bool Enabled { get; protected set; }
        public float Strength { get; protected set; }

        protected ShadowRendererBase(BaseRenderer renderer)
        {
            Renderer = renderer;
        }

        public virtual void Initialize()
        {
            var opts = Renderer.currentOptions;

            if (opts.ShadowResolution == ShadowMapResolution.Off)
            {
                Cleanup();
                Enabled = false;
                Renderer.fileSystem.AppendToLogFile("[CSM] Shadows disabled by user setting.");
                return;
            }

            int resolution = Math.Max(1, (int)opts.ShadowResolution);
            int cascadeCount = (int)opts.ShadowCascades;
            double shadowDistance = opts.ShadowDrawDistance == ShadowDistance.ViewingDistance
                ? opts.ViewingDistance
                : (double)(int)opts.ShadowDrawDistance;
            shadowDistance = Math.Max(1.0, shadowDistance);
            Strength = (float)opts.ShadowStrength;

            try
            {
                Map = CreateOrResizeMap(cascadeCount, resolution);
                Caster = CreateOrResizeCaster(cascadeCount);
                Caster.ShadowDistance = shadowDistance;
                Caster.Resolution = resolution;
                Caster.SplitLambda = 0.75;
                Caster.DepthMargin = 150.0;

                if (DepthShader == null)
                {
                    DepthShader = new ShadowDepthShader(Renderer, "shadow_depth", "shadow_depth", true);
                }

                Enabled = true;
                Renderer.fileSystem.AppendToLogFile(
                    $"[CSM] Initialized: {cascadeCount} cascades, {resolution}\u00d7{resolution}, " +
                    $"distance={shadowDistance}m, strength={Strength:P0}");
            }
            catch (Exception ex)
            {
                Renderer.fileSystem.AppendToLogFile($"[CSM] Init failed: {ex.Message}");
                Enabled = false;
                GL.GetError();
            }
        }

        private CascadedShadowMap CreateOrResizeMap(int cascadeCount, int resolution)
        {
            if (Map == null)
                return new CascadedShadowMap(cascadeCount, resolution);
            Map.Resize(cascadeCount, resolution);
            return Map;
        }

        private CascadedShadowCaster CreateOrResizeCaster(int cascadeCount)
        {
            if (Caster == null || cascadeCount != Caster.CascadeCount)
                return new CascadedShadowCaster(cascadeCount);
            return Caster;
        }

        public virtual void RenderDepthPass()
        {
            if (!Enabled || Map == null || Caster == null || DepthShader == null)
                return;

            Vector3 lightDir = new Vector3(
                -Renderer.Lighting.OptionLightPosition.X,
                -Renderer.Lighting.OptionLightPosition.Y,
                 Renderer.Lighting.OptionLightPosition.Z);

            if (lightDir.IsNullVector())
                return;

            Caster.Resolution = Map.Resolution;
            if (Renderer.currentOptions.ShadowDrawDistance == ShadowDistance.ViewingDistance)
                Caster.ShadowDistance = Renderer.currentOptions.ViewingDistance;

            Caster.Update(lightDir, Renderer.CurrentViewMatrix, Renderer.CurrentProjectionMatrix,
                0.1, Renderer.Camera.VerticalViewingAngle, Renderer.Screen.AspectRatio);

            Renderer.CurrentShader?.Deactivate();
            DepthShader.Activate();
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            if (Renderer.OptionBackFaceCulling)
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Front);
            }
            else
            {
                GL.Disable(EnableCap.CullFace);
            }
            GL.DepthMask(true);
            OnBeforeDepthPass();
            DepthShader.SetTexture(0);

            for (int i = 0; i < Caster.CascadeCount; i++)
            {
                Map.BindCascadeForWriting(i);
                GL.Clear(ClearBufferMask.DepthBufferBit);
                SetupPolygonOffset(i);
                DepthShader.SetLightSpaceMatrix(Caster.LightSpaceMatrices[i]);

                lock (Renderer.VisibleObjects.LockObject)
                {
                    int lastVAO = -1;
                    double maxDistance = Renderer.currentOptions.ShadowFilterCascades
                        ? Caster.SplitDistances[i] + 150.0
                        : double.MaxValue;
                    double maxDistanceSquared = maxDistance * maxDistance;

                    RenderFacesFiltered(Renderer.VisibleObjects.OpaqueFaces, ref lastVAO, maxDistanceSquared);
                    RenderFacesFiltered(Renderer.VisibleObjects.AlphaFaces, ref lastVAO, maxDistanceSquared);
                }
                Map.Unbind();
            }

            OnAfterDepthPass();
            GL.DepthFunc(DepthFunction.Lequal);
            GL.CullFace(CullFaceMode.Front);
            GL.Viewport(0, 0, Renderer.Screen.Width, Renderer.Screen.Height);
            Renderer.LastBoundTexture = null;
        }

        protected virtual void OnBeforeDepthPass() { }
        protected virtual void OnAfterDepthPass() { }
        protected abstract void SetupPolygonOffset(int cascadeIndex);

        public abstract void BindToMainShader(Shader shader);

        private void RenderFaces(IEnumerable<FaceState> faces, ref int lastVAO) =>
            RenderFacesFiltered(faces, ref lastVAO, double.MaxValue);

        private void RenderFacesFiltered(IEnumerable<FaceState> faces, ref int lastVAO, double maxDistanceSquared)
        {
            Vector3 cameraPos = Renderer.Camera.AbsolutePosition;

            foreach (var face in faces)
            {
                if (face.Object.Prototype.Mesh.VAO == null || face.Object.DisableShadowCasting)
                    continue;

                ObjectState state = face.Object;

                if (maxDistanceSquared < double.MaxValue)
                {
                    double dx = state.WorldPosition.X - cameraPos.X;
                    double dy = state.WorldPosition.Y - cameraPos.Y;
                    double dz = state.WorldPosition.Z - cameraPos.Z;
                    if (dx * dx + dy * dy + dz * dz > maxDistanceSquared)
                        continue;
                }

                DepthShader.SetModelMatrix(state.ModelMatrix * Renderer.Camera.TranslationMatrix);
                DepthShader.SetTextureMatrix(state.TextureTranslation);

                var material = face.Object.Prototype.Mesh.Materials[face.Face.Material];
                if ((material.Flags & MaterialFlags.NoShadow) != 0 ||
                    material.BlendMode == MeshMaterialBlendMode.Additive)
                    continue;

                if (material.DaytimeTexture != null &&
                    Renderer.currentHost.LoadTexture(ref material.DaytimeTexture,
                        (OpenGlTextureWrapMode)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)))
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D,
                        material.DaytimeTexture.OpenGlTextures[
                            (int)(material.WrapMode ?? OpenGlTextureWrapMode.ClampClamp)].Name);
                    DepthShader.SetHasTexture(true);
                }
                else
                {
                    DepthShader.SetHasTexture(false);
                }

                DepthShader.SetAlphaCutoff(0.5f);
                DepthShader.SetMaterialAlpha(material.Color.A / 255.0f);
                DepthShader.SetMaterialFlags(material.Flags);

                if (state.Matricies != null && state.Matricies.Length > 0)
                {
                    DepthShader.SetCurrentAnimationMatricies(state);
                    GL.BindBufferBase(BufferTarget.UniformBuffer, 0, state.MatrixBufferIndex);
                }

                var vao = (VertexArrayObject)face.Object.Prototype.Mesh.VAO;
                if (vao.handle != lastVAO)
                {
                    vao.Bind();
                    lastVAO = vao.handle;
                }
                if (Renderer.OptionBackFaceCulling)
                {
                    if ((face.Face.Flags & FaceFlags.Face2Mask) != 0)
                        GL.Disable(EnableCap.CullFace);
                    else
                        GL.Enable(EnableCap.CullFace);
                }
                PrimitiveType drawMode = Renderer.GetPrimitiveType(face.Face.Flags);
                vao.Draw(drawMode, face.Face.IboStartIndex, face.Face.Vertices.Length);
            }
        }

        protected void BindNullDepthMaps()
        {
            for (int i = 0; i < 4; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture4 + i);
                GL.BindTexture(TextureTarget.Texture2D, Renderer.nullDepthMap);
            }
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        protected virtual void Cleanup()
        {
            Map?.Dispose();
            Map = null;
            DepthShader?.Dispose();
            DepthShader = null;
            Caster = null;
            Enabled = false;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
