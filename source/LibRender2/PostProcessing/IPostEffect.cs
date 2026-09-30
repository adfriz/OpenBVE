namespace LibRender2.PostProcessing
{
	/// One post step. The chain only decides order.
	public interface IPostEffect
	{
		/// Takes the previous texture, draws fullscreen, returns the texture for the next step.
		int Apply(int inputTexture);
	}
}
