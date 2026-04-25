using System;
using System.IO;
using System.Reflection;
using System.Text;
using LibRender2.Fogs;
using OpenTK.Graphics.OpenGL;
using Raylib_cs;
using AlphaFunction = OpenTK.Graphics.OpenGL.AlphaFunction;



namespace LibRender2.Shaders
{
	public class AbstractShader : IDisposable
	{
		internal readonly Raylib_cs.Shader Handle;

		internal readonly BaseRenderer Renderer;
		internal bool IsActive;

		public AbstractShader(BaseRenderer renderer, string vertexShaderName, string fragmentShaderName, bool isFromStream, bool fragColor)
		{
			Renderer = renderer;
			string vertexSource = "";
			string fragmentSource = "";

			if (isFromStream)
			{
				Assembly thisAssembly = Assembly.GetExecutingAssembly();
				vertexSource = GetShaderSourceFromStream(thisAssembly, vertexShaderName, "vert");
				fragmentSource = GetShaderSourceFromStream(thisAssembly, fragmentShaderName, "frag");
			}
			else
			{
				vertexSource = File.ReadAllText(vertexShaderName, Encoding.UTF8);
				fragmentSource = File.ReadAllText(fragmentShaderName, Encoding.UTF8);
			}

			Handle = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
		}

		private string GetShaderSourceFromStream(Assembly assembly, string name, string ext)
		{
			using (Stream stream = assembly.GetManifestResourceStream($"LibRender2.{name}.{ext}"))
			{
				if (stream != null)
				{
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						return reader.ReadToEnd();
					}
				}
			}
			return null;
		}

		public virtual void Activate()
		{
			if (IsActive) return;

			if (Renderer.CurrentShader != null)
			{
				Renderer.CurrentShader.IsActive = false;
			}
			
			Raylib.BeginShaderMode(Handle);
			IsActive = true;
			Renderer.lastVAO = uint.MaxValue;

			Renderer.CurrentShader = this;
		}

		public void Deactivate()
		{
			if (!IsActive) return;
			IsActive = false;
			Raylib.EndShaderMode();
			Renderer.lastVAO = uint.MaxValue;

		}

		public virtual void SetAlphaFunction(AlphaFunction alphaFunction, float alphaComparison) { }
		public virtual void SetAlphaTest(bool enabled) { }
		public virtual void SetFog(bool enabled) { }
		public virtual void SetFog(Fog Fog) { }

		private bool disposed;
		public void Dispose()
		{
			if (!disposed)
			{
				Raylib.UnloadShader(Handle);
				GC.SuppressFinalize(this);
				disposed = true;
			}
		}
	}
}
