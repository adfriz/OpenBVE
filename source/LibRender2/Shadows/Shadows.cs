using System;
using System.Text.RegularExpressions;
using LibRender2.Shaders;
using OpenBveApi.Interface;
using OpenTK.Graphics.OpenGL;

namespace LibRender2.ShadowMapping
{
    public class Shadows
    {
        private readonly BaseRenderer renderer;
        private IShadowRenderer _renderer;

        internal CascadedShadowMap Map => (_renderer as ShadowRendererBase)?.Map;
        internal CascadedShadowCaster Caster => (_renderer as ShadowRendererBase)?.Caster;
        internal ShadowDepthShader DepthShader => (_renderer as ShadowRendererBase)?.DepthShader;

        public bool Enabled => _renderer?.Enabled ?? false;
        public float Strength => _renderer?.Strength ?? 0f;

        public Shadows(BaseRenderer renderer)
        {
            this.renderer = renderer;
        }

        public void Initialize()
        {
            _renderer?.Dispose();
            _renderer = null;

            var opts = renderer.currentOptions;
            if (opts.ShadowResolution == ShadowMapResolution.Off)
            {
                renderer.fileSystem.AppendToLogFile("[CSM] Shadows disabled by user setting.");
                return;
            }

            var glVersion = DetectOpenGLVersion();

            if (glVersion >= new Version(4, 3))
            {
                renderer.currentHost.AddMessage(MessageType.Information, false, $"OpenGL {glVersion} — using GL 4.3 compute shadow path.");
                _renderer = new ShadowRendererGL43(renderer);
            }
            else
            {
                renderer.currentHost.AddMessage(MessageType.Information, false, $"OpenGL {glVersion} — using GL 4.1 fallback shadow path.");
                _renderer = new ShadowRendererGL41(renderer);
            }

            _renderer.Initialize();
        }

        public void RenderPass()
        {
            _renderer?.RenderDepthPass();
        }

        public void Bind(Shader shader)
        {
            _renderer?.BindToMainShader(shader);
        }

        public void Dispose()
        {
            _renderer?.Dispose();
            _renderer = null;
        }

        internal static Version DetectOpenGLVersion()
        {
            try
            {
                string versionStr = GL.GetString(StringName.Version);
                if (string.IsNullOrEmpty(versionStr))
                    return new Version(3, 3);

                var match = Regex.Match(versionStr, @"^(\d+)\.(\d+)");
                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out int major) &&
                    int.TryParse(match.Groups[2].Value, out int minor))
                {
                    return new Version(major, minor);
                }
            }
            catch
            {
                // ignore
            }
            return new Version(3, 3);
        }
    }
}
