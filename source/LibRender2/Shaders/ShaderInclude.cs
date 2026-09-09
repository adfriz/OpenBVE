using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LibRender2.Shaders
{
	/// <summary>
	/// Minimal <c>#include "chunk"</c> expansion for GLSL sources.
	/// An include line must stand alone (outside comments) and names a chunk
	/// embedded as <c>LibRender2.{name}</c>, e.g. <c>#include "common_blur.glsl"</c>.
	/// Chunks carry no <c>#version</c> line, so the include must sit below the
	/// consumer's own <c>#version</c> directive. Sources without includes are
	/// returned untouched (fast path, zero behavior change for existing shaders).
	/// </summary>
	public static class ShaderInclude
	{
		private const int MaxDepth = 8;

		/// <summary>Expands all <c>#include</c> lines in a GLSL source string.</summary>
		/// <exception cref="InvalidOperationException">Thrown when a chunk is missing or nesting is too deep.</exception>
		public static string ExpandIncludes(string source)
		{
			if (string.IsNullOrEmpty(source) || source.IndexOf("#include", StringComparison.Ordinal) < 0)
			{
				return source;
			}
			return Expand(source, 0);
		}

		private static string Expand(string source, int depth)
		{
			if (depth > MaxDepth)
			{
				throw new InvalidOperationException("Shader #include nesting exceeds " + MaxDepth + " levels.");
			}
			var output = new StringBuilder(source.Length + 1024);
			using (var reader = new StringReader(source))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					string trimmed = line.TrimStart();
					if (trimmed.StartsWith("#include", StringComparison.Ordinal))
					{
						output.AppendLine(Expand(LoadChunk(ParseName(trimmed)), depth + 1));
					}
					else
					{
						output.AppendLine(line);
					}
				}
			}
			return output.ToString();
		}

		private static string ParseName(string directive)
		{
			int first = directive.IndexOf('"');
			int last = directive.LastIndexOf('"');
			if (first < 0 || last <= first + 1)
			{
				throw new InvalidOperationException("Malformed shader #include directive: " + directive);
			}
			string name = directive.Substring(first + 1, last - first - 1);
			foreach (char c in name)
			{
				if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
				{
					throw new InvalidOperationException("Rejected shader #include name: " + name);
				}
			}
			return name;
		}

		private static string LoadChunk(string name)
		{
			var asm = Assembly.GetExecutingAssembly();
			using (var stream = asm.GetManifestResourceStream("LibRender2." + name))
			{
				if (stream == null)
				{
					throw new InvalidOperationException("Shader include chunk not found: LibRender2." + name);
				}
				using (var reader = new StreamReader(stream, Encoding.UTF8))
				{
					return reader.ReadToEnd();
				}
			}
		}
	}
}
