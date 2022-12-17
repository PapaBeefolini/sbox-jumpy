using Sandbox.UI;

namespace Jumpy.UI
{
	public partial class GameUI : Sandbox.HudEntity<RootPanel>
	{
		public GameUI()
		{
			if ( IsClient )
			{
				RootPanel.StyleSheet.Load( "/ui/GameUI.scss" );

				ChatBox chat = RootPanel.AddChild<ChatBox>();
				RootPanel.AddChild<VoiceList>();
				RootPanel.AddChild<Score>();
			}
		}
	}
}
