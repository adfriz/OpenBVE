//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, The OpenBVE Project
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
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using LibRender2.Shaders;
using OpenBveApi.Colors;
using OpenBveApi.Interface;
using OpenTK.Graphics.OpenGL;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;

namespace LibRender2.OIT
{
	/// <summary>
	/// Hybrid order-independent transparency renderer: a fixed number of exact
	/// depth-peeled front layers composited over the opaque scene, followed by a
	/// depth-weighted blended tail for all remaining layers (McGuire &amp; Bavoil,
	/// "Weighted Blended Order-Independent Transparency", JCGT Vol. 2, No. 2, 2013).
	/// </summary>
	/// <remarks>
	/// <para>Requires an OpenGL 4.0 context (per-draw-buffer blending via glBlendFunci is
	/// needed by the tail pass). <see cref="IsSupported"/> reports availability and must be
	/// checked by the caller before use.</para>
	/// <para>Construct after the renderer's <see cref="BaseRenderer.Initialize()"/> has run
	/// (a valid OpenGL context and the renderer's dummy VAO are required).</para>
	/// <para>Depth-peel semantics: classic front-to-back peeling with opaque occlusion discard.
	/// The first peel pass clears the frontier depth to the far plane and renders with
	/// glDepthFunc(GL_LESS), so the nearest transparent layer along each ray is captured
	/// (including layers behind opaque geometry). Subsequent peel passes render with
	/// glDepthFunc(GL_GREATER) against the frontier, which advances by itself as depth writes
	/// are enabled. Peel layers occluded by the opaque scene are discarded at composite time
	/// against the resolved opaque depth (see <see cref="EndOpaquePass"/>), and the weighted
	/// blended tail pass discards fragments lying behind the opaque scene in the shader, so
	/// transparent faces in front of opaque geometry render correctly.</para>
	/// </remarks>
	public class OitRenderer : IDisposable
	{
		private const float inv255 = 1.0f / 255.0f;

		private readonly BaseRenderer renderer;
		private bool supported;
		private bool supportLogged;
		private int width;
		private int height;
		private int samples;

		// Scene target (MSAA): receives the background and the opaque scene
		private int sceneFbo;
		private int sceneColorTex;
		private int sceneDepthTex;
		// Non-MSAA resolve targets
		private int resolveFbo;
		private int resolveColorTex;
		private int frontierFbo;
		private int frontierDepthTex;
		// Resolved opaque scene depth (single-sample); used by the peel composite and the tail pass occlusion discards
		private int opaqueDepthFbo;
		private int opaqueDepthTex;
		// Depth-peel target
		private int peelFbo;
		private int peelColorTex;
		// Weighted-blended tail target (dual draw buffers)
		private int accumFbo;
		private int accumTex;
		private int revealTex;
		private int whiteTex;

		// A second instance of the default program: identical shading, with uOitMode = 1
		private Shader tailShader;
		private int oitModeLocation;
		private int opaqueDepthLocation;
		private CompositeProgram compositeShader;
		private DepthResolveProgram depthResolveProgram;

		// Occlusion query for peel early-exit
		private int peelQuery;
		private bool peelQueryActive;
		private bool lastPeelPassHadSamples;
		private bool peeling;

		/// <summary>Number of depth-peel layers composited into the scene this frame</summary>
		/// <remarks>Reset to zero by <see cref="BeginScene"/>. Intended for debug overlays.</remarks>
		public int PeelsPerformed { get; private set; }

		/// <summary>Whether the hybrid OIT pipeline is usable on the current OpenGL context</summary>
		/// <remarks>False when the context is older than OpenGL 4.0, a shader failed to compile or a
		/// required framebuffer target could not be created. A message is logged to the host once.</remarks>
		public bool IsSupported
		{
			get { return supported; }
		}

		/// <summary>The tail-pass variant of the default shader (uOitMode = 1)</summary>
		/// <remarks>Activated automatically by <see cref="BeginTailPass"/>; exposed so the caller can
		/// draw faces with <see cref="FaceState.Draw(Shader)"/> when it wants to be explicit.</remarks>
		public Shader TailShader
		{
			get { return tailShader; }
		}

		/// <summary>Creates a new hybrid OIT renderer bound to the supplied base renderer</summary>
		/// <param name="renderer">The base renderer</param>
		/// <remarks>Requires a valid OpenGL context: call after <see cref="BaseRenderer.Initialize()"/>.</remarks>
		public OitRenderer(BaseRenderer renderer)
		{
			this.renderer = renderer;
			supported = QuerySupport();
			if (supported)
			{
				try
				{
					// Compile the regular default program a second time: the fragment shader
					// selects the weighted-blended output path via uOitMode (see default.frag),
					// which guarantees pixel-identical shading with the regular render path.
					tailShader = new Shader(renderer, "default", "default", true);
					oitModeLocation = GL.GetUniformLocation(tailShader.Handle, "uOitMode");
					GL.ProgramUniform1(tailShader.Handle, oitModeLocation, 1);
					// The tail shader samples the resolved opaque scene depth on unit 3 to
					// discard fragments occluded by opaque geometry; fixed once per link.
					opaqueDepthLocation = GL.GetUniformLocation(tailShader.Handle, "uOpaqueDepth");
					if (opaqueDepthLocation >= 0)
					{
						GL.ProgramUniform1(tailShader.Handle, opaqueDepthLocation, 3);
					}
					compositeShader = new CompositeProgram();
					depthResolveProgram = new DepthResolveProgram();
					whiteTex = CreateWhiteTexture();
				}
				catch
				{
					supported = false;
					CleanupShaders();
					LogNotSupported("Compiling the hybrid OIT shaders failed.");
				}
			}
		}

		/// <summary>Creates (or recreates on resize) all off-screen targets</summary>
		/// <param name="width">The pixel width of the targets</param>
		/// <param name="height">The pixel height of the targets</param>
		/// <param name="msaaSamples">The number of multisample samples for the scene target; 0 or 1 disables MSAA</param>
		/// <remarks>Must be called after construction and again whenever the screen size changes.
		/// When the requested sample count is not supported the scene target falls back to 1 sample.</remarks>
		public void Setup(int width, int height, int msaaSamples)
		{
			if (!supported)
			{
				return;
			}
			DisposeTargets();
			this.width = width;
			this.height = height;
			samples = msaaSamples > 1 ? msaaSamples : 1;
			try
			{
				int maxSamples;
				GL.GetInteger(GetPName.MaxSamples, out maxSamples);
				if (samples > maxSamples)
				{
					samples = maxSamples > 1 ? maxSamples : 1;
				}
			}
			catch
			{
				samples = 1;
			}
			try
			{
				CreateSceneTargets();
				CreateResolveTargets();
				CreateOpaqueDepthTargets();
				CreatePeelTargets();
				CreateAccumTargets();
			}
			catch
			{
				supported = false;
				DisposeTargets();
				LogNotSupported("Creating the hybrid OIT framebuffer targets failed.");
			}
		}

		/// <summary>Creates the targets using the renderer's configured anti-aliasing level</summary>
		/// <param name="width">The pixel width of the targets</param>
		/// <param name="height">The pixel height of the targets</param>
		public void Setup(int width, int height)
		{
			Setup(width, height, renderer.currentOptions.AntiAliasingLevel);
		}

		/// <summary>Starts an OIT frame: binds the multisampled scene target and clears it</summary>
		/// <remarks>Resets <see cref="PeelsPerformed"/> to zero. Render the background and all opaque
		/// faces (with the renderer's default shader) immediately after this call, then call
		/// <see cref="EndOpaquePass"/> before any transparent rendering.</remarks>
		public void BeginScene()
		{
			PeelsPerformed = 0;
			peeling = false;
			peelQueryActive = false;
			lastPeelPassHadSamples = true;
			if (!supported || sceneFbo == 0)
			{
				return;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			Color32 clear = renderer.currentOptions.ClearColor;
			GL.ClearColor(clear.R * inv255, clear.G * inv255, clear.B * inv255, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
			GL.DepthFunc(DepthFunction.Lequal);
			GL.DepthMask(true);
		}

		/// <summary>Ends the opaque scene pass: resolves the scene colour and copies the scene depth into the opaque depth texture</summary>
		/// <remarks>The opaque depth texture is used by the peel composite and the tail pass to
		/// discard transparent fragments occluded by opaque geometry. The peel frontier is NOT
		/// seeded here: the first peel pass clears it to the far plane so that transparent faces
		/// in front of the opaque scene are captured (front-to-back peeling).</remarks>
		public void EndOpaquePass()
		{
			if (!supported)
			{
				return;
			}
			ResolveOpaqueColor();
			CopyOpaqueDepth();
		}

		/// <summary>Begins a depth-peel pass: binds the peel framebuffer (frontier depth attached) with depth writes enabled and blending disabled</summary>
		/// <param name="firstPeel">True for the first peel layer: the frontier is cleared to the far
		/// plane and the pass renders with glDepthFunc(GL_LESS), capturing the nearest transparent
		/// layer along each ray (front-to-back peeling). False for subsequent layers: the frontier
		/// holds the previous layer's depth and the pass renders with glDepthFunc(GL_GREATER).</param>
		/// <returns>False when the pipeline is unsupported or the required targets are missing (call <see cref="Setup"/> first)</returns>
		/// <remarks>Draw the transparent faces with the renderer's default shader between this call
		/// and <see cref="EndPeelPassAndComposite"/>. Because depth writes are enabled the frontier
		/// depth texture holds this layer's depth when the pass finishes, which is exactly the
		/// correct test for the next peel or tail pass.</remarks>
		public bool BeginPeelPass(bool firstPeel = false)
		{
			if (!supported || peelFbo == 0 || frontierDepthTex == 0)
			{
				return false;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, peelFbo);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit);
			if (firstPeel)
			{
				// Front-to-back: start from the far plane so the nearest transparent fragment
				// along each ray is captured, including fragments behind opaque surfaces
				// (those are discarded at composite time against the opaque scene depth).
				GL.ClearDepth(1.0);
				GL.Clear(ClearBufferMask.DepthBufferBit);
				GL.DepthFunc(DepthFunction.Less);
			}
			else
			{
				GL.DepthFunc(DepthFunction.Greater);
			}
			GL.DepthMask(true);
			GL.Disable(EnableCap.Blend);
			peeling = true;
			return true;
		}

		/// <summary>Starts an occlusion query (GL_ANY_SAMPLES_PASSED) for the current peel pass</summary>
		/// <returns>False when occlusion queries are unavailable; peeling then always runs for the
		/// full requested layer count (see <see cref="PeelsRemaining"/>)</returns>
		public bool BeginPeelQuery()
		{
			if (!supported)
			{
				return false;
			}
			if (peelQuery == 0)
			{
				try
				{
					peelQuery = GL.GenQuery();
				}
				catch
				{
					peelQuery = 0;
					return false;
				}
			}
			try
			{
				GL.BeginQuery(QueryTarget.AnySamplesPassed, peelQuery);
				peelQueryActive = true;
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Whether another depth-peel pass should be performed</summary>
		/// <returns>True when the previous peel pass wrote any fragments. True when no occlusion
		/// query is active (conservative: peel for the full requested layer count). False when the
		/// last peel was empty, or when no peel pass is in progress.</returns>
		public bool PeelsRemaining()
		{
			if (!supported || !peeling)
			{
				return false;
			}
			if (peelQueryActive)
			{
				// The query for the current pass is still running: we cannot know yet
				return true;
			}
			return lastPeelPassHadSamples;
		}

		/// <summary>Ends the current peel pass: reads back the occlusion query (if one was started)
		/// and composites the peeled layer over the scene with standard OVER blending</summary>
		/// <param name="drawNothing">Reserved hook invoked after the scene target is bound for
		/// writing and before the fullscreen quad is drawn; may be null</param>
		/// <remarks>The composite always happens, even when the peel pass drew nothing (the layer
		/// colour is then transparent black and the OVER blend leaves the scene untouched).
		/// Pixels where the peeled layer lies behind the resolved opaque scene depth are
		/// discarded (composite program mode 2), so transparent fragments occluded by opaque
		/// geometry never reach the scene. After this call the frontier depth texture holds
		/// this layer's depth, which is exactly the correct test for the next peel or tail pass.</remarks>
		public void EndPeelPassAndComposite(Action drawNothing = null)
		{
			if (!supported)
			{
				return;
			}
			if (peelQueryActive)
			{
				try
				{
					GL.EndQuery(QueryTarget.AnySamplesPassed);
					int result = 0;
					GL.GetQueryObject(peelQuery, GetQueryObjectParam.QueryResult, out result);
					lastPeelPassHadSamples = result != 0;
				}
				catch
				{
					lastPeelPassHadSamples = true;
				}
				peelQueryActive = false;
			}
			if (peelFbo == 0 || sceneFbo == 0 || compositeShader == null)
			{
				return;
			}
			PeelsPerformed++;
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.Disable(EnableCap.DepthTest);
			GL.Enable(EnableCap.Blend);
			GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			compositeShader.Use();
			compositeShader.SetMode(2);
			compositeShader.SetOpaque(peelColorTex);
			compositeShader.SetAccum(whiteTex);
			compositeShader.SetReveal(whiteTex);
			compositeShader.SetLayerDepth(frontierDepthTex);
			compositeShader.SetOpaqueDepth(opaqueDepthTex);
			if (drawNothing != null)
			{
				drawNothing();
			}
			renderer.dummyVao.Bind();
			GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 6);
			compositeShader.Unuse();
			GL.ActiveTexture(TextureUnit.Texture0);
			renderer.LastBoundTexture = null;
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Greater);
			GL.DepthMask(true);
			renderer.RestoreBlendFunc();
		}

		/// <summary>Begins the weighted blended OIT tail pass</summary>
		/// <returns>False when the pipeline is unsupported or the required targets are missing</returns>
		/// <remarks>Binds the dual-drawbuffer accumulation framebuffer (accum = location 0,
		/// reveal = location 1, frontier depth attached), enables per-draw-buffer blending
		/// (buffer 0: ONE/ONE, buffer 1: ZERO/ONE_MINUS_SRC_ALPHA), sets glDepthFunc(GL_GREATER)
		/// with depth writes disabled and activates the OIT variant of the default shader.
		/// The per-frame shading state (shadow maps, lighting and fog) is mirrored from the
		/// renderer and the resolved opaque scene depth is bound for the occlusion discard, so
		/// the tail shades identically to the peeled layers and fragments occluded by opaque
		/// geometry never reach the accumulation buffers.
		/// Draw the remaining transparent faces between this call and <see cref="EndTailPass"/>
		/// (plain <see cref="FaceState.Draw()"/> is sufficient, the OIT shader is already active).
		/// Additive faces MUST NOT be drawn here: their blend override would clobber the
		/// per-draw-buffer factors (glBlendFunc sets all buffers); render them separately.</remarks>
		public bool BeginTailPass()
		{
			if (!supported || accumFbo == 0 || frontierDepthTex == 0 || tailShader == null)
			{
				return false;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, accumFbo);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment1);
			GL.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit);
			GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 });
			GL.DepthFunc(DepthFunction.Greater);
			GL.DepthMask(false);
			GL.Enable(EnableCap.Blend);
			try
			{
				// Per-draw-buffer blend factors (glBlendFunci, GL 4.0)
				GL.BlendFunc(0, BlendingFactorSrc.One, BlendingFactorDest.One);
				GL.BlendFunc(1, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcAlpha);
			}
			catch (EntryPointNotFoundException)
			{
				supported = false;
				LogNotSupported("Per-draw-buffer blending (glBlendFunci) is not available.");
				renderer.RestoreBlendFunc();
				return false;
			}
			tailShader.Activate();
			GL.ProgramUniform1(tailShader.Handle, oitModeLocation, 1);
			// Reset the OIT program's uniforms and flush the renderer's per-face state cache
			// (lastColor / lastObjectState), so the first face drawn with this second instance
			// of the default program always receives its material and matrix uniforms.
			renderer.ResetShader(tailShader);
			renderer.lastObjectState = null;
			renderer.RestoreAlphaFunc();
			tailShader.SetCurrentProjectionMatrix(renderer.CurrentProjectionMatrix);
			// Mirror the per-frame shading state from the regular default program so the tail
			// is shaded identically to the opaque scene and the peeled layers.
			renderer.Shadows.Bind(tailShader);
			tailShader.SetIsLight(renderer.OptionLighting);
			if (renderer.OptionLighting)
			{
				tailShader.SetLightPosition(renderer.TransformedLightPosition);
				tailShader.SetLightAmbient(renderer.Lighting.OptionAmbientColor);
				tailShader.SetLightDiffuse(renderer.Lighting.OptionDiffuseColor);
				tailShader.SetLightSpecular(renderer.Lighting.OptionSpecularColor);
				tailShader.SetLightModel(renderer.Lighting.LightModel);
			}
			if (renderer.Fog.Enabled)
			{
				tailShader.SetFog(true);
				tailShader.SetFog(renderer.Fog);
			}
			// Bind the resolved opaque scene depth (unit 3) for the tail's occlusion discard
			GL.ActiveTexture(TextureUnit.Texture3);
			GL.BindTexture(TextureTarget.Texture2D, opaqueDepthTex);
			GL.ActiveTexture(TextureUnit.Texture0);
			return true;
		}

		/// <summary>Ends the tail pass: deactivates the OIT shader, restores the depth function,
		/// depth mask and the renderer's tracked blend state</summary>
		public void EndTailPass()
		{
			if (tailShader != null)
			{
				tailShader.Deactivate();
			}
			GL.DepthFunc(DepthFunction.Lequal);
			GL.DepthMask(true);
			renderer.RestoreBlendFunc();
			peeling = false;
		}

		/// <summary>Composites the final frame onto the default framebuffer</summary>
		/// <remarks>Re-resolves the scene colour (so the peel layers composited into the scene
		/// target are included), then draws a fullscreen quad blending the resolved opaque image
		/// with the weighted-blended tail. Ends with depth testing enabled, the depth function
		/// restored to GL_LEQUAL and the renderer's default shader (re)activated, so normal
		/// rendering can continue directly afterwards.</remarks>
		public void Composite()
		{
			if (!supported)
			{
				return;
			}
			if (compositeShader == null || accumTex == 0 || revealTex == 0 || resolveColorTex == 0)
			{
				return;
			}
			ResolveOpaqueColor();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			GL.DrawBuffer(DrawBufferMode.Back);
			GL.Disable(EnableCap.DepthTest);
			GL.Enable(EnableCap.Blend);
			GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			compositeShader.Use();
			compositeShader.SetMode(0);
			compositeShader.SetOpaque(resolveColorTex);
			compositeShader.SetAccum(accumTex);
			compositeShader.SetReveal(revealTex);
			renderer.dummyVao.Bind();
			GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 6);
			compositeShader.Unuse();
			GL.ActiveTexture(TextureUnit.Texture0);
			renderer.LastBoundTexture = null;
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);
			GL.DepthMask(true);
			renderer.RestoreBlendFunc();
			renderer.DefaultShader.Activate();
		}

		/// <summary>Releases all OpenGL resources held by this instance</summary>
		public void Dispose()
		{
			DisposeTargets();
			CleanupShaders();
			if (peelQuery != 0)
			{
				try
				{
					GL.DeleteQuery(peelQuery);
				}
				catch
				{
					// ignore
				}
				peelQuery = 0;
			}
		}

		#region Support detection

		private bool QuerySupport()
		{
			try
			{
				// Per-draw-buffer blending (glBlendFunci) is a GL 4.0 feature and is required by the tail pass
				string versionString = GL.GetString(StringName.Version);
				if (string.IsNullOrEmpty(versionString))
				{
					return false;
				}
				string[] parts = versionString.Split('.');
				int major;
				if (!int.TryParse(parts[0].Trim(), out major))
				{
					return false;
				}
				int minor = 0;
				if (parts.Length > 1)
				{
					int.TryParse(parts[1].Trim(), out minor);
				}
				return major > 4 || (major == 4);
			}
			catch
			{
				return false;
			}
		}

		private void LogNotSupported(string reason)
		{
			if (supportLogged)
			{
				return;
			}
			supportLogged = true;
			try
			{
				renderer.currentHost.AddMessage(MessageType.Error, false, reason + " Order-independent transparency is disabled.");
			}
			catch
			{
				// ignore
			}
		}

		#endregion

		#region Target creation

		private static int CreateTexture2D(PixelInternalFormat internalFormat, PixelFormat format, PixelType type, int width, int height)
		{
			int texture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, texture);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, type, IntPtr.Zero);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			return texture;
		}

		private static int CreateDepthTexture(PixelInternalFormat internalFormat, int width, int height)
		{
			int texture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, texture);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			return texture;
		}

		private static int CreateWhiteTexture()
		{
			int texture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, texture);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 255, 255, 255 });
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			return texture;
		}

		private static bool CheckFramebufferStatus()
		{
			return GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
		}

		private void CreateSceneTargets()
		{
			sceneColorTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2DMultisample, sceneColorTex);
			GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, samples, PixelInternalFormat.Rgba8, width, height, true);
			GL.BindTexture(TextureTarget.Texture2DMultisample, 0);

			sceneDepthTex = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2DMultisample, sceneDepthTex);
			GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, samples, PixelInternalFormat.DepthComponent24, width, height, true);
			GL.BindTexture(TextureTarget.Texture2DMultisample, 0);

			sceneFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2DMultisample, sceneColorTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2DMultisample, sceneDepthTex, 0);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Scene framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		private void CreateResolveTargets()
		{
			resolveColorTex = CreateTexture2D(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, width, height);
			resolveFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, resolveColorTex, 0);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Resolve framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

			// The frontier has no colour attachment, so its draw/read buffers must be GL_NONE
			// for the framebuffer to be complete and for depth blits to be legal.
			frontierDepthTex = CreateDepthTexture(PixelInternalFormat.DepthComponent32f, width, height);
			frontierFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, frontierFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, frontierDepthTex, 0);
			GL.DrawBuffer(DrawBufferMode.None);
			GL.ReadBuffer(ReadBufferMode.None);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Frontier framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		private void CreateOpaqueDepthTargets()
		{
			opaqueDepthTex = CreateDepthTexture(PixelInternalFormat.DepthComponent32f, width, height);
			opaqueDepthFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueDepthFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, opaqueDepthTex, 0);
			GL.DrawBuffer(DrawBufferMode.None);
			GL.ReadBuffer(ReadBufferMode.None);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Opaque depth framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		private void CreatePeelTargets()
		{
			peelColorTex = CreateTexture2D(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, width, height);
			peelFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, peelFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, peelColorTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, frontierDepthTex, 0);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Peel framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		private void CreateAccumTargets()
		{
			accumTex = CreateTexture2D(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat, width, height);
			revealTex = CreateTexture2D(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat, width, height);
			accumFbo = GL.GenFramebuffer();
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, accumFbo);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, accumTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, revealTex, 0);
			GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, frontierDepthTex, 0);
			if (!CheckFramebufferStatus())
			{
				throw new InvalidOperationException("Accumulation framebuffer is incomplete");
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		#endregion

		#region Blits

		private void ResolveOpaqueColor()
		{
			if (sceneFbo == 0 || resolveFbo == 0)
			{
				return;
			}
			GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sceneFbo);
			GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, resolveFbo);
			GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
			GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		/// <summary>Resolves the multisampled scene depth into the single-sample opaque depth
		/// texture. glBlitFramebuffer cannot copy depth between framebuffers with different
		/// multisample counts (GL_INVALID_OPERATION), so the depth is resolved with a fullscreen
		/// pass writing gl_FragDepth from the nearest multisample (see depthresolve.frag).</summary>
		private void CopyOpaqueDepth()
		{
			if (sceneFbo == 0 || opaqueDepthFbo == 0 || depthResolveProgram == null || renderer.dummyVao == null)
			{
				return;
			}
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, opaqueDepthFbo);
			GL.DrawBuffer(DrawBufferMode.None);
			GL.ReadBuffer(ReadBufferMode.None);
			GL.Disable(EnableCap.DepthTest);
			GL.Disable(EnableCap.CullFace);
			GL.Disable(EnableCap.Blend);
			GL.DepthMask(true);
			depthResolveProgram.Use();
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2DMultisample, sceneDepthTex);
			depthResolveProgram.SetSampleCount(samples);
			renderer.dummyVao.Bind();
			GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 6);
			depthResolveProgram.Unuse();
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2DMultisample, 0);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);
			GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}

		#endregion

		#region Cleanup

		private void DisposeTargets()
		{
			DeleteFramebuffer(ref sceneFbo);
			DeleteTexture(ref sceneColorTex);
			DeleteTexture(ref sceneDepthTex);
			DeleteFramebuffer(ref resolveFbo);
			DeleteTexture(ref resolveColorTex);
			DeleteFramebuffer(ref frontierFbo);
			DeleteTexture(ref frontierDepthTex);
			DeleteFramebuffer(ref opaqueDepthFbo);
			DeleteTexture(ref opaqueDepthTex);
			DeleteFramebuffer(ref peelFbo);
			DeleteTexture(ref peelColorTex);
			DeleteFramebuffer(ref accumFbo);
			DeleteTexture(ref accumTex);
			DeleteTexture(ref revealTex);
		}

		private void CleanupShaders()
		{
			DeleteTexture(ref whiteTex);
			if (tailShader != null)
			{
				tailShader.Dispose();
				tailShader = null;
			}
			if (compositeShader != null)
			{
				compositeShader.Dispose();
				compositeShader = null;
			}
			if (depthResolveProgram != null)
			{
				depthResolveProgram.Dispose();
				depthResolveProgram = null;
			}
		}

		private static void DeleteTexture(ref int texture)
		{
			if (texture != 0)
			{
				GL.DeleteTexture(texture);
				texture = 0;
			}
		}

		private static void DeleteFramebuffer(ref int framebuffer)
		{
			if (framebuffer != 0)
			{
				GL.DeleteFramebuffer(framebuffer);
				framebuffer = 0;
			}
		}

		#endregion

		/// <summary>Compiles a shader stage, throwing a descriptive exception on failure.
		/// Shared by the composite and depth resolve programs</summary>
		private static int CompileShader(ShaderType type, string source, string name)
		{
			int shader = GL.CreateShader(type);
			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);
			int status;
			GL.GetShader(shader, ShaderParameter.CompileStatus, out status);
			if (status == 0)
			{
				string log = GL.GetShaderInfoLog(shader);
				GL.DeleteShader(shader);
				throw new InvalidOperationException("Compiling the " + name + " shader failed: " + log);
			}
			return shader;
		}

		/// <summary>Small dedicated program for the fullscreen composite passes</summary>
		private sealed class CompositeProgram : IDisposable
		{
			private readonly int handle;
			private readonly int uModeLocation;
			private bool disposed;

			internal CompositeProgram()
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				string vertexSource;
				string fragmentSource;
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2.composite.vert"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException("The embedded shader resource LibRender2.composite.vert is missing");
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						vertexSource = reader.ReadToEnd();
					}
				}
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2.composite.frag"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException("The embedded shader resource LibRender2.composite.frag is missing");
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						fragmentSource = reader.ReadToEnd();
					}
				}
				int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource, "composite");
				int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource, "composite");
				handle = GL.CreateProgram();
				GL.AttachShader(handle, vertexShader);
				GL.AttachShader(handle, fragmentShader);
				GL.DeleteShader(vertexShader);
				GL.DeleteShader(fragmentShader);
				GL.BindFragDataLocation(handle, 0, "fragColor");
				GL.LinkProgram(handle);
				int status;
				GL.GetProgram(handle, GetProgramParameterName.LinkStatus, out status);
				if (status == 0)
				{
					throw new InvalidOperationException("Linking the composite shader failed: " + GL.GetProgramInfoLog(handle));
				}
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uOpaque"), 0);
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uAccum"), 1);
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uReveal"), 2);
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uLayerDepth"), 3);
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uOpaqueDepth"), 4);
				uModeLocation = GL.GetUniformLocation(handle, "uMode");
				GL.ProgramUniform1(handle, uModeLocation, 0);
			}

			internal void Use()
			{
				GL.UseProgram(handle);
			}

			internal void Unuse()
			{
				GL.UseProgram(0);
			}

			internal void SetMode(int mode)
			{
				GL.ProgramUniform1(handle, uModeLocation, mode);
			}

			internal void SetOpaque(int texture)
			{
				GL.ActiveTexture(TextureUnit.Texture0);
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}

			internal void SetAccum(int texture)
			{
				GL.ActiveTexture(TextureUnit.Texture1);
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}

			internal void SetReveal(int texture)
			{
				GL.ActiveTexture(TextureUnit.Texture2);
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}

			internal void SetLayerDepth(int texture)
			{
				GL.ActiveTexture(TextureUnit.Texture3);
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}

			internal void SetOpaqueDepth(int texture)
			{
				GL.ActiveTexture(TextureUnit.Texture4);
				GL.BindTexture(TextureTarget.Texture2D, texture);
			}

			public void Dispose()
			{
				if (!disposed)
				{
					GL.DeleteProgram(handle);
					GC.SuppressFinalize(this);
					disposed = true;
				}
			}
		}

		/// <summary>Dedicated program for resolving the multisampled scene depth into the
		/// single-sample opaque depth texture (see depthresolve.frag)</summary>
		private sealed class DepthResolveProgram : IDisposable
		{
			private readonly int handle;
			private readonly int uSampleCountLocation;
			private bool disposed;

			internal DepthResolveProgram()
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				string vertexSource;
				string fragmentSource;
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2.depthresolve.vert"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException("The embedded shader resource LibRender2.depthresolve.vert is missing");
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						vertexSource = reader.ReadToEnd();
					}
				}
				using (Stream stream = assembly.GetManifestResourceStream("LibRender2.depthresolve.frag"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException("The embedded shader resource LibRender2.depthresolve.frag is missing");
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						fragmentSource = reader.ReadToEnd();
					}
				}
				int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource, "depth resolve");
				int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource, "depth resolve");
				handle = GL.CreateProgram();
				GL.AttachShader(handle, vertexShader);
				GL.AttachShader(handle, fragmentShader);
				GL.DeleteShader(vertexShader);
				GL.DeleteShader(fragmentShader);
				GL.LinkProgram(handle);
				int status;
				GL.GetProgram(handle, GetProgramParameterName.LinkStatus, out status);
				if (status == 0)
				{
					throw new InvalidOperationException("Linking the depth resolve shader failed: " + GL.GetProgramInfoLog(handle));
				}
				GL.ProgramUniform1(handle, GL.GetUniformLocation(handle, "uSceneDepth"), 0);
				uSampleCountLocation = GL.GetUniformLocation(handle, "uSampleCount");
				GL.ProgramUniform1(handle, uSampleCountLocation, 1);
			}

			internal void Use()
			{
				GL.UseProgram(handle);
			}

			internal void Unuse()
			{
				GL.UseProgram(0);
			}

			internal void SetSampleCount(int count)
			{
				if (uSampleCountLocation >= 0)
				{
					GL.ProgramUniform1(handle, uSampleCountLocation, count);
				}
			}

			public void Dispose()
			{
				if (!disposed)
				{
					GL.DeleteProgram(handle);
					GC.SuppressFinalize(this);
					disposed = true;
				}
			}
		}
	}
}
