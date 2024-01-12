using Sandbox;
using Sandbox.UI;

namespace Jumpy.UI
{
	public partial class GameUI : Sandbox.HudEntity<RootPanel>
	{
		public Label GameEndText { get; set; }

		public GameUI()
		{
			if ( Game.IsClient )
			{
				RootPanel.StyleSheet.Load( "/code/ui/GameUI.scss" );
				RootPanel.StyleSheet.Load( "/code/ui/mainmenu/mainmenu.scss" );
				RootPanel.StyleSheet.Load( "/code/ui/mainmenu/loadingpanel.scss" );

				ChatBox chat = RootPanel.AddChild<ChatBox>();
				RootPanel.AddChild<VoiceList>();

				GameEndText = RootPanel.AddChild<Label>();
			}
		}

		[GameEvent.Client.Frame]
		public void Frame()
		{
			JumpyGame current = (JumpyGame.Current as JumpyGame);

			if ( current.IsGameOver )
				GameEndText.Text = "Game Over!";
			else
				GameEndText.Text = "";
		}
	}
}
