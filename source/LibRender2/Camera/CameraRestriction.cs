using OpenBveApi.Math;

namespace LibRender2.Cameras
{
	public struct CameraRestriction
	{
		/// <summary>Absolute bottom-left corner.</summary>
		public Vector3 AbsoluteBottomLeft;
		/// <summary>Absolute top-right corner.</summary>
		public Vector3 AbsoluteTopRight;
		/// <summary>Relative bottom-left corner.</summary>
		public Vector3 BottomLeft;
		/// <summary>Relative top-right corner.</summary>
		public Vector3 TopRight;

		/// <summary>Rotates the restriction 180 degrees.</summary>
		public void Reverse()
		{
			AbsoluteBottomLeft.Rotate(Vector3.Forward, 3.14159);
			AbsoluteTopRight.Rotate(Vector3.Forward, 3.14159);
		}
	}
}
