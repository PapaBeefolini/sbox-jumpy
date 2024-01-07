using Sandbox;
using Sandbox.UI;

namespace Jumpy.UI
{
	public partial class GameUI : Sandbox.HudEntity<RootPanel>
	{
		public GameUI()
		{
			if ( Game.IsClient )
			{
				RootPanel.StyleSheet.Load( "/ui/GameUI.scss" );

				ChatBox chat = RootPanel.AddChild<ChatBox>();
				RootPanel.AddChild<VoiceList>();
				RootPanel.AddChild<Score>();

				var text = RootPanel.AddChild<Label>();
				text.Text = "Jumpy";
			}
		}
	}
}
