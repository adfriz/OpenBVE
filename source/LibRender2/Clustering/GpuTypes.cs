using System.Runtime.InteropServices;
using OpenTK;
using OTKVector4 = OpenTK.Vector4;

namespace LibRender2.Clustering
{
	/// <summary>
	/// GPU-ready representation of a dynamic light source (std430 compatible).
	/// Maps directly from <see cref="OpenBveApi.Objects.SceneLight"/>.
	/// Total size: 48 bytes (std430 aligned).
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct GpuLight
	{
		/// <summary>Light position in view space (xyz) + Range (w)</summary>
		public OTKVector4 PositionAndRange;    // 16 bytes — offset 0

		/// <summary>Light color (rgb) + Intensity/Power multiplier (w)</summary>
		public OTKVector4 ColorAndIntensity;   // 16 bytes — offset 16

		/// <summary>Spot direction in view space (xyz) + SpotCutoff cosine (w)</summary>
		public OTKVector4 DirectionAndCutoff;  // 16 bytes — offset 32

		/// <summary>x=Type (0=Point,1=Spot), y=SoftFalloff, z=Softness, w=RangeSquared packed</summary>
		public OTKVector4 Params;              // 16 bytes — offset 48

		// Total: 64 bytes per GpuLight (4 × vec4)
	}

	/// <summary>
	/// Per-cluster data: offset into global light index list + number of lights in cluster.
	/// Size: 8 bytes (uvec2 in std430).
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct GpuCluster
	{
		/// <summary>Offset into the global LightIndex SSBO</summary>
		public uint LightOffset;    // 4 bytes

		/// <summary>Number of lights affecting this cluster</summary>
		public uint LightCount;     // 4 bytes
	}

	/// <summary>
	/// View-space AABB for a cluster tile, used by the compute shader for sphere-AABB testing.
	/// Size: 32 bytes (2 × vec4, padded for std430).
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct GpuClusterAABB
	{
		/// <summary>AABB minimum corner (xyz) + padding (w)</summary>
		public OTKVector4 Min;         // 16 bytes

		/// <summary>AABB maximum corner (xyz) + padding (w)</summary>
		public OTKVector4 Max;         // 16 bytes
	}
}
