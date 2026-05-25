using OpenBveApi.Math;

namespace LibRenderNext.Pipeline
{
	public class RenderContext
	{
		public BaseRenderer Renderer { get; }
		public Matrix4D ViewMatrix { get; set; }
		public Matrix4D ProjectionMatrix { get; set; }

		public RenderContext(BaseRenderer renderer)
		{
			Renderer = renderer;
		}
	}
}
