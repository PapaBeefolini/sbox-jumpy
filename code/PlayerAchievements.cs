using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Jumpy
{
	// Idents and stat names have to match the achievements set up on sbox.game for brick.jumpy
	// exactly. Everything here acts on whoever is running this client, so only ever call it for the
	// local player's own frog — never for a bot or a proxy.
	public static class PlayerAchievements
	{
		public const string FirstWin = "first_win";
		public const string Flawless = "flawless";
		public const string Comeback = "comeback";
		public const string Checkpoint = "checkpoint";
		public const string DeathAll = "death_all";
		public const string EarlyExit = "early_exit";
		public const string LogRider = "log_rider";
		public const string TotemPole = "totem_pole";

		// Stat-driven achievements (wins_10, deaths_100, hops_1000, ...) unlock on the backend once the
		// summed stat reaches their goal, so the game only ever counts these up.
		public const string WinsStat = "wins";
		public const string DeathsStat = "deaths";
		public const string HopsStat = "hops";
		public const string RoundsPlayedStat = "rounds_played";

		private static readonly string[] deathIdents = { "death_car", "death_train", "death_water", "death_drift" };

		// Unlocked this session, or found already unlocked. Saves re-sending an unlock a per-frame check
		// keeps qualifying for, and covers the gap before the backend reports a fresh unlock back.
		private static readonly HashSet<string> unlocked = new();

		public static string ForDeath( DeathType deathType ) => deathType switch
		{
			DeathType.Car => "death_car",
			DeathType.Train => "death_train",
			DeathType.Drift => "death_drift",
			_ => "death_water"
		};

		public static bool IsUnlocked( string ident )
		{
			if ( unlocked.Contains( ident ) )
				return true;

			if ( !Sandbox.Services.Achievements.All.Any( achievement => achievement.Name == ident && achievement.IsUnlocked ) )
				return false;

			unlocked.Add( ident );
			return true;
		}

		public static void Unlock( string ident )
		{
			if ( IsUnlocked( ident ) )
				return;

			unlocked.Add( ident );
			Sandbox.Services.Achievements.Unlock( ident );
		}

		public static void Count( string stat )
		{
			Sandbox.Services.Stats.Increment( stat, 1 );
		}

		public static void RecordDeath( DeathType deathType )
		{
			Count( DeathsStat );
			Unlock( ForDeath( deathType ) );

			if ( deathIdents.All( IsUnlocked ) )
				Unlock( DeathAll );
		}
	}
}
