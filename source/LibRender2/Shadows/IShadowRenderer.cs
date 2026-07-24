using System;
using LibRender2.Shaders;

namespace LibRender2.ShadowMapping
{
    public interface IShadowRenderer : IDisposable
    {
        bool Enabled { get; }
        float Strength { get; }
        void Initialize();
        void RenderDepthPass();
        void BindToMainShader(Shader shader);
    }
}
