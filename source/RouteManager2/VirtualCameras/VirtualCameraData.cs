using OpenBveApi.Math;

namespace RouteManager2.VirtualCameras
{
	/// <summary>Defines the activation mode for a virtual camera</summary>
	public enum VirtualCameraActiveMode
	{
		/// <summary>Camera feed is always rendered</summary>
		Always = 0,
		/// <summary>Camera feed is only active when a train is stopped at a station</summary>
		StopOnly = 1,
		/// <summary>Camera feed is active when a train is within ActivationDistance</summary>
		Distance = 2
	}

	/// <summary>Stores the runtime data for a virtual camera defined in the route</summary>
	public class VirtualCameraData
	{
		/// <summary>The unique index of this camera (matches CAM_X receiver index)</summary>
		public int Index;
		/// <summary>The world position of the camera</summary>
		public Vector3 Position;
		/// <summary>The yaw rotation in radians</summary>
		public double Yaw;
		/// <summary>The pitch rotation in radians</summary>
		public double Pitch;
		/// <summary>The roll rotation in radians</summary>
		public double Roll;
		/// <summary>The vertical field of view in radians</summary>
		public double FieldOfView;
		/// <summary>The render texture width in pixels</summary>
		public int RenderWidth;
		/// <summary>The render texture height in pixels</summary>
		public int RenderHeight;
		/// <summary>The activation mode for this camera</summary>
		public VirtualCameraActiveMode ActiveMode;
		/// <summary>The activation distance in meters (only used when ActiveMode is Distance)</summary>
		public double ActivationDistance;
		/// <summary>An optional label for this camera</summary>
		public string Label;
	}
}
