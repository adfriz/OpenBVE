using System;
using System.Runtime.ExceptionServices;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace LibRender2
{
	/// <summary>
	/// Snapshot of the OpenGL capabilities of the current context.
	/// Created once after a context is current; all feature selection reads this rather than
	/// assuming a fixed GL version. The safe baseline is 4.1 - the version the current shaders target.
	/// </summary>
	public sealed class GlCapabilities
	{
		/// <summary>Highest version requested first, descending to the established 4.1 baseline.</summary>
		private static readonly int[][] CandidateVersions =
		{
			new[] { 4, 6 },
			new[] { 4, 5 },
			new[] { 4, 4 },
			new[] { 4, 3 },
			new[] { 4, 2 },
			new[] { 4, 1 }
		};

		public int Major { get; private set; }
		public int Minor { get; private set; }
		public string VersionString { get; private set; }
		public string GlslVersion { get; private set; }

		private GlCapabilities(int major, int minor, string versionString, string glslVersion)
		{
			Major = major;
			Minor = minor;
			VersionString = versionString;
			GlslVersion = glslVersion;
		}

		public bool IsAtLeast(int major, int minor)
		{
			return Major > major || (Major == major && Minor >= minor);
		}

		public override string ToString()
		{
			return Major + "." + Minor + " (" + VersionString + ", GLSL " + GlslVersion + ")";
		}

		/// <summary>Reads the version of the currently current context. Never throws.</summary>
		/// <param name="queryStrings">False on platforms where GL.GetString is known to be unsafe.</param>
		public static GlCapabilities Detect(bool queryStrings = true)
		{
			int major = 4;
			int minor = 1;
			try
			{
				int detectedMajor;
				int detectedMinor;
				GL.GetInteger(GetPName.MajorVersion, out detectedMajor);
				GL.GetInteger(GetPName.MinorVersion, out detectedMinor);
				if (GL.GetError() == ErrorCode.NoError && detectedMajor > 0 && detectedMinor >= 0)
				{
					major = detectedMajor;
					minor = detectedMinor;
				}
			}
			catch
			{
				// No usable context - stay on the safe baseline.
				major = 4;
				minor = 1;
			}

			string version = "unknown";
			string glsl = "unknown";
			if (queryStrings)
			{
				try
				{
					version = GL.GetString(StringName.Version) ?? "unknown";
				}
				catch
				{
					version = "unknown";
					GL.GetError();
				}
				try
				{
					glsl = GL.GetString(StringName.ShadingLanguageVersion) ?? "unknown";
				}
				catch
				{
					glsl = "unknown";
					GL.GetError();
				}
			}
			return new GlCapabilities(major, minor, version, glsl);
		}

		/// <summary>
		/// Tries to create a window at the highest supported GL version, falling back one version at a
		/// time and finally to the host's original (legacy) constructor. The factory receives the
		/// requested major/minor; (0, 0) means "use the legacy constructor".
		/// </summary>
		[HandleProcessCorruptedStateExceptions]
		public static GameWindow CreateWindow(Func<int, int, GameWindow> factory, out GlCapabilities capabilities, Action<string> log = null)
		{
			if (factory == null)
			{
				throw new ArgumentNullException(nameof(factory));
			}

			foreach (int[] version in CandidateVersions)
			{
				try
				{
					GameWindow window = factory(version[0], version[1]);
					window.MakeCurrent();
					capabilities = Detect(false);
					if (log != null)
					{
						log("[GL] Requested " + version[0] + "." + version[1] + ", got " + capabilities.Major + "." + capabilities.Minor + ".");
					}
					return window;
				}
				catch (Exception ex)
				{
					if (log != null)
					{
						log("[GL] Requested " + version[0] + "." + version[1] + " failed: " + ex.GetType().Name + ": " + ex.Message);
					}
					// Do NOT touch the GL API here: a failed creation may have left no current
					// context at all, and GL.GetError() faults on some drivers in that state.
				}
			}

			capabilities = new GlCapabilities(4, 1, "legacy context", "unknown");
			if (log != null)
			{
				log("[GL] No 4.x context could be created; falling back to the legacy context.");
			}
			return factory(0, 0);
		}
	}
}
