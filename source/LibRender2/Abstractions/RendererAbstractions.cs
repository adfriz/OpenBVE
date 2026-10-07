// Abstraction seams for LibRender2.
//
// Goal: break the direct hub-and-spoke dependency where every subsystem takes
// the concrete BaseRenderer. New code should depend on these narrow interfaces;
// BaseRenderer implements them all, so existing callers keep compiling.

using LibRender2.Cameras;
using LibRender2.Fogs;
using LibRender2.Lightings;
using LibRender2.Screens;
using LibRender2.Shaders;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Textures;
using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace LibRender2.Abstractions
{
	/// <summary>Read-only access to host plumbing owned by the renderer.</summary>
	public interface IRendererContext
	{
		HostInterface Host { get; }
		BaseOptions Options { get; }
		FileSystem FileSystem { get; }
		Screen Screen { get; }
		Vector2 ScaleFactor { get; }
		void RunInRenderThread(System.Threading.ThreadStart job, int timeout);
	}

	/// <summary>Encapsulates mutable OpenGL state caching (blend / alpha / cull / bindings).</summary>
	public interface IRenderState
	{
		bool BlendEnabled { get; }
		bool AlphaTestEnabled { get; }
		bool CullFaceEnabled { get; set; }
		int LastVAO { get; set; }
		OpenGlTexture LastBoundTexture { get; set; }
		Color32 LastColor { get; set; }

		void SetBlendFunc(BlendingFactor srcFactor, BlendingFactor destFactor);
		void SetBlendFunc();
		void UnsetBlendFunc();
		void RestoreBlendFunc();

		void SetAlphaFunc(AlphaFunction comparison, float value);
		void SetAlphaFunc();
		void UnsetAlphaFunc();
		void RestoreAlphaFunc();

		void ResetOpenGlState();
	}

	/// <summary>Scene stores needed by culling / visibility.</summary>
	public interface ISceneProvider
	{
		List<ObjectState> StaticObjectStates { get; }
		List<ObjectState> DynamicObjectStates { get; }
		CameraProperties Camera { get; }
		TrackFollower CameraTrackFollower { get; }
		double LastUpdatedTrackPosition { get; set; }
	}

	/// <summary>Everything FaceRenderer needs; implemented by BaseRenderer.</summary>
	public interface IFaceRendererHost : IRendererContext, IRenderState
	{
		CameraProperties Camera { get; }
		Lighting Lighting { get; }
		Fog Fog { get; }
		AbstractShader CurrentShader { get; set; }
		Shader DefaultShader { get; }
		Matrix4D GetCurrentViewMatrix();
		bool OptionLighting { get; }
		bool OptionBackFaceCulling { get; }
		bool OptionWireFrame { get; }
		bool OptionNormals { get; }
	}
}
