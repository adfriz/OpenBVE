namespace LibRenderNext.Pipeline
{
	public interface IRenderPass
	{
		void Execute(RenderContext context);
	}
}
