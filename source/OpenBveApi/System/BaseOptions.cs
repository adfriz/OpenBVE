using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenBveApi.Colors;
using OpenBveApi.Graphics;
using OpenBveApi.Objects;
using OpenBveApi.Routes;
using OpenBveApi.Trains;
using OpenBveApi.Interface;

namespace OpenBveApi
{
	/// <summary>Defines the base shared options to be passed to the Renderer etc.</summary>
	public abstract class BaseOptions
	{
		/// <summary>The ISO 639-1 code for the current user interface language</summary>
		public string LanguageCode;
		/// <summary>Whether the program is to be run in full-screen mode</summary>
		public bool FullscreenMode;
		/// <summary>Whether the program is to be rendered using vertical synchronisation</summary>
		public bool VerticalSynchronization;
		/// <summary>The screen width (Windowed Mode)</summary>
		public int WindowWidth;
		/// <summary>The screen height (Windowed Mode)</summary>
		public int WindowHeight;
		/// <summary>The screen width (Fullscreen Mode)</summary>
		public int FullscreenWidth;
		/// <summary>The screen height (Fullscreen Mode)</summary>
		public int FullscreenHeight;
		/// <summary>The number of bits per pixel (Only relevant in fullscreen mode)</summary>
		public int FullscreenBits;
		/// <summary>The current pixel interpolation mode </summary>
		public InterpolationMode Interpolation;
		/// <summary>The current transparency quality mode</summary>
		public TransparencyMode TransparencyMode;
		/// <summary>The level of anisotropic filtering to be applied</summary>
		public int AnisotropicFilteringLevel;
		/// <summary>The maximum level of anisotropic filtering supported by the system</summary>
		public int AnisotropicFilteringMaximum;
		/// <summary>The level of antialiasing to be applied</summary>
		public int AntiAliasingLevel;
		/// <summary>The parser to use for Microsoft DirectX objects</summary>
		public XParsers CurrentXParser;
		/// <summary>The parser to use for Wavefront Obj objects</summary>
		public ObjParsers CurrentObjParser;
		/// <summary>Enables / disables various hacks for BVE related content</summary>
		public bool EnableBveTsHacks;
		/// <summary>Stores whether to use fuzzy matching for transparency colors (Matches BVE2 / BVE4 behaviour)</summary>
		public bool OldTransparencyMode;
		/// <summary>The viewing distance in meters</summary>
		public int ViewingDistance;
		/// <summary>The size of a leaf when using QuadTree visibility</summary>
		public int QuadTreeLeafSize;
		/// <summary>Whether toppling is enabled</summary>
		public bool Toppling;
		/// <summary>Whether derailments are enabled</summary>
		public bool Derailments;
		/// <summary>The number 1km/h must be multiplied by to produce your desired speed units, or 0.0 to disable this</summary>
		public double SpeedConversionFactor = 0.0;
		/// <summary>The unit of speed displayed in in-game messages</summary>
		public string UnitOfSpeed = "km/h";
		/// <summary>The default mode for the train's safety system to start in</summary>
		public TrainStartMode TrainStart = TrainStartMode.EmergencyBrakesAts;
		/// <summary>The initial destination for any train within the game</summary>
		public int InitialDestination = -1;
		/// <summary>The initial camera viewpoint</summary>
		public int InitialViewpoint = 0;
		/// <summary>The speed limit for any preceding AI trains</summary>
		public double PrecedingTrainSpeedLimit = double.PositiveInfinity;
		/// <summary>The name of the current train</summary>
		public string TrainName = "";
		/// <summary>The current compatibility signal set</summary>
		public string CurrentCompatibilitySignalSet;
		/// <summary>Allows a forwards compatible context to be forced</summary>
		public bool ForceForwardsCompatibleContext;
		/*
		 * Note: Object optimisation takes time whilst loading, but may increase the render performance of an
		 * object by checking for duplicate vertices etc.
		 */
		/// <summary>The minimum number of vertices for basic optimisation to be performed on an object</summary>
		public int ObjectOptimizationBasicThreshold;
		/// <summary>The maximum number of sounds playing at any one time</summary>
		public int SoundNumber;
		/// <summary>Shadow map resolution per cascade. Off disables shadows.</summary>
		public ShadowMapResolution ShadowResolution = ShadowMapResolution.Off;
		/// <summary>Maximum distance from the camera at which shadows appear.</summary>
		public ShadowDistance ShadowDrawDistance = ShadowDistance.Medium;
		/// <summary>Number of shadow cascades.</summary>
		public ShadowCascadeCount ShadowCascades = ShadowCascadeCount.Three;
		/// <summary>Shadow darkness strength. 0.0 = invisible, 1.0 = full black.</summary>
		public double ShadowStrength = 0.7;
		/// <summary>Shadow bias to prevent shadow acne.</summary>
		public double ShadowBias = 0.000005; // default synced to 0.000005
		/// <summary>Shadow normal bias (slope scale multiplier) to perfectly cure acne on curved/thin meshes.</summary>
		public double ShadowNormalBias = 2.0;
		/// <summary>Whether to filter shadow casters per cascade to improve performance.</summary>
		public bool ShadowFilterCascades = true;

		/// <summary>Master switch for the stackable post-processing chain. Default OFF so existing output is untouched.</summary>
		public bool EnablePostProcessing = false;
		/// <summary>Default effect order when options.cfg has no PostEffectOrder key.</summary>
		public const string DefaultPostEffectOrder = "AmbientOcclusion,FXAA,Sharpen,Vignette";
		/// <summary>Comma-separated effect order, e.g. "AmbientOcclusion,FXAA,Sharpen,Vignette".</summary>
		public string PostEffectOrder = DefaultPostEffectOrder;
		/// <summary>Whether the FXAA effect is enabled.</summary>
		public bool PostFxaa = false;
		/// <summary>Whether the Sharpen effect is enabled.</summary>
		public bool PostSharpen = false;
		/// <summary>Whether the Vignette effect is enabled.</summary>
		public bool PostVignette = false;
		/// <summary>Ambient occlusion mode. Off disables AO.</summary>
		public AmbientOcclusionMode AoMode = AmbientOcclusionMode.Off;
		/// <summary>AO sampling radius in meters. Range 0.1-5.</summary>
		public float AoRadius = 1.2f;
		/// <summary>AO strength multiplier. Range 0-2.</summary>
		public float AoIntensity = 1.0f;
		/// <summary>AO contrast curve exponent. Range 0.5-3.</summary>
		public float AoPower = 1.5f;
		/// <summary>AO depth bias factor (x radius). RETIRED by CACAO port; kept for cfg compat.</summary>
		public float AoBias = 0.05f;
		/// <summary>AO render resolution scale. One of 0.25 / 0.5 / 1.0.</summary>
		public float AoResolutionScale = 0.5f;
		/// <summary>AO bilateral blur radius.</summary>
		public int AoBlurRadius = 3;
		/// <summary>AO bilateral depth sharpness.</summary>
		public float AoBlurSharpness = 0.01f;
		/// <summary>Whether AO also affects the 3D cab layer (2D cab and HUD are always sterile).</summary>
		public bool AoAffectCab3D = true;
		/// <summary>AO debug view. 0 = composite, 1 = AO-only.</summary>
		public int AoDebugView = 0;
		/// <summary>SAO pattern tap count (each = 2 mirrored samples). Tiers 3 / 5 / 12.</summary>
		public int SaoSamples = 5;
		/// <summary>SAO spiral turns. RETIRED by CACAO port; kept for cfg compat.</summary>
		public int SaoSpiralTurns = 7;
		/// <summary>SAO horizon-angle threshold (CACAO). Range 0-0.2.</summary>
		public float AoHorizonThreshold = 0.06f;
		/// <summary>SAO detail-AO strength from immediate neighbors (CACAO). Range 0-5.</summary>
		public float AoDetailStrength = 0.5f;
		/// <summary>GTAO slice count. Default 4 = Balanced tier (matches effect ctor).</summary>
		public int GtaoSlices = 4;
		/// <summary>GTAO steps per side. Default 3 = Balanced tier (matches effect ctor).</summary>
		public int GtaoSteps = 3;
		/// <summary>GTAO falloff range.</summary>
		public float GtaoFalloffRange = 0.4f;


		/// <summary>The sun azimuth in degrees</summary>
		public double LightAzimuth = -26.57;
		/// <summary>The sun elevation in degrees</summary>
		public double LightElevation = 60.0;
		/// <summary>Whether debug logs should be generated</summary>
		public bool GenerateDebugLogging;
		/// <summary>Whether loading sway is added</summary>
		public bool LoadingSway;
		/// <summary>The game mode- Affects how the score is calculated</summary>
		public GameMode GameMode;
		/// <summary>Whether Panel2 is loaded using the extended touch controls mode</summary>
		public bool Panel2ExtendedMode;
		/// <summary>The minimum size for a Panel2 control to be considered touch sensitive</summary>
		public int Panel2ExtendedMinSize;
		/// <summary>Whether various accessibility helpers are enabled</summary>
		public bool Accessibility;
		/// <summary>The font to use</summary>
		public string Font;
		/// <summary>The object disposal mode in use</summary>
		/// <remarks>Not saved</remarks>
		public ObjectDisposalMode ObjectDisposalMode;
		/// <summary>Uses the native Windows GDI+ decoders for PNG / JPG</summary>
		public bool UseGDIDecoders;
		/// <summary>The filename of the current cursor</summary>
		public string CursorFileName;
		/// <summary>The download location for the train required by the current route</summary>
		public string TrainDownloadLocation = "";
		/// <summary>Whether delayed animated updates based upon track position are used</summary>
		/// <remarks>Not saved</remarks>
		public bool DelayedAnimatedUpdates;
		/// <summary>Whether the adhesion hack is enabled</summary>
		/// <remarks>Not saved</remarks>
		public bool AdhesionHack;
		/// <summary>Enables scripted trains on BVE5 routes</summary>
		public bool EnableBve5ScriptedTrain;
		/// <summary>The scale factor for the user interface</summary>
		public int UserInterfaceScaleFactor;
		/// <summary>Whether loaded objects are automatically reloaded on change</summary>
		public bool AutoReloadObjects;
		
		/// <summary>The near clipping plane for scenery</summary>
		public double NearClipScenery = 0.5;
		/// <summary>The near clipping plane for the cab</summary>
		public double NearClipCab = 0.025;
		/// <summary>The near clipping plane for the base renderer</summary>
		public double NearClipBase = 0.2;
		/// <summary>The color used by the renderer when issuing GL.Clear()</summary>
		/// <remarks>Not saved</remarks>
		public Color24 ClearColor = new Color24(170, 170, 170);

		/// <summary>Normalizes a PostEffectOrder CSV via Parse (trimmed, deduped, order-preserved; empty -&gt; default).</summary>
		public static string NormalizePostEffectOrder(string csv)
		{
			var ids = ParsePostEffectOrder(csv);
			if (ids.Count == 0)
			{
				return DefaultPostEffectOrder;
			}
			return string.Join(",", ids.ToArray());
		}

		/// <summary>Parses a PostEffectOrder CSV into ordered ids (trimmed, deduped case-insensitively, order-preserved).</summary>
		public static List<string> ParsePostEffectOrder(string csv)
		{
			var ids = new List<string>();
			if (string.IsNullOrEmpty(csv))
			{
				return ids;
			}
			string[] parts = csv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				string id = parts[i].Trim();
				if (id.Length == 0)
				{
					continue;
				}
				bool dup = false;
				for (int j = 0; j < ids.Count; j++)
				{
					if (string.Equals(ids[j], id, StringComparison.OrdinalIgnoreCase))
					{
						dup = true;
						break;
					}
				}
				if (!dup)
				{
					ids.Add(id);
				}
			}
			return ids;
		}

		/// <summary>Appends the shared [postprocessing] section.</summary>
		protected void AppendPostProcessingSection(StringBuilder builder)
		{
			builder.AppendLine("[postprocessing]");
			builder.AppendLine("enablepostprocessing = " + (EnablePostProcessing ? "true" : "false"));
			builder.AppendLine("posteffectorder = " + PostEffectOrder);
			builder.AppendLine("postfxaa = " + (PostFxaa ? "true" : "false"));
			builder.AppendLine("postsharpen = " + (PostSharpen ? "true" : "false"));
			builder.AppendLine("postvignette = " + (PostVignette ? "true" : "false"));
		}

		/// <summary>Appends the shared [ambientocclusion] section.</summary>
		protected void AppendAmbientOcclusionSection(StringBuilder builder, CultureInfo culture)
		{
			builder.AppendLine("[ambientocclusion]");
			builder.AppendLine("aomode = " + AoMode);
			builder.AppendLine("aoradius = " + AoRadius.ToString(culture));
			builder.AppendLine("aointensity = " + AoIntensity.ToString(culture));
			builder.AppendLine("aopower = " + AoPower.ToString(culture));
			builder.AppendLine("aobias = " + AoBias.ToString(culture));
			builder.AppendLine("aoresolutionscale = " + AoResolutionScale.ToString(culture));
			builder.AppendLine("aoblurradius = " + AoBlurRadius.ToString(culture));
			builder.AppendLine("aoblursharpness = " + AoBlurSharpness.ToString(culture));
			builder.AppendLine("aoaffectcab3d = " + (AoAffectCab3D ? "true" : "false"));
			builder.AppendLine("aodebugview = " + AoDebugView.ToString(culture));
			builder.AppendLine("saosamples = " + SaoSamples.ToString(culture));
			builder.AppendLine("saospiralturns = " + SaoSpiralTurns.ToString(culture));
			builder.AppendLine("aohorizonthreshold = " + AoHorizonThreshold.ToString(culture));
			builder.AppendLine("aodetailstrength = " + AoDetailStrength.ToString(culture));
			builder.AppendLine("gtaoslices = " + GtaoSlices.ToString(culture));
			builder.AppendLine("gtaosteps = " + GtaoSteps.ToString(culture));
			builder.AppendLine("gtaofalloffrange = " + GtaoFalloffRange.ToString(culture));
		}

		/// <summary>Saves the options to the specified filename</summary>
		/// <param name="fileName">The filename to save the options to</param>
		public abstract void Save(string fileName);
	}
}
