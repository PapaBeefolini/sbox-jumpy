using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Jumpy.UI
{
	public class Score : Panel
	{
		public Score()
		{
			Add.Image( "/ui/frog.png" );
		}

		public override void Tick()
		{
			PanelTransform pt = new PanelTransform();
			pt.AddRotation( 0, 0, System.MathF.Sin( 12 * Time.Now ) * 8 );
			pt.AddTranslateY( System.MathF.Sin( 24 * Time.Now ) * 4 );
			Style.Transform = pt;
			Style.Dirty();
		}
	}
}
