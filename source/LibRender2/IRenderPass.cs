using OpenBveApi.World;

namespace LibRender2
{
	public interface IRenderPass
	{
		string Name { get; }
		bool Enabled { get; set; }
		void Render(BaseRenderer renderer);
	}
}
