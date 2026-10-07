using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using LibRender2.Abstractions;
using LibRender2.Backgrounds;
using LibRender2.Cameras;
using LibRender2.Fogs;
using LibRender2.Lightings;
using LibRender2.Loadings;
using LibRender2.MotionBlurs;
using LibRender2.Objects;
using LibRender2.Overlays;
using LibRender2.Primitives;
using LibRender2.Rendering;
using LibRender2.Scene;
using LibRender2.Screens;
using LibRender2.Shaders;
using LibRender2.ShadowMapping;
using LibRender2.State;
using LibRender2.Text;
using LibRender2.Textures;
using LibRender2.Viewports;
using OpenBveApi;
using OpenBveApi.Colors;
using OpenBveApi.FileSystem;
using OpenBveApi.Hosts;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Textures;
using OpenBveApi.World;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using Path = OpenBveApi.Path;
using PixelFormat = OpenBveApi.Textures.PixelFormat;
using Vector2 = OpenBveApi.Math.Vector2;
using Vector3 = OpenBveApi.Math.Vector3;

namespace LibRender2
{
	public abstract class BaseRenderer : IRendererContext, IRenderState, ISceneProvider, IFaceRendererHost
	{
		// constants
		protected const float inv255 = 1.0f / 255.0f;

		/// <summary>Modular subsystems extracted from the former god-class.</summary>
		private readonly RenderStateManager renderState;
		private readonly VisibilityManager visibilityManager;
		private readonly FaceRenderer faceRenderer;

		/// <summary>Holds the lock for GDI Plus functions</summary>
		public static readonly object GdiPlusLock = new object();

		/// <summary>The callback to the host application</summary>
		internal HostInterface currentHost;
		/// <summary>The host filesystem</summary>
		internal FileSystem fileSystem;

		/// <summary>Holds a reference to the current options</summary>
		internal BaseOptions currentOptions;

		/// <summary>Exposes host plumbing via <see cref="IRendererContext"/> without breaking direct field access.</summary>
		public HostInterface Host => currentHost;
		public BaseOptions Options => currentOptions;
		public FileSystem FileSystem => fileSystem;
		public Matrix4D GetCurrentViewMatrix() => CurrentViewMatrix;

		public List<ObjectState> StaticObjectStates { get; set; }
		public List<ObjectState> DynamicObjectStates { get; set; }
		public VisibleObjectLibrary VisibleObjects { get; set; }

		protected int[] ObjectsSortedByStart
		{
			get => visibilityManager.ObjectsSortedByStart;
			set => visibilityManager.ObjectsSortedByStart = value;
		}
		protected int[] ObjectsSortedByEnd
		{
			get => visibilityManager.ObjectsSortedByEnd;
			set => visibilityManager.ObjectsSortedByEnd = value;
		}
		protected int ObjectsSortedByStartPointer
		{
			get => visibilityManager.ObjectsSortedByStartPointer;
			set => visibilityManager.ObjectsSortedByStartPointer = value;
		}
		protected int ObjectsSortedByEndPointer
		{
			get => visibilityManager.ObjectsSortedByEndPointer;
			set => visibilityManager.ObjectsSortedByEndPointer = value;
		}
		protected internal double LastUpdatedTrackPosition
		{
			get => visibilityManager.LastUpdatedTrackPosition;
			set => visibilityManager.LastUpdatedTrackPosition = value;
		}

		double Abstractions.ISceneProvider.LastUpdatedTrackPosition
		{
			get => visibilityManager.LastUpdatedTrackPosition;
			set => visibilityManager.LastUpdatedTrackPosition = value;
		}
		/// <summary>Whether ReShade is in use</summary>
		/// <remarks>Don't use OpenGL error checking with ReShade, as this breaks</remarks>
		public bool ReShadeInUse;
		/// <summary>A dummy VAO used when working with procedural data within the shader</summary>
		public VertexArrayObject dummyVao;

		public Screen Screen { get; set; }

		/// <summary>The track follower for the main camera</summary>
		public TrackFollower CameraTrackFollower { get; set; }

		public bool RenderThreadJobWaiting;

		/// <summary>Holds a reference to the current interface type of the game (Used by the renderer)</summary>
		public InterfaceType CurrentInterface
		{
			get => currentInterface;
			set
			{
				previousInterface = currentInterface;
				currentInterface = value;
			}
		}

		/// <summary>Gets the scale factor for the current display</summary>
		public Vector2 ScaleFactor
		{
			get
			{
				if (currentHost.Application == HostApplication.TrainEditor || currentHost.Application == HostApplication.TrainEditor2)
				{
					// accessing display device under SDL2 GLControl fails, scale not supported here anyways
					return Vector2.One;
				}
				if (_scaleFactor.X > 0)
				{
					return _scaleFactor;
				}
				// guard against the fact that some systems return a scale factor of zero
				_scaleFactor = new Vector2(Math.Max(DisplayDevice.Default.ScaleFactor.X, 1), Math.Max(DisplayDevice.Default.ScaleFactor.Y, 1));
				return _scaleFactor;
			}
		}

		private static Vector2 _scaleFactor = new Vector2(-1, -1);

		/// <summary>Holds a reference to the previous interface type of the game</summary>
		public InterfaceType PreviousInterface => previousInterface;

		//Backing properties for the interface values
		private InterfaceType currentInterface = InterfaceType.Normal;
		private InterfaceType previousInterface = InterfaceType.Normal;

		public CameraProperties Camera { get; set; }
		public Lighting Lighting { get; set; }
		public Background Background { get; set; }
		public Fog Fog { get; set; }
		public Marker Marker { get; set; }
		public OpenGlString OpenGlString { get; set; }
		public TextureManager TextureManager { get; set; }
		public Cube Cube { get; set; }
		public Rectangle Rectangle { get; set; }
		public Particle Particle { get; set; }
		public Loading Loading { get; set; }
		public Keys Keys { get; set; }
		public MotionBlur MotionBlur { get; set; }
		public Fonts Fonts { get; set; }

		public Matrix4D CurrentProjectionMatrix;
		public Matrix4D CurrentViewMatrix;

		public Vector3 TransformedLightPosition;

		protected List<Matrix4D> projectionMatrixList;
		protected List<Matrix4D> viewMatrixList;

		public List<int> usedTrackColors = new List<int>();
		public Dictionary<int, RailPath> trackColors = new Dictionary<int, RailPath>();
#if RELEASE
#pragma warning disable 0219, CS0169
#endif
		/// <summary>Holds the last openGL error</summary>
		/// <remarks>Is only used in debug builds, hence the pragma</remarks>
		private ErrorCode lastError;
#if RELEASE
#pragma warning restore 0219, CS0169
#endif

		/// <summary>The current shader in use</summary>
		public AbstractShader CurrentShader { get; set; }

		public Shader DefaultShader { get; set; }
		
		/// <summary>Manages the Cascaded Shadow Mapping (CSM) system.</summary>
		public Shadows Shadows { get; set; }

		/// <summary>Whether shadows are enabled.</summary>
		public bool ShadowsEnabled => Shadows?.Enabled ?? false;

		/// <summary>Shadow strength: 0=invisible, 1=full darkness.</summary>
		public float ShadowStrength => Shadows?.Strength ?? 0.7f;

		/// <summary>Whether lighting is enabled in the debug options</summary>
		public bool OptionLighting { get; set; } = true;

		/// <summary>Whether normals rendering is enabled in the debug options</summary>
		private bool optionNormals = false;

		/// <summary>Whether normals rendering is enabled in the debug options</summary>
		/// <remarks>Disposing the normals VAO when disabled frees the duplicate vertex buffer held in RAM.</remarks>
		public bool OptionNormals
		{
			get => optionNormals;
			set
			{
				if (value == optionNormals)
				{
					return;
				}

				optionNormals = value;
				if (!value)
				{
					DisposeNormalsVAOs();
					// Force the GC so the freed normals buffers are gone from RAM immediately.
					GC.Collect();
					GC.WaitForPendingFinalizers();
				}
			}
		}

		private void DisposeNormalsVAOs()
		{
			if (StaticObjectStates != null)
			{
				foreach (var state in StaticObjectStates)
				{
					DisposeNormalsVAO(state);
				}
			}

			if (DynamicObjectStates != null)
			{
				foreach (var state in DynamicObjectStates)
				{
					DisposeNormalsVAO(state);
				}
			}
		}

		private static void DisposeNormalsVAO(ObjectState state)
		{
			if (state?.Prototype?.Mesh?.NormalsVAO is VertexArrayObject normalsVao)
			{
				normalsVao.UnBind();
				normalsVao.Dispose();
				state.Prototype.Mesh.NormalsVAO = null;
			}
		}

		/// <summary>Whether back face culling is enabled</summary>
		public bool OptionBackFaceCulling { get; set; } = true;

		/// <summary>Whether WireFrame rendering is enabled in the debug options</summary>
		public bool OptionWireFrame { get; set; } = false;

		/// <summary>The current viewport mode</summary>
		protected ViewportMode CurrentViewportMode = ViewportMode.Scenery;

		/// <summary>The current debug output mode</summary>
		public OutputMode CurrentOutputMode = OutputMode.Default;

		/// <summary>The previous debug output mode</summary>
		public OutputMode PreviousOutputMode = OutputMode.Default;

		/// <summary>The currently displayed timetable</summary>
		public DisplayedTimetable CurrentTimetable = DisplayedTimetable.None;

		/// <summary>The total number of OpenGL triangles in the current frame</summary>
		public int InfoTotalTriangles;

		/// <summary>The total number of OpenGL triangle strips in the current frame</summary>
		public int InfoTotalTriangleStrip;

		/// <summary>The total number of OpenGL quad strips in the current frame</summary>
		public int InfoTotalQuadStrip;

		/// <summary>The total number of OpenGL quads in the current frame</summary>
		public int InfoTotalQuads;

		/// <summary>The total number of OpenGL polygons in the current frame</summary>
		public int InfoTotalPolygon;

		/// <summary>The game's current framerate</summary>
		public double FrameRate = 1.0;

		/// <summary>Render state is owned by <see cref="RenderStateManager"/>; these members proxy to it for compatibility.</summary>
		public bool BlendEnabled => renderState.BlendEnabled;
		public bool AlphaTestEnabled => renderState.AlphaTestEnabled;

		/// <summary>Stores the most recently bound texture</summary>
		public OpenGlTexture LastBoundTexture
		{
			get => renderState.LastBoundTexture;
			set => renderState.LastBoundTexture = value;
		}

		internal Color32 lastColor
		{
			get => renderState.LastColor;
			set => renderState.LastColor = value;
		}

		public Color32 LastColor
		{
			get => renderState.LastColor;
			set => renderState.LastColor = value;
		}

		// Remembers if culling is on, so we don't call GL for every face
		private bool cullFaceEnabled
		{
			get => renderState.CullFaceEnabled;
			set => renderState.CullFaceEnabled = value;
		}

		public bool CullFaceEnabled
		{
			get => renderState.CullFaceEnabled;
			set => renderState.CullFaceEnabled = value;
		}

		/// <summary>Holds the handle of the last VAO bound by openGL</summary>
		public int lastVAO
		{
			get => renderState.LastVAO;
			set => renderState.LastVAO = value;
		}

		public int LastVAO
		{
			get => renderState.LastVAO;
			set => renderState.LastVAO = value;
		}

		protected internal Texture _programLogo;

		protected internal Texture whitePixel;
		/// <summary>A dummy 1x1 depth texture with comparison enabled, used when shadows are disabled to satisfy driver requirements.</summary>
		internal int nullDepthMap; 

		private bool logoError;

		/// <summary>Gets the current program logo</summary>
		public Texture ProgramLogo
		{
			get
			{
				if (_programLogo != null || logoError)
				{
					return _programLogo;
				}
				try
				{
					if (Screen.Width > 1024)
					{
						currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("In-game"), "logo_1024.png"), TextureParameters.NoChange, out _programLogo, true);
					}
					else if (Screen.Width > 512)
					{
						currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("In-game"), "logo_512.png"), TextureParameters.NoChange, out _programLogo, true);
					}
					else
					{
						currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("In-game"), "logo_256.png"), TextureParameters.NoChange, out _programLogo, true);
					}
				}
				catch
				{
					_programLogo = null;
					logoError = true;
				}
				return _programLogo;
			}
		}

		/// <summary>A joystick icon</summary>
		public Texture JoystickTexture;
		/// <summary>A keyboard icon</summary>
		public Texture KeyboardTexture;
		/// <summary>A generic gamepad icon</summary>
		public Texture GamepadTexture;
		/// <summary>An XInput gamepad icon</summary>
		public Texture XInputTexture;
		/// <summary>A Mascon 2-Handle controller icon</summary>
		public Texture MasconTexture;
		/// <summary>A raildriver icon</summary>
		public Texture RailDriverTexture;
		/// <summary>The game window</summary>
		public GameWindow GameWindow;
		/// <summary>The graphics mode in use</summary>
		public GraphicsMode GraphicsMode;
		public bool LoadLogo()
		{
			return currentHost.LoadTexture(ref _programLogo, OpenGlTextureWrapMode.ClampClamp);
		}

		/*
		 * List of VBO and IBO to delete on the next frame pass
		 * This needs to be done here as opposed to in the finalizer
		 */
		internal static readonly List<int> vaoToDelete = new List<int>();
		internal static readonly List<int> vboToDelete = new List<int>();
		internal static readonly List<int> iboToDelete = new List<int>();

		public Dictionary<Texture, HashSet<Vector3>> CubesToDraw = new Dictionary<Texture, HashSet<Vector3>>();
		
		protected BaseRenderer(HostInterface CurrentHost, BaseOptions CurrentOptions, FileSystem FileSystem)
		{
			currentHost = CurrentHost;
			currentOptions = CurrentOptions;
			fileSystem = FileSystem;
			renderState = new RenderStateManager(() => CurrentShader);
			visibilityManager = new VisibilityManager(() => currentOptions);
			faceRenderer = new FaceRenderer(this);
			Screen = new Screen(this);
			Camera = new CameraProperties(this);
			Lighting = new Lighting(this);
			Marker = new Marker(this);
			Shadows = new Shadows(this);

			projectionMatrixList = new List<Matrix4D>();
			viewMatrixList = new List<Matrix4D>();
			Fonts = new Fonts(currentHost, this, CurrentOptions.Font);
			VisibilityThread = new Thread(RunVisibiliityThread);
			VisibilityThread.Start();
			RenderThreadJobs = new ConcurrentQueue<ThreadStart>();
		}


		/// <summary>Call this once to initialise the renderer</summary>
		/// <remarks>A call to DeInitialize should be made when closing the program to release resources</remarks>
		[HandleProcessCorruptedStateExceptions] //As some graphics cards crash really nastily if we request unsupported features
		public virtual void Initialize()
		{
			try
			{
				if (DefaultShader == null)
				{
					DefaultShader = new Shader(this, "default", "default", true);
				}
				DefaultShader.Activate();
				DefaultShader.SetMaterialAmbient(Color32.White);
				DefaultShader.SetMaterialDiffuse(Color32.White);
				DefaultShader.SetMaterialSpecular(Color32.White);
				lastColor = Color32.White;
				DefaultShader.Deactivate();
				dummyVao = new VertexArrayObject();
			}
			catch
			{
				currentHost.AddMessage(MessageType.Error, false, "Initializing the default shaders failed.");
				GL.GetError();
				try
				{
					/*
					 * Nasty little edge case with some Intel graphics- They create the shader OK
					 * but it crashes on use, but remains active
					 * Deactivate it, otherwise we get a grey screen
					 */
					DefaultShader?.Deactivate();
				}
				catch
				{
					// ignored
					GL.GetError();
				}

			}

			if (DefaultShader == null)
			{
				// Shader failed to load, but no exception
				currentHost.AddMessage(MessageType.Error, false, "Initializing the default shaders failed.");
			}

            Background = new Background(this);
			Fog = new Fog(this);
			OpenGlString = new OpenGlString(this); //text shader shares the rectangle fragment shader
			TextureManager = new TextureManager(currentHost, this);
			Cube = new Cube(this);
			Rectangle = new Rectangle(this);
			Particle = new Particle(this);
			Loading = new Loading(this);
			Keys = new Keys(this);
			MotionBlur = new MotionBlur(this);

			StaticObjectStates = new List<ObjectState>();
			DynamicObjectStates = new List<ObjectState>();
			VisibleObjects = new VisibleObjectLibrary(this);
			whitePixel = new Texture(new Texture(1, 1, PixelFormat.RGBAlpha, new byte[] {255, 255, 255, 255}, (OpenBveApi.Colors.Color24[])null));
			nullDepthMap = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, nullDepthMap);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent16, 1, 1, 0, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.UnsignedShort, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
			GL.BindTexture(TextureTarget.Texture2D, 0);
			GL.ClearColor(currentOptions.ClearColor.R * inv255, currentOptions.ClearColor.G * inv255, currentOptions.ClearColor.B * inv255, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);
			SetBlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			if (currentOptions.ForceForwardsCompatibleContext == false)
			{
				// not valid with a forwards compatible context, so don't generate spurious error
				GL.Hint(HintTarget.FogHint, HintMode.Fastest);
				GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Fastest);
				GL.Hint(HintTarget.GenerateMipmapHint, HintMode.Nicest);
				GL.Disable(EnableCap.Lighting);
				GL.Disable(EnableCap.Fog);
				GL.Hint(HintTarget.LineSmoothHint, HintMode.Fastest);
				GL.Hint(HintTarget.PointSmoothHint, HintMode.Fastest);
				GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Fastest);
			}

			GL.Enable(EnableCap.CullFace);
			GL.CullFace(CullFaceMode.Front);
			GL.Disable(EnableCap.Dither);
			
			// ReSharper disable once PossibleNullReferenceException
			string openGLdll = Path.CombineFile(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "opengl32.dll");

			if (File.Exists(openGLdll))
			{
				FileVersionInfo glVersionInfo = FileVersionInfo.GetVersionInfo(openGLdll);
				if (glVersionInfo.ProductName == @"ReShade")
				{
					ReShadeInUse = true;
				}
			}
			// icons for use in GL menus
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "keyboard.png"), TextureParameters.NoChange, out KeyboardTexture);
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "gamepad.png"), TextureParameters.NoChange, out GamepadTexture);
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "xbox.png"), TextureParameters.NoChange, out XInputTexture);
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "zuki.png"), TextureParameters.NoChange, out MasconTexture);
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "joystick.png"), TextureParameters.NoChange, out JoystickTexture);
			currentHost.RegisterTexture(Path.CombineFile(fileSystem.GetDataFolder("Menu"), "raildriver.png"), TextureParameters.NoChange, out RailDriverTexture);

			Lighting.Initialize();
			Shadows.Initialize();
        }

		/// <summary>Initializes (or reinitializes) shadow mapping from current options.</summary>
		public void InitializeShadows() => Shadows.Initialize();

		/// <summary>Disposes all shadow GPU resources.</summary>
		public void DisposeShadows() => Shadows.Dispose();

		/// <summary>
		/// Call this when the user changes shadow settings at runtime
		/// (e.g. from an in-game options menu). This will recreate
		/// GPU resources to match the new settings.
		/// </summary>
		public void ReloadShadowSettings()
		{
			fileSystem.AppendToLogFile("[CSM] Reloading shadow settings from options...");
			InitializeShadows();
		}

		/// <summary>Deinitializes the renderer</summary>
		public void DeInitialize()
		{
			// Flush all in-memory caches on shutdown (close window / process exit).
			// Must run before GameWindow.Dispose() while the GL context is still alive.
			try
			{
				TextureManager?.UnloadAllTextures(false);
			}
			catch
			{
				// Ignored - best effort cleanup during shutdown
			}
			try
			{
				currentHost?.ClearObjectCaches();
				currentHost?.ClearErrors();
			}
			catch
			{
				// Ignored - best effort cleanup during shutdown
			}
			if (nullDepthMap != 0)
			{
				GL.DeleteTexture(nullDepthMap);
				nullDepthMap = 0;
			}
			GameWindow?.Dispose();
			// terminate spinning thread
			VisibilityThreadShouldRun = false;
		}
		
		/// <summary>Performs the CSM shadow depth rendering pass for all geometry.</summary>
		protected void PerformCSMShadowPass() => Shadows.RenderPass();

		/// <summary>Binds cascading shadow data to the default shader.</summary>
		protected void BindCSMToDefaultShader() => Shadows.Bind(DefaultShader);

		internal PrimitiveType GetPrimitiveType(FaceFlags flags)
		{
			return faceRenderer.GetPrimitiveType(flags);
		}

		/// <summary>Performs cleanup of disposed resources</summary>
		public void ReleaseResources()
		{
			//Must remember to lock on the lists as the destructor is in a different thread
			lock (vaoToDelete)
			{
				foreach (int VAO in vaoToDelete)
				{
					GL.DeleteVertexArray(VAO);
				}
				vaoToDelete.Clear();
			}

			lock (vboToDelete)
			{
				foreach (int VBO in vboToDelete)
				{
					GL.DeleteBuffer(VBO);
				}
				vboToDelete.Clear();
			}

			lock (iboToDelete)
			{
				foreach (int IBO in iboToDelete)
				{
					GL.DeleteBuffer(IBO);
				}
				iboToDelete.Clear();
			}
		}

		/// <summary>
		/// Performs a reset of OpenGL to the default state
		/// </summary>
		public virtual void ResetOpenGlState()
		{
			renderState.ResetOpenGlState();
		}

		public void PushMatrix(MatrixMode Mode)
		{
			switch (Mode)
			{
				case MatrixMode.Modelview:
					viewMatrixList.Add(CurrentViewMatrix);
					break;
				case MatrixMode.Projection:
					projectionMatrixList.Add(CurrentProjectionMatrix);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(Mode), Mode, null);
			}
		}

		public void PopMatrix(MatrixMode Mode)
		{
			switch (Mode)
			{
				case MatrixMode.Modelview:
					CurrentViewMatrix = viewMatrixList.Last();
					viewMatrixList.RemoveAt(viewMatrixList.Count - 1);
					break;
				case MatrixMode.Projection:
					CurrentProjectionMatrix = projectionMatrixList.Last();
					projectionMatrixList.RemoveAt(projectionMatrixList.Count - 1);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(Mode), Mode, null);
			}
		}

		public void Reset()
		{
			currentHost.ClearAnimatedObjectCache();
			currentHost.PruneStaleStaticObjects();
			TextureManager.UnloadAllTextures(true);
			VisibleObjects.Clear();
		}

		public int CreateStaticObject(StaticObject Prototype, Vector3 Position, Transformation WorldTransformation, Transformation LocalTransformation, ObjectDisposalMode AccurateObjectDisposal, ObjectCreationParameters Parameters, double BlockLength)
		{
			Matrix4D Translate = Matrix4D.CreateTranslation(Position.X, Position.Y, -Position.Z);
			Matrix4D Rotate = (Matrix4D)new Transformation(LocalTransformation ?? Transformation.NullTransformation, WorldTransformation);
			return CreateStaticObject(Position, Prototype, LocalTransformation, Rotate, Translate, AccurateObjectDisposal, Parameters, BlockLength);
		}

		public int CreateStaticObject(Vector3 Position, StaticObject Prototype, Transformation LocalTransformation, Matrix4D Rotate, Matrix4D Translate, ObjectDisposalMode AccurateObjectDisposal, ObjectCreationParameters Parameters, double BlockLength)
		{
			if (Prototype == null)
			{
				return -1;
			}

			if (Prototype.Mesh.Faces.Length == 0)
			{
				//Null object- Waste of time trying to calculate anything for these
				return -1;
			}

			float startingDistance = float.MaxValue;
			float endingDistance = float.MinValue;

			if (AccurateObjectDisposal == ObjectDisposalMode.Accurate)
			{
				foreach (VertexTemplate vertex in Prototype.Mesh.Vertices)
				{
					Vector3 Coordinates = new Vector3(vertex.Coordinates);
					Coordinates.Rotate(LocalTransformation ?? Transformation.NullTransformation);

					if (Coordinates.Z < startingDistance)
					{
						startingDistance = (float)Coordinates.Z;
					}

					if (Coordinates.Z > endingDistance)
					{
						endingDistance = (float)Coordinates.Z;
					}
				}

				startingDistance += (float)Parameters.AccurateObjectDisposalZOffset;
				endingDistance += (float)Parameters.AccurateObjectDisposalZOffset;
			}

			const double minBlockLength = 20.0;

			if (BlockLength < minBlockLength)
			{
				BlockLength *= Math.Ceiling(minBlockLength / BlockLength);
			}

			switch (AccurateObjectDisposal)
			{
				case ObjectDisposalMode.Accurate:
					startingDistance += (float)Parameters.TrackPosition;
					endingDistance += (float)Parameters.TrackPosition;
					double z = BlockLength * Math.Floor(Parameters.TrackPosition / BlockLength);
					Parameters.StartingDistance = Math.Min(z - BlockLength, startingDistance);
					Parameters.EndingDistance = Math.Max(z + 2.0 * BlockLength, endingDistance);
					startingDistance = (float)(BlockLength * Math.Floor(Parameters.StartingDistance / BlockLength));
					endingDistance = (float)(BlockLength * Math.Ceiling(Parameters.EndingDistance / BlockLength));
					break;
				case ObjectDisposalMode.Legacy:
					startingDistance = (float)Parameters.StartingDistance;
					endingDistance = (float)Parameters.EndingDistance;
					break;
				case ObjectDisposalMode.Mechanik:
					startingDistance = (float)Parameters.StartingDistance;
					endingDistance = (float)Parameters.EndingDistance + 1500;
					if (startingDistance < 0)
					{
						startingDistance = 0;
					}
					break;
			}
			StaticObjectStates.Add(new ObjectState
			{
				Prototype = Prototype,
				Translation = Translate,
				Rotate = Rotate,
				StartingDistance = startingDistance,
				EndingDistance = endingDistance,
				WorldPosition = Position,
				DisableShadowCasting = Parameters.DisableShadowCasting
			});
			
			foreach (MeshFace face in Prototype.Mesh.Faces)
			{
				switch (face.Flags & FaceFlags.FaceTypeMask)
				{
					case FaceFlags.Triangles:
						InfoTotalTriangles++;
						break;
					case FaceFlags.TriangleStrip:
						InfoTotalTriangleStrip++;
						break;
					case FaceFlags.Quads:
						InfoTotalQuads++;
						break;
					case FaceFlags.QuadStrip:
						InfoTotalQuadStrip++;
						break;
					case FaceFlags.Polygon:
						InfoTotalPolygon++;
						break;
				}
			}

			return StaticObjectStates.Count - 1;
		}

		public void CreateDynamicObject(ref ObjectState internalObject)
		{
			if (internalObject == null)
			{
				internalObject = new ObjectState( new StaticObject(currentHost));
			}

			internalObject.Prototype.Dynamic = true;

			DynamicObjectStates.Add(internalObject);
		}

		/// <summary>Initializes the visibility of all objects within the game world</summary>
		/// <remarks>If the new renderer is enabled, this must be run in a thread processing an openGL context in order to successfully create
		/// the required VAO objects</remarks>
		public void InitializeVisibility()
		{
			for (int i = 0; i < StaticObjectStates.Count; i++)
			{
				VAOExtensions.CreateOrUpdateVAO(StaticObjectStates[i].Prototype.Mesh, false, DefaultShader.VertexLayout, this);
				/*
				 * n.b.
				 * Only create the actual matrix buffer at first frame render time
				 * I can't find why at the minute, but Object Viewer otherwise doesn't show them, and attempting
				 * to retrieve previously set matricies from the shader shows all zeros
				 *
				 * Probably a timing issue, but it works doing it that way :/
				 */
			}
			for (int i = 0; i < DynamicObjectStates.Count; i++)
			{
				VAOExtensions.CreateOrUpdateVAO(DynamicObjectStates[i].Prototype.Mesh, false, DefaultShader.VertexLayout, this);
			}
            ObjectsSortedByStart = null;
			ObjectsSortedByEnd = null;
			visibilityManager.BuildSortIndices(StaticObjectStates);
			visibilityManager.LastUpdatedTrackPosition = LastUpdatedTrackPosition;
			
			if (currentOptions.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
			{
				foreach (ObjectState state in StaticObjectStates)
				{
					VisibleObjects.quadTree.Add(state, Orientation3.Default);
				}
				VisibleObjects.quadTree.Initialize(currentOptions.QuadTreeLeafSize);
				UpdateQuadTreeVisibility();
			}
			else
			{
				double p = CameraTrackFollower.TrackPosition + Camera.Alignment.Position.Z;
				foreach (ObjectState state in StaticObjectStates.Where(recipe => recipe.StartingDistance <= p + Camera.ForwardViewingDistance & recipe.EndingDistance >= p - Camera.BackwardViewingDistance))
				{
					VisibleObjects.ShowObject(state, ObjectType.Static);
				}
			}
		}

		private VisibilityUpdate updateVisibility;
		/// <summary>The lock to be held whilst visibility updates or loading operations are in progress</summary>
		public object VisibilityUpdateLock = new object();
		
		public bool VisibilityThreadShouldRun = true;

		public Thread VisibilityThread;

		private void RunVisibiliityThread()
		{
			while (VisibilityThreadShouldRun)
			{
				lock (VisibilityUpdateLock)
				{
					if (updateVisibility != VisibilityUpdate.None && CameraTrackFollower != null)
					{
						UpdateVisibility(CameraTrackFollower.TrackPosition + Camera.Alignment.Position.Z);
					}
				}

				if (updateVisibility == VisibilityUpdate.None)
				{
					Thread.Sleep(100);
				}
			}
		}

		public void UpdateVisibility(bool force)
		{
			updateVisibility = force ? VisibilityUpdate.Force : VisibilityUpdate.Normal;
		}

		private void UpdateVisibility(double trackPosition)
		{
			if (currentOptions.ObjectDisposalMode == ObjectDisposalMode.QuadTree)
			{
				UpdateQuadTreeVisibility();
			}
			else
			{
				if (updateVisibility == VisibilityUpdate.Normal)
				{
					UpdateLegacyVisibility(trackPosition);
				}
				else
				{
					/*
					 * The original visibility algorithm fails to handle correctly cases where the
					 * camera angle is rotated, but the track position does not change
					 *
					 * Horrible kludge...
					 */
					UpdateLegacyVisibility(trackPosition + 0.01);
					UpdateLegacyVisibility(trackPosition - 0.01);
				}
				
			}

			updateVisibility = VisibilityUpdate.None;
		}

		private void UpdateQuadTreeVisibility()
		{
			if (VisibleObjects == null || VisibleObjects.quadTree == null)
			{
				Thread.Sleep(10);
				return;
			}
			Camera.UpdateQuadTreeLeaf();
		}

		private void UpdateLegacyVisibility(double trackPosition)
		{
			visibilityManager.UpdateLegacyVisibility(trackPosition, StaticObjectStates, VisibleObjects, Camera, CameraTrackFollower);
		}

		public void UpdateViewingDistances(double backgroundImageDistance)
		{
			visibilityManager.UpdateViewingDistances(backgroundImageDistance, Camera, CameraTrackFollower);
			updateVisibility = VisibilityUpdate.Force;
		}

		/// <summary>Determines the maximum Anisotropic filtering level the system supports</summary>
		public void DetermineMaxAFLevel()
		{
			if (currentHost.Platform == HostPlatform.AppleOSX)
			{
				// Calling GL.GetString in this manner seems to be crashing the OS-X driver (No idea, but probably OpenTK....)
				// As we only support newer Intel Macs, 16x AF is safe
				currentOptions.AnisotropicFilteringMaximum = 16;
				return;
			}
			
			string[] Extensions;
			try
			{
				Extensions = GL.GetString(StringName.Extensions).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				ErrorCode error = GL.GetError();
				if (error == ErrorCode.InvalidEnum)
				{
					// Doing this on a forward compatible GL context fails with invalid enum
					currentOptions.AnisotropicFilteringMaximum = 16;
					return;
				}
			}
			catch
			{
				currentOptions.AnisotropicFilteringMaximum = 0;
				currentOptions.AnisotropicFilteringLevel = 0;
				return;
			}
			currentOptions.AnisotropicFilteringMaximum = 0;

			foreach (string extension in Extensions)
			{
				if (string.Compare(extension, "GL_EXT_texture_filter_anisotropic", StringComparison.OrdinalIgnoreCase) == 0)
				{
					float n = GL.GetFloat((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
					int MaxAF = (int)Math.Round(n);

					if (MaxAF != currentOptions.AnisotropicFilteringMaximum)
					{
						currentOptions.AnisotropicFilteringMaximum = (int)Math.Round(n);
					}
					break;
				}
			}

			if (currentOptions.AnisotropicFilteringMaximum <= 0)
			{
				currentOptions.AnisotropicFilteringMaximum = 0;
				currentOptions.AnisotropicFilteringLevel = 0;
			}
			else if (currentOptions.AnisotropicFilteringLevel == 0 & currentOptions.AnisotropicFilteringMaximum > 0)
			{
				currentOptions.AnisotropicFilteringLevel = currentOptions.AnisotropicFilteringMaximum;
			}
			else if (currentOptions.AnisotropicFilteringLevel > currentOptions.AnisotropicFilteringMaximum)
			{
				currentOptions.AnisotropicFilteringLevel = currentOptions.AnisotropicFilteringMaximum;
			}
		}
		
		/// <summary>Updates the openGL viewport</summary>
		/// <param name="mode">The viewport change mode</param>
		public void UpdateViewport(ViewportChangeMode mode)
		{
			switch (mode)
			{
				case ViewportChangeMode.ChangeToScenery:
					CurrentViewportMode = ViewportMode.Scenery;
					break;
				case ViewportChangeMode.ChangeToCab:
					CurrentViewportMode = ViewportMode.Cab;
					break;
			}

			UpdateViewport(Screen.Width, Screen.Height);
		}

		protected virtual void UpdateViewport(int Width, int Height)
		{
			Screen.Width = Width;
			Screen.Height = Height;
			GL.Viewport(0, 0, Screen.Width, Screen.Height);

			Screen.AspectRatio = Screen.Width / (double)Screen.Height;
			Camera.HorizontalViewingAngle = 2.0 * Math.Atan(Math.Tan(0.5 * Camera.VerticalViewingAngle) * Screen.AspectRatio);
			double nearClip = Math.Max(0.01, currentOptions.NearClipBase);
			CurrentProjectionMatrix = Matrix4D.CreatePerspectiveFieldOfView(Camera.VerticalViewingAngle, Screen.AspectRatio, nearClip, currentOptions.ViewingDistance);
		}

		public void ResetShader(Shader shader)
		{
#if DEBUG
			if (!ReShadeInUse)
			{
				lastError = GL.GetError();

				if (lastError != ErrorCode.NoError)
				{
					throw new InvalidOperationException($"OpenGL Error: {lastError}");
				}
			}
#endif

			shader.SetCurrentProjectionMatrix(Matrix4D.Identity);
			shader.SetCurrentModelViewMatrix(Matrix4D.Identity);
			shader.SetCurrentTextureMatrix(Matrix4D.Identity);
			shader.SetIsLight(false);
			shader.SetLightPosition(Vector3.Zero);
			shader.SetLightAmbient(Color24.White);
			shader.SetLightDiffuse(Color24.White);
			shader.SetLightSpecular(Color24.White);
			shader.SetLightModel(Lighting.LightModel);
			shader.SetMaterialAmbient(Color24.White);
			shader.SetMaterialDiffuse(Color24.White);
			shader.SetMaterialSpecular(Color24.White);
			shader.SetMaterialEmission(Color24.White);
			lastColor = Color32.White;
			shader.SetMaterialShininess(1.0f);
			shader.SetFog(false);
			shader.DisableTexturing();
			shader.SetTexture(0);
			shader.SetBrightness(1.0f);
			shader.SetOpacity(1.0f);
			shader.SetObjectIndex(0);
			shader.SetAlphaTest(false);
		}

		public void SetBlendFunc()
		{
			renderState.SetBlendFunc();
		}

		public void SetBlendFunc(BlendingFactor srcFactor, BlendingFactor destFactor)
		{
			renderState.SetBlendFunc(srcFactor, destFactor);
		}

		public void UnsetBlendFunc()
		{
			renderState.UnsetBlendFunc();
		}

		public void RestoreBlendFunc()
		{
			renderState.RestoreBlendFunc();
		}

		/// <summary>Specifies the OpenGL alpha function to perform</summary>
		public void SetAlphaFunc()
		{
			renderState.SetAlphaFunc();
		}

		/// <summary>Specifies the OpenGL alpha function to perform</summary>
		/// <param name="comparison">The comparison to use</param>
		/// <param name="value">The value to compare</param>
		public void SetAlphaFunc(AlphaFunction comparison, float value)
		{
			renderState.SetAlphaFunc(comparison, value);
        }

		/// <summary>Disables OpenGL alpha testing</summary>
		public void UnsetAlphaFunc()
		{
			renderState.UnsetAlphaFunc();
        }

		/// <summary>Restores the OpenGL alpha function to it's previous state</summary>
		public void RestoreAlphaFunc()
		{
			renderState.RestoreAlphaFunc();
		}


		// Cached object state is owned by FaceRenderer; kept as a proxy for compatibility.
		protected internal ObjectState lastObjectState
		{
			get => faceRenderer.LastObjectState;
			set => faceRenderer.LastObjectState = value;
		}

		/// <summary>Draws a face using the current shader</summary>
		/// <param name="state">The FaceState to draw</param>
		/// <param name="isDebugTouchMode">Whether debug touch mode</param>
		public void RenderFace(FaceState state, bool isDebugTouchMode = false)
		{
			faceRenderer.RenderFace(state, isDebugTouchMode);
		}

		/// <summary>Draws a face using the specified shader and matrices</summary>
		/// <param name="shader">The shader to use</param>
		/// <param name="state">The ObjectState to draw</param>
		/// <param name="face">The Face within the ObjectState</param>
		/// <param name="modelMatrix">The model matrix to use</param>
		/// <param name="modelViewMatrix">The modelview matrix to use</param>
		public void RenderFace(Shader shader, ObjectState state, MeshFace face, Matrix4D modelMatrix, Matrix4D modelViewMatrix)
		{
			faceRenderer.RenderFace(shader, state, face, modelMatrix, modelViewMatrix);
		}

		/// <summary>Draws a face using the specified shader</summary>
		/// <param name="shader">The shader to use</param>
		/// <param name="state">The ObjectState to draw</param>
		/// <param name="face">The Face within the ObjectState</param>
		/// <param name="debugTouchMode">Whether debug touch mode</param>
		/// <param name="screenSpace">Used when a forced matrix, for items which are in screen space not camera space</param>
		public void RenderFace(Shader shader, ObjectState state, MeshFace face, bool debugTouchMode = false, bool screenSpace = false)
		{
			faceRenderer.RenderFace(shader, state, face, debugTouchMode, screenSpace);

		}


		/// <summary>Sets the current MouseCursor</summary>
		/// <param name="newCursor">The new cursor</param>
		public void SetCursor(OpenTK.MouseCursor newCursor)
		{
			GameWindow.Cursor = newCursor;
		}

		/// <summary>Sets the window state</summary>
		/// <param name="windowState">The new window state</param>
		public void SetWindowState(WindowState windowState)
		{
			GameWindow.WindowState = windowState;
			if (windowState == WindowState.Fullscreen)
			{
				// move origin appropriately
				GameWindow.X = 0;
				GameWindow.Y = 0;
			}
		}

		/// <summary>Sets the size of the window</summary>
		/// <param name="width">The new width</param>
		/// <param name="height">The new height</param>
		public void SetWindowSize(int width, int height)
		{
			GameWindow.Width = width;
			GameWindow.Height = height;
			Screen.Width = width;
			Screen.Height = height;

			if (width == DisplayDevice.Default.Width && height == DisplayDevice.Default.Height)
			{
				SetWindowState(WindowState.Maximized);
			}
		}

		public ConcurrentQueue<ThreadStart> RenderThreadJobs;

		/// <summary>This method is used during loading to run commands requiring an OpenGL context in the main render loop</summary>
		/// <param name="job">The OpenGL command</param>
		/// <param name="timeout">The timeout</param>
		public void RunInRenderThread(ThreadStart job, int timeout)
		{
			RenderThreadJobs.Enqueue(job);
			//Don't set the job to available until after it's been loaded into the queue
			RenderThreadJobWaiting = true;
			//Failsafe: If our job has taken more than the timeout, stop waiting for it
			//A missing texture is probably better than an infinite loadscreen
			lock (job)
			{
				Monitor.Wait(job, timeout);
			}
		}
	}
}
