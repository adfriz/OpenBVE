//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, The OpenBVE Project
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

/*
 * NewObjParser
 * ------------
 * I got tired of the Wavefront plugin dragging in the whole Assimp port just
 * to read an OBJ, so this is a small standalone alternative:
 *
 *  - One file, nothing to reference beyond the BCL. Drop it into any project
 *    and it should just work.
 *  - Fast without being clever: the file is scanned in place, tokens are just
 *    (start, end) ranges into the original string, and faces are triangulated
 *    as they're read. The only per-value allocation left is the substring that
 *    double.TryParse needs- everything else (line splitting, tokenizing, face
 *    indices, keyword matching) allocates nothing.
 *  - No statics holding parse state, so route objects loading on several
 *    threads at once can't step on each other.
 *  - The result is plain structs and lists, no OpenBVE types, so other tools
 *    can reuse it too.
 *
 * The bit that turns all that into a StaticObject lives in
 * NewObjParserObject.cs, deliberately kept separate so this file stays portable.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Object.Wavefront
{
	/// <summary>Where we send the "this file is a bit odd" messages while parsing</summary>
	public interface IObjParserLogger
	{
		/// <summary>Something's off but we can carry on</summary>
		void Warning(string message);
		/// <summary>Something's off and the bit it belongs to is unusable</summary>
		void Error(string message);
	}

	/// <summary>Which program (probably) exported this file</summary>
	/// <remarks>Matters because a few exporters quietly use a different handedness or flip the textures</remarks>
	public enum ObjExporter
	{
		/// <summary>No idea- just assume right-handed and hope for the best</summary>
		Unknown = 0,
		/// <summary>Google SketchUp</summary>
		SketchUp,
		/// <summary>BlockBench</summary>
		BlockBench,
		/// <summary>Blender</summary>
		Blender
	}

	/// <summary>One vertex position</summary>
	public struct ObjPosition
	{
		/// <summary>X co-ordinate, already scaled to meters</summary>
		public double X;
		/// <summary>Y co-ordinate, already scaled to meters</summary>
		public double Y;
		/// <summary>Z co-ordinate, already scaled to meters</summary>
		public double Z;
	}

	/// <summary>One texture co-ordinate</summary>
	public struct ObjTexCoord
	{
		/// <summary>U co-ordinate</summary>
		public double U;
		/// <summary>V co-ordinate</summary>
		public double V;
	}

	/// <summary>One vertex normal</summary>
	public struct ObjNormal
	{
		/// <summary>X component</summary>
		public double X;
		/// <summary>Y component</summary>
		public double Y;
		/// <summary>Z component</summary>
		public double Z;
	}

	/// <summary>One corner of a triangle: indices into the model's arrays</summary>
	public struct ObjCorner
	{
		/// <summary>Index into Positions</summary>
		public int Position;
		/// <summary>Index into TexCoords, or -1 when the file didn't give one</summary>
		public int TexCoord;
		/// <summary>Index into Normals, or -1 when the file didn't give one</summary>
		public int Normal;
		/// <summary>
		/// Whether this corner came from a face with smoothing explicitly disabled
		/// </summary>
		/// <remarks>
		/// OBJ's 's off' / 's 0' is the only place a file can ask for flat, single-sided
		/// shading. The bridge uses this to leave Face2Mask off, so backface culling
		/// stays on for those faces. -1 means the file said nothing, which we treat as
		/// smooth / double-sided to match what every exporter expects.
		/// </remarks>
		public bool Flat;
	}

	/// <summary>One material out of the .mtl file</summary>
	public sealed class ObjMaterial
	{
		/// <summary>Its name, i.e. what usemtl refers to</summary>
		public string Name = string.Empty;
		/// <summary>Diffuse red, 0-255</summary>
		public byte R = 255;
		/// <summary>Diffuse green, 0-255</summary>
		public byte G = 255;
		/// <summary>Diffuse blue, 0-255</summary>
		public byte B = 255;
		/// <summary>Opacity, 0-255</summary>
		public byte A = 255;
		/// <summary>Daytime texture path, or null if there isn't one</summary>
		public string DaytimeTexture;
		/// <summary>Nighttime texture path, or null if there isn't one</summary>
		public string NighttimeTexture;
		/// <summary>Transparency texture path, or null if there isn't one</summary>
		public string TransparencyTexture;
	}

	/// <summary>A bunch of triangles that all use the same material</summary>
	public sealed class ObjMesh
	{
		/// <summary>Index into the model's material list, or -1 for plain untextured</summary>
		public int MaterialIndex = -1;
		/// <summary>The triangle corners, three at a time</summary>
		public readonly List<ObjCorner> Corners = new List<ObjCorner>();

		/// <summary>How many triangles are in here</summary>
		public int TriangleCount
		{
			get { return Corners.Count / 3; }
		}
	}

	/// <summary>Everything we got out of one OBJ file</summary>
	public sealed class ObjModel
	{
		/// <summary>The 'o' name, if the file bothered to have one</summary>
		public string Name = string.Empty;
		/// <summary>Best guess at what exported it (drives the flipping below)</summary>
		public ObjExporter Exporter;
		/// <summary>True when X needs mirroring and winding needs reversing</summary>
		public bool LeftHanded;
		/// <summary>True when U comes out upside-down for this exporter</summary>
		public bool FlipUvX;
		/// <summary>True when V comes out upside-down for this exporter</summary>
		public bool FlipUvY;
		/// <summary>Every v line in the file, in order</summary>
		public readonly List<ObjPosition> Positions = new List<ObjPosition>();
		/// <summary>Every vt line in the file, in order</summary>
		public readonly List<ObjTexCoord> TexCoords = new List<ObjTexCoord>();
		/// <summary>Every vn line in the file, in order</summary>
		public readonly List<ObjNormal> Normals = new List<ObjNormal>();
		/// <summary>Every material the mtllib files defined</summary>
		public readonly List<ObjMaterial> Materials = new List<ObjMaterial>();
		/// <summary>The geometry, already split by material / group</summary>
		public readonly List<ObjMesh> Meshes = new List<ObjMesh>();
	}

	/// <summary>Reads Wavefront OBJ files without any dependencies. See the note at the top of this file.</summary>
	public static class NewObjParser
	{
		// Nobody should have a face with more corners than this; guards against a broken file eating memory
		private const int MaxFaceCorners = 1024;

		/// <summary>Read a whole OBJ file off disk</summary>
		/// <param name="filePath">Where it lives</param>
		/// <param name="encoding">What encoding it's in, or null to just guess</param>
		/// <param name="logger">Where complaints go, or null to stay quiet</param>
		public static ObjModel ParseFile(string filePath, System.Text.Encoding encoding = null, IObjParserLogger logger = null)
		{
			string text = encoding == null ? File.ReadAllText(filePath) : File.ReadAllText(filePath, encoding);
			string directory = string.Empty;
			try
			{
				directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
			}
			catch
			{
				// Weird path- just resolve libraries against nothing and let them fail softly later
			}
			return ParseText(text, directory, logger);
		}

		/// <summary>Parse OBJ text you already have in memory (handy for tests)</summary>
		/// <param name="text">The file contents</param>
		/// <param name="baseDirectory">Where mtllib / texture lookups start from</param>
		/// <param name="logger">Where complaints go, or null to stay quiet</param>
		public static ObjModel ParseText(string text, string baseDirectory, IObjParserLogger logger = null)
		{
			Parser parser = new Parser(baseDirectory ?? string.Empty, logger);
			return parser.Run(text);
		}

		// The fast bits are all down here: tokens are (start, end) ranges into the
		// original string, so scanning lines, matching keywords and reading face
		// indices allocates nothing at all. Only actual values (floats, names,
		// paths) ever become strings, and floats are the only per-line case.

		private static bool IsWhite(char c)
		{
			return c == ' ' || c == '\t' || c == '\v' || c == '\f';
		}

		// Steps pos past the next whitespace-separated token in text[pos..end].
		// Returns false when there's nothing left.
		private static bool NextToken(string text, ref int pos, int end, out int tokenStart, out int tokenEnd)
		{
			while (pos < end && IsWhite(text[pos]))
			{
				pos++;
			}
			if (pos >= end)
			{
				tokenStart = tokenEnd = end;
				return false;
			}
			tokenStart = pos;
			while (pos < end && !IsWhite(text[pos]))
			{
				pos++;
			}
			tokenEnd = pos;
			return true;
		}

		// Case-insensitive match against a lower-case keyword, no allocation.
		private static bool IsToken(string text, int start, int end, string keyword)
		{
			if (end - start != keyword.Length)
			{
				return false;
			}
			for (int i = 0; i < keyword.Length; i++)
			{
				char c = text[start + i];
				if (c >= 'A' && c <= 'Z')
				{
					c = (char)(c + 32);
				}
				if (c != keyword[i])
				{
					return false;
				}
			}
			return true;
		}

		// Reads a double out of text[start..end]. This is the one per-value
		// allocation in the whole parser (the substring TryParse needs)- writing
		// our own float parser would remove it, but life's too short.
		private static bool ReadDouble(string text, int start, int end, out double value)
		{
			value = 0.0;
			if (end <= start)
			{
				return false;
			}
			return double.TryParse(text.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		// Reads a plain int out of text[start..end] with no allocation.
		// Fails on empty text and trailing junk, honours a leading sign.
		private static bool ReadInt(string text, int start, int end, out int value)
		{
			value = 0;
			int i = start;
			bool negative = false;
			if (i < end && (text[i] == '+' || text[i] == '-'))
			{
				negative = text[i] == '-';
				i++;
			}
			long acc = 0;
			bool anyDigit = false;
			while (i < end && text[i] >= '0' && text[i] <= '9')
			{
				acc = acc * 10 + (text[i] - '0');
				if (acc > int.MaxValue)
				{
					return false;
				}
				anyDigit = true;
				i++;
			}
			if (!anyDigit || i < end)
			{
				return false;
			}
			value = negative ? -(int)acc : (int)acc;
			return true;
		}

		// OBJ indices are 1-based, and negatives count back from the last one
		// defined. Zero / garbage isn't a valid index.
		private static bool ReadIndex(string text, int start, int end, int count, out int index)
		{
			index = -1;
			int raw;
			if (!ReadInt(text, start, end, out raw) || raw == 0)
			{
				return false;
			}
			index = raw > 0 ? raw - 1 : count + raw;
			return true;
		}

		// All the scratch state for parsing one file. Lives here (not in statics) so
		// two files on two threads can't trip over each other.
		private sealed class Parser
		{
			private readonly string baseDirectory;
			private readonly IObjParserLogger logger;
			private readonly ObjModel model = new ObjModel();
			private readonly Dictionary<string, int> materialsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			private ObjMesh currentMesh;
			private int currentMaterial = -1;
			private double scale = 1.0;
			// Whether 's off' / 's 0' is currently in force; see ObjCorner.Flat
			private bool currentFlat;

			internal Parser(string baseDirectory, IObjParserLogger logger)
			{
				this.baseDirectory = baseDirectory;
				this.logger = logger;
			}

			private void Warn(string message)
			{
				if (logger != null) logger.Warning(message);
			}

			private void Error(string message)
			{
				if (logger != null) logger.Error(message);
			}

			internal ObjModel Run(string text)
			{
				// Walk the lines in place- Split('\n') would duplicate the whole file as an array
				int length = text.Length;
				int pos = 0;
				int lineNumber = 0;
				while (pos <= length)
				{
					int eol = text.IndexOf('\n', pos);
					if (eol < 0)
					{
						eol = length;
					}
					int end = eol;
					if (end > pos && text[end - 1] == '\r')
					{
						end--;
					}
					lineNumber++;
					if (end > pos)
					{
						ProcessLine(text, pos, end, lineNumber);
					}
					pos = eol + 1;
				}
				FlushMesh();
				return model;
			}

			private void ProcessLine(string text, int start, int end, int lineNumber)
			{
				// Comments double as exporter tags ("# SketchUp", "# File units = ..."), so peek at them first
				for (int i = start; i < end; i++)
				{
					if (text[i] == '#')
					{
						ReadComment(text.Substring(i + 1, end - i - 1));
						end = i;
						break;
					}
				}
				int pos = start;
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					return;
				}
				if (IsToken(text, tokenStart, tokenEnd, "v"))
				{
					ReadVertex(text, ref pos, end, lineNumber);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "vt"))
				{
					ReadTexCoord(text, ref pos, end, lineNumber);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "vn"))
				{
					ReadNormal(text, ref pos, end, lineNumber);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "vp"))
				{
					Warn("Parameter space vertices are not supported at line " + lineNumber);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "f"))
				{
					ReadFace(text, ref pos, end, lineNumber);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "o"))
				{
					if (NextToken(text, ref pos, end, out tokenStart, out tokenEnd) && model.Name.Length == 0)
					{
						model.Name = text.Substring(tokenStart, tokenEnd - tokenStart);
					}
					StartMesh();
				}
				else if (IsToken(text, tokenStart, tokenEnd, "g"))
				{
					StartMesh();
				}
				else if (IsToken(text, tokenStart, tokenEnd, "s"))
				{
					// Smoothing groups. OpenBVE shades per-vertex anyway, so the group number
					// itself is meaningless to us - but 'off' / 0 is the file explicitly
					// asking for flat shading, and that we do act on: it keeps Face2Mask off
					// so those faces stay single-sided.
					ReadSmoothing(text, ref pos, end);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "usemtl"))
				{
					UseMaterial(text, ref pos, end);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "mtllib"))
				{
					LoadLibraries(text, ref pos, end);
				}
				// Anything else (l, bevel, trace_obj...) isn't geometry we can draw, so skip it
			}

			// --- Vertices, uvs, normals --------------------------------------------

			// Reads the next token as a double; warns and yields 0 when it's missing or junk
			private bool NextDouble(string text, ref int pos, int end, string what, int lineNumber, out double value)
			{
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd) ||
					!ReadDouble(text, tokenStart, tokenEnd, out value))
				{
					Warn("Invalid " + what + " at line " + lineNumber);
					value = 0.0;
					return false;
				}
				return true;
			}

			private void ReadVertex(string text, ref int pos, int end, int lineNumber)
			{
				double x, y, z;
				NextDouble(text, ref pos, end, "X co-ordinate in Vertex", lineNumber, out x);
				NextDouble(text, ref pos, end, "Y co-ordinate in Vertex", lineNumber, out y);
				NextDouble(text, ref pos, end, "Z co-ordinate in Vertex", lineNumber, out z);
				model.Positions.Add(new ObjPosition { X = x * scale, Y = y * scale, Z = z * scale });
			}

			private void ReadTexCoord(string text, ref int pos, int end, int lineNumber)
			{
				double u, v;
				NextDouble(text, ref pos, end, "U co-ordinate in Texture Co-ordinates", lineNumber, out u);
				NextDouble(text, ref pos, end, "V co-ordinate in Texture Co-ordinates", lineNumber, out v);
				model.TexCoords.Add(new ObjTexCoord { U = u, V = v });
			}

			private void ReadNormal(string text, ref int pos, int end, int lineNumber)
			{
				double x, y, z;
				NextDouble(text, ref pos, end, "X co-ordinate in Vertex Normal", lineNumber, out x);
				NextDouble(text, ref pos, end, "Y co-ordinate in Vertex Normal", lineNumber, out y);
				NextDouble(text, ref pos, end, "Z co-ordinate in Vertex Normal", lineNumber, out z);
				model.Normals.Add(new ObjNormal { X = x, Y = y, Z = z });
			}

			// --- Faces ---------------------------------------------------------------

			// 's off', 's 0' means flat / single-sided from here on. Any other group
			// number is just a label we can't act on, so it clears the flag again.
			private void ReadSmoothing(string text, ref int pos, int end)
			{
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					return;
				}
				if (IsToken(text, tokenStart, tokenEnd, "off"))
				{
					currentFlat = true;
					return;
				}
				int group;
				if (ReadInt(text, tokenStart, tokenEnd, out group))
				{
					currentFlat = group == 0;
				}
			}

			private void ReadFace(string text, ref int pos, int end, int lineNumber)
			{
				// Stream the corners straight into a fan of triangles- no per-face
				// list needed, just remember the first corner and the previous one
				ObjCorner first = new ObjCorner();
				ObjCorner previous = new ObjCorner();
				ObjMesh mesh = null;
				int corners = 0;
				int tokenStart, tokenEnd;
				while (NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					if (corners >= MaxFaceCorners)
					{
						Warn("Face with more than " + MaxFaceCorners + " vertices at line " + lineNumber);
						return;
					}
					ObjCorner corner;
					if (!ReadCorner(text, tokenStart, tokenEnd, out corner) || !InRange(corner))
					{
						Warn("Invalid vertex index in Face at line " + lineNumber);
						return;
					}
					corner.Flat = currentFlat;
					if (corners == 0)
					{
						first = corner;
					}
					else if (corners == 1)
					{
						previous = corner;
					}
					else
					{
						if (mesh == null)
						{
							mesh = CurrentMesh();
						}
						mesh.Corners.Add(first);
						mesh.Corners.Add(previous);
						mesh.Corners.Add(corner);
						previous = corner;
					}
					corners++;
				}
				if (corners < 3)
				{
					Warn("Face with fewer than three vertices at line " + lineNumber);
				}
			}

			// Reads one "v/vt/vn" corner straight out of its token range. Any part
			// may be missing except the position (that's what "1//3" means).
			private bool ReadCorner(string text, int start, int end, out ObjCorner corner)
			{
				corner = new ObjCorner { Position = -1, TexCoord = -1, Normal = -1 };
				int slash1 = -1;
				int slash2 = -1;
				for (int i = start; i < end; i++)
				{
					if (text[i] == '/')
					{
						if (slash1 < 0)
						{
							slash1 = i;
						}
						else
						{
							slash2 = i;
							break;
						}
					}
				}
				int position;
				if (!ReadIndex(text, start, slash1 >= 0 ? slash1 : end, model.Positions.Count, out position))
				{
					return false;
				}
				corner.Position = position;
				if (slash1 >= 0)
				{
					int uvEnd = slash2 >= 0 ? slash2 : end;
					if (uvEnd > slash1 + 1)
					{
						int uv;
						if (!ReadIndex(text, slash1 + 1, uvEnd, model.TexCoords.Count, out uv))
						{
							return false;
						}
						corner.TexCoord = uv;
					}
					if (slash2 >= 0 && end > slash2 + 1)
					{
						int normal;
						if (!ReadIndex(text, slash2 + 1, end, model.Normals.Count, out normal))
						{
							return false;
						}
						corner.Normal = normal;
					}
				}
				return true;
			}

			private bool InRange(ObjCorner corner)
			{
				return corner.Position >= 0 && corner.Position < model.Positions.Count &&
					corner.TexCoord >= -1 && corner.TexCoord < model.TexCoords.Count &&
					corner.Normal >= -1 && corner.Normal < model.Normals.Count;
			}

			// --- Groups: o / g / usemtl each start a fresh mesh ----------------------

			// Stash whatever triangles we have and begin a new mesh
			private void StartMesh()
			{
				FlushMesh();
				currentMesh = new ObjMesh { MaterialIndex = currentMaterial };
			}

			// The mesh faces are currently being added to- make one if there isn't one yet
			private ObjMesh CurrentMesh()
			{
				if (currentMesh == null)
				{
					currentMesh = new ObjMesh { MaterialIndex = currentMaterial };
				}
				return currentMesh;
			}

			private void FlushMesh()
			{
				if (currentMesh != null && currentMesh.TriangleCount != 0)
				{
					model.Meshes.Add(currentMesh);
				}
				currentMesh = null;
			}

			// --- Materials -------------------------------------------------------------

			private void UseMaterial(string text, ref int pos, int end)
			{
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					Warn("The usemtl command requires a material name");
					return;
				}
				string name = text.Substring(tokenStart, tokenEnd - tokenStart);
				int index;
				if (!materialsByName.TryGetValue(name, out index))
				{
					// Keep the old material rather than switching to nothing- the faces still need to go somewhere
					Error("Material " + name + " was not found.");
					return;
				}
				if (index != currentMaterial)
				{
					currentMaterial = index;
					StartMesh();
				}
			}

			private void LoadLibraries(string text, ref int pos, int end)
			{
				int tokenStart, tokenEnd;
				while (NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					string file = text.Substring(tokenStart, tokenEnd - tokenStart);
					if (Path.IsPathRooted(file))
					{
						// Nobody's absolute path survives being sent to someone else, so look next to the .obj instead
						file = Path.GetFileName(file);
					}
					string fullPath;
					try
					{
						fullPath = Path.Combine(baseDirectory, file);
					}
					catch
					{
						continue;
					}
					if (!File.Exists(fullPath))
					{
						continue;
					}
					try
					{
						ParseMaterialLibrary(File.ReadAllText(fullPath), fullPath);
					}
					catch (Exception ex)
					{
						Warn("Unable to read the material library " + file + ": " + ex.Message);
					}
				}
			}

			// A .mtl file is its own little language, so it gets its own function with
			// its own locals- no shared tokenizer state to get confused about.
			private void ParseMaterialLibrary(string text, string libraryPath)
			{
				string directory = string.Empty;
				try
				{
					directory = Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? string.Empty;
				}
				catch
				{
					// Weird path- texture lookups below will just fail softly
				}
				ObjMaterial current = null;
				int length = text.Length;
				int pos = 0;
				int lineNumber = 0;
				while (pos <= length)
				{
					int eol = text.IndexOf('\n', pos);
					if (eol < 0)
					{
						eol = length;
					}
					int end = eol;
					if (end > pos && text[end - 1] == '\r')
					{
						end--;
					}
					lineNumber++;
					if (end > pos)
					{
						current = ProcessMaterialLine(text, pos, end, lineNumber, directory, current);
					}
					pos = eol + 1;
				}
			}

			// Handles one .mtl line, returning the (possibly new) current material
			private ObjMaterial ProcessMaterialLine(string text, int start, int end, int lineNumber, string directory, ObjMaterial current)
			{
				int pos = start;
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					return current;
				}
				if (IsToken(text, tokenStart, tokenEnd, "newmtl"))
				{
					string name = NextToken(text, ref pos, end, out tokenStart, out tokenEnd)
						? text.Substring(tokenStart, tokenEnd - tokenStart)
						: string.Empty;
					int existing;
					if (materialsByName.TryGetValue(name, out existing))
					{
						Warn("Material " + name + " has been defined twice.");
						return model.Materials[existing];
					}
					ObjMaterial material = new ObjMaterial { Name = name };
					materialsByName[name] = model.Materials.Count;
					model.Materials.Add(material);
					return material;
				}
				if (current == null)
				{
					return null;
				}
				if (IsToken(text, tokenStart, tokenEnd, "kd"))
				{
					double r, g, b;
					NextMaterialDouble(text, ref pos, end, "R", current, lineNumber, out r);
					NextMaterialDouble(text, ref pos, end, "G", current, lineNumber, out g);
					NextMaterialDouble(text, ref pos, end, "B", current, lineNumber, out b);
					current.R = ToByte(r * 255.0);
					current.G = ToByte(g * 255.0);
					current.B = ToByte(b * 255.0);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "d") || IsToken(text, tokenStart, tokenEnd, "tr"))
				{
					// d is opacity, Tr is transparency- same number the other way round
					bool inverted = text[tokenStart] == 't' || text[tokenStart] == 'T';
					double a;
					NextMaterialDouble(text, ref pos, end, "alpha", current, lineNumber, out a);
					current.A = ToByte((inverted ? 1.0 - a : a) * 255.0);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "map_kd") || IsToken(text, tokenStart, tokenEnd, "map_ka"))
				{
					ReadMaterialTexture(text, ref pos, end, current, directory, false);
				}
				else if (IsToken(text, tokenStart, tokenEnd, "map_d"))
				{
					ReadMaterialTexture(text, ref pos, end, current, directory, true);
				}
				// ka, ks, ke, map_ns, illum and the rest: the renderer doesn't use them, so skip
				return current;
			}

			private bool NextMaterialDouble(string text, ref int pos, int end, string what, ObjMaterial material, int lineNumber, out double value)
			{
				int tokenStart, tokenEnd;
				if (!NextToken(text, ref pos, end, out tokenStart, out tokenEnd) ||
					!ReadDouble(text, tokenStart, tokenEnd, out value))
				{
					Warn("Invalid " + what + " value in the definition of material " + material.Name + " at line " + lineNumber);
					value = 1.0;
					return false;
				}
				return true;
			}

			private void ReadMaterialTexture(string text, ref int pos, int end, ObjMaterial material, string directory, bool transparency)
			{
				// Filenames with spaces get split into several tokens and nobody escapes them
				// consistently, so remember every token and try the last one, then the whole lot joined
				List<string> tokens = new List<string>();
				int tokenStart, tokenEnd;
				while (NextToken(text, ref pos, end, out tokenStart, out tokenEnd))
				{
					tokens.Add(text.Substring(tokenStart, tokenEnd - tokenStart));
				}
				if (tokens.Count == 0)
				{
					return;
				}
				string path = ResolveTexturePath(tokens[tokens.Count - 1], directory) ??
					(tokens.Count > 1 ? ResolveTexturePath(string.Join(" ", tokens.ToArray()), directory) : null);
				if (path == null)
				{
					Warn("The texture file " + tokens[tokens.Count - 1] + " for material " + material.Name + " was not found.");
					return;
				}
				if (transparency)
				{
					material.TransparencyTexture = path;
				}
				else
				{
					material.DaytimeTexture = path;
				}
			}

			private static string ResolveTexturePath(string file, string directory)
			{
				if (string.IsNullOrEmpty(file))
				{
					return null;
				}
				if (Path.IsPathRooted(file))
				{
					// Same story as mtllib above- an absolute path from someone else's PC is useless, try the filename alone
					file = Path.GetFileName(file);
				}
				try
				{
					string combined = Path.Combine(directory, file);
					return File.Exists(combined) ? combined : null;
				}
				catch
				{
					return null;
				}
			}

			private static byte ToByte(double value)
			{
				if (value < 0) return 0;
				if (value > 255) return 255;
				return (byte)(value + 0.5);
			}

			// --- Exporter sniffing ------------------------------------------------------
			// SketchUp likes to leave "# File units = ..." and a "# SketchUp" tag in its files;
			// Blender and BlockBench leave their own tags. We use them to work out whose
			// co-ordinate quirks we need to undo later.

			private void ReadComment(string comment)
			{
				string lower = comment.ToLowerInvariant();
				if (lower.Contains("sketchup"))
				{
					SetExporter(ObjExporter.SketchUp);
				}
				else if (lower.Contains("blender"))
				{
					SetExporter(ObjExporter.Blender);
				}
				else if (lower.Contains("blockbench"))
				{
					SetExporter(ObjExporter.BlockBench);
				}
				if (lower.IndexOf("file units", StringComparison.Ordinal) >= 0)
				{
					int equals = comment.IndexOf('=');
					if (equals >= 0 && equals < comment.Length - 1)
					{
						scale = UnitsToScale(comment.Substring(equals + 1).Trim().ToLowerInvariant());
					}
				}
			}

			private void SetExporter(ObjExporter exporter)
			{
				model.Exporter = exporter;
				// All three of these come out mirrored, so the bridge flips X and reverses winding for them
				model.LeftHanded = true;
				model.FlipUvY = true;
				// SketchUp mirrors the textures horizontally as well, for good measure
				model.FlipUvX = exporter == ObjExporter.SketchUp;
			}

			// SketchUp's "# File units = ..." comment tells us what a unit means- turn the word into a multiplier
			private double UnitsToScale(string units)
			{
				switch (units)
				{
					case "millimeters":
					case "millimetres":
					case "mm":
						return 0.001;
					case "centimeters":
					case "centimetres":
					case "cm":
						return 0.01;
					case "meters":
					case "metres":
					case "m":
						return 1.0;
					case "inches":
					case "inch":
					case "in":
						return 0.0254;
					case "feet":
					case "foot":
					case "ft":
						return 0.3048;
					default:
						Warn("Unrecognised units value " + units);
						return 1.0;
				}
			}
		}
	}
}
