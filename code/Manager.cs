using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jumpy
{
	public sealed class Manager : Component, Component.INetworkListener
	{
		public static Manager Instance { get; private set; }

		public const int TileSize = 96;
		private const float recentSpawnMemory = 5f;

		private static readonly string[] botNames =
		{
			"Hopper", "Ribbit", "Croak", "Lily", "Tad",
			"Splash", "Bouncer", "Wart", "Puddles", "Bulls-eye"
		};

		[Property] public GameObject PlayerPrefab { get; set; }
		[Property] public GameObject TilePrefab { get; set; }

		[Property] public GameObject FencePrefab { get; set; }

		[Property] public GameObject CarPrefab { get; set; }
		[Property] public GameObject RoadPrefab { get; set; }
		[Property] public GameObject BigRoadPrefab { get; set; }

		[Property] public GameObject LogPrefab { get; set; }
		[Property] public GameObject LillyPrefab { get; set; }
		[Property] public GameObject TreePrefab { get; set; }
		[Property] public GameObject RockPrefab { get; set; }
		[Property] public GameObject PebblesPrefab { get; set; }

		[Property] public GameObject SpawnerPrefab { get; set; }

		[Property, Group( "Bots" )] public int BotCount { get; set; } = 1;

		[Property, Group( "Start Area" )] public int StartAreaWidth { get; set; } = 8;
		[Property, Group( "Start Area" )] public int StartAreaDepth { get; set; } = 3;
		[Property, Group( "Start Area" )] public float CountdownSeconds { get; set; } = 5f;

		[Property, Group( "Rounds" )] public float RestartDelaySeconds { get; set; } = 10f;

		// Measured in tiles. Width spans side-to-side (Y axis); height runs from start to finish (X axis).
		[Property, Group( "World" )] public int WorldWidth { get; set; } = 28;
		[Property, Group( "World" )] public int WorldHeight { get; set; } = 96;

		// Safe bands spaced evenly along the run. Reaching one becomes your respawn point, so
		// death only costs the segment since your last checkpoint.
		[Property, Group( "World" )] public int CheckpointCount { get; set; } = 2;

		[Sync] public bool IsGameActive { get; set; } = false;
		[Sync] public bool IsGameOver { get; set; } = false;
		[Sync] public float WinTilePosition { get; set; } = 0;
		[Sync] public int CountdownRemaining { get; set; } = 0;

		[Sync] public TimeUntil NextRoundStart { get; set; }

		// Start-area pen bounds; frogs are confined inside these until the countdown ends.
		[Sync] public float StartAreaMinX { get; set; }
		[Sync] public float StartAreaMaxX { get; set; }
		[Sync] public float StartAreaMinY { get; set; }
		[Sync] public float StartAreaMaxY { get; set; }

		// Every walkable tile on every checkpoint band, so a checkpoint respawn picks a random
		// unoccupied spot on the reached row instead of always the centre.
		[Sync] public List<Vector3> CheckpointSpawns { get; set; } = new();

		// The X threshold of each band, ascending. Drives reach detection, the HUD ticks, and
		// which row a frog respawns on (indexed by Frog.CheckpointIndex).
		[Sync] public List<float> CheckpointXs { get; set; } = new();

		// Spawn spots handed out recently. Respawn RPCs haven't round-tripped during a
		// round-start burst, so this is what keeps two frogs from being dealt the same spot.
		private readonly List<(Frog frog, Vector3 position, float time)> recentSpawns = new();

		public float WorldWidthY => WorldWidth * TileSize;

		protected override async Task OnLoad()
		{
			if ( Scene.IsEditor )
				return;

			if ( !Networking.IsActive )
			{
				LoadingScreen.Title = "Creating Lobby";
				await Task.DelayRealtimeSeconds( 0.1f );
				Networking.CreateLobby( new Sandbox.Network.LobbyConfig
				{
					MaxPlayers = 8,
					Name = $"{Connection.Local.DisplayName}'s Game"
				} );
			}
		}

		protected override void OnAwake()
		{
			Instance = this;
		}

		protected override void OnStart()
		{
			Mouse.Visibility = MouseVisibility.Hidden;
			_ = StartNewGame();
		}

		protected override void OnUpdate()
		{
			if ( !Networking.IsHost || !IsGameActive )
				return;

			// CheckpointIndex is synced and monotonic, so it's up to date on the owning client by
			// the time its delayed respawn fires.
			foreach ( Frog frog in Scene.GetAllComponents<Frog>() )
			{
				for ( int i = CheckpointXs.Count - 1; i > frog.CheckpointIndex; i-- )
				{
					if ( frog.WorldPosition.x >= CheckpointXs[i] )
					{
						frog.CheckpointIndex = i;
						break;
					}
				}
			}

			// The flag is synced, so proxy clients show the winner at 100% instead of trusting
			// their lagging interpolated position, which reads just short of the finish.
			var finishers = Scene.GetAllComponents<Frog>().Where( frog => frog.WorldPosition.x >= WinTilePosition ).ToList();
			if ( finishers.Count > 0 )
			{
				foreach ( Frog frog in finishers )
					frog.HasFinished = true;

				_ = EndGame();
			}
		}

		public void OnActive( Connection connection )
		{
			Log.Info( $"Player '{connection.DisplayName}' has joined the game" );

			if ( PlayerPrefab is null )
				return;

			var player = PlayerPrefab.Clone( new Transform( Vector3.Up * 100 ), name: $"Player - {connection.DisplayName}" );
			player.NetworkSpawn( connection );

			RespawnFrog( player.Components.Get<Frog>() );
		}

		public async Task StartNewGame()
		{
			if ( !Networking.IsHost )
				return;

			IsGameActive = false;
			IsGameOver = false;
			CountdownRemaining = 0;

			GenerateWorld();
			EnsureBots();
			RespawnAllFrogs();

			// Let the frogs settle onto the start platform before the clock starts.
			await Task.DelayRealtimeSeconds( 1.0f );

			for ( int seconds = (int)float.Ceiling( CountdownSeconds ); seconds > 0; seconds-- )
			{
				CountdownRemaining = seconds;
				await Task.DelayRealtimeSeconds( 1.0f );
			}

			CountdownRemaining = 0;
			IsGameActive = true;
		}

		public async Task EndGame()
		{
			if ( !Networking.IsHost )
				return;

			IsGameActive = false;
			IsGameOver = true;
			NextRoundStart = RestartDelaySeconds;

			await Task.DelayRealtimeSeconds( RestartDelaySeconds );

			_ = StartNewGame();
		}

		public void RespawnFrog( Frog frog )
		{
			bool hasCheckpoint = frog.CheckpointIndex >= 0 && frog.CheckpointIndex < CheckpointXs.Count;

			frog.Respawn( hasCheckpoint ? GetCheckpointSpawn( frog ) : GetSpawnPoint( frog ) );
		}

		public Vector3 GetSpawnPoint( Frog frog )
		{
			var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToList();
			if ( spawnPoints.Count == 0 )
				return Vector3.Zero;

			ForgetStaleSpawns( frog );

			var free = spawnPoints.Where( sp => !IsSpawnPointOccupied( sp.WorldPosition, frog ) ).ToList();
			Vector3 chosen = Game.Random.FromList( free.Count > 0 ? free : spawnPoints ).WorldPosition;

			recentSpawns.Add( (frog, chosen, Time.Now) );

			return chosen;
		}

		public Vector3 GetCheckpointSpawn( Frog frog )
		{
			float rowX = CheckpointXs[frog.CheckpointIndex];

			var candidates = CheckpointSpawns.Where( p => float.Abs( p.x - rowX ) < 1f ).ToList();
			if ( candidates.Count == 0 )
				return new Vector3( rowX, 0, 40 );

			ForgetStaleSpawns( frog );

			var free = candidates.Where( p => !IsSpawnPointOccupied( p, frog ) ).ToList();
			Vector3 chosen = Game.Random.FromList( free.Count > 0 ? free : candidates );

			recentSpawns.Add( (frog, chosen, Time.Now) );

			return chosen;
		}

		public bool IsWithinStartArea( Vector3 worldPos )
		{
			return worldPos.x >= StartAreaMinX && worldPos.x <= StartAreaMaxX
				&& worldPos.y >= StartAreaMinY && worldPos.y <= StartAreaMaxY;
		}

		private void ClearWorld()
		{
			foreach ( GameObject child in GameObject.Children )
				child.Destroy();
		}

		private void GenerateWorld()
		{
			ClearWorld();
			CheckpointSpawns.Clear();
			CheckpointXs.Clear();

			int roadFreq = Game.Random.Int( 8 );
			int bigRoadFreq = Game.Random.Int( 12 );
			int riverFreq = Game.Random.Int( 12 );
			int lillyFreq = Game.Random.Int( 16 );

			int halfWidth = WorldWidth / 2;

			int areaDepth = int.Clamp( StartAreaDepth, 1, WorldHeight - 1 );
			int areaWidth = int.Clamp( StartAreaWidth, 1, WorldWidth );
			int startColumn = (WorldWidth - areaWidth) / 2;

			// Half-tile slack so frogs can stand on the edge tiles.
			StartAreaMinX = -TileSize * 0.5f;
			StartAreaMaxX = (areaDepth - 1) * TileSize + TileSize * 0.5f;
			StartAreaMinY = (startColumn - halfWidth) * TileSize - TileSize * 0.5f;
			StartAreaMaxY = (startColumn + areaWidth - 1 - halfWidth) * TileSize + TileSize * 0.5f;

			// Evenly-spaced rows (by progress fraction i/(N+1)) reserved for checkpoint bands,
			// snapped to a tile row and kept clear of the start pen and win row.
			float finishX = (WorldHeight - 1) * TileSize;
			var checkpointRows = new HashSet<int>();
			for ( int i = 0; i < CheckpointCount; i++ )
			{
				float frac = (i + 1f) / (CheckpointCount + 1f);
				int row = (int)float.Round( (StartAreaMaxX + frac * (finishX - StartAreaMaxX)) / TileSize );
				checkpointRows.Add( int.Clamp( row, areaDepth, WorldHeight - 2 ) );
			}

			for ( int x = 0; x < WorldHeight; x++ )
			{
				roadFreq--;
				bigRoadFreq--;
				riverFreq--;
				lillyFreq--;

				bool rowWasRoad = false;

				for ( int y = 0; y < WorldWidth; y++ )
				{
					Vector3 currentPosition = new Vector3( x * TileSize, y * TileSize - TileSize * halfWidth, Sandbox.Utility.Noise.Perlin( x * 32, y * 32 ) * 8 );

					if ( x < areaDepth )
					{
						if ( y >= startColumn && y < startColumn + areaWidth )
						{
							CreateTile( currentPosition, new Color( 0.75f, 1, 0.75f ) );
							CreateSpawnPoint( currentPosition + Vector3.Up * 32 );

							if ( x == areaDepth - 1 )
								CreateFence( currentPosition.WithX( StartAreaMaxX - 8.0f ) + Vector3.Up * 32.0f );
						}
						continue;
					}

					if ( x >= WorldHeight - 1 )
					{
						CreateTile( currentPosition, new Color( 1, 0.75f, 0.75f ) );
						WinTilePosition = currentPosition.x;
						continue;
					}

					// Handled before the lane rolls so no hazard ever spawns on a checkpoint band
					// and its X is never mutated.
					if ( checkpointRows.Contains( x ) )
					{
						CreateTile( currentPosition, new Color( 0.55f, 0.8f, 1f ) );
						CheckpointSpawns.Add( currentPosition + Vector3.Up * 32 );
						if ( y == 0 )
							CheckpointXs.Add( currentPosition.x );
						continue;
					}

					// A multi-row lane runs x++ internally, so it must not start within its span of
					// a reserved checkpoint row or it would leap over it and swallow the band.
					bool checkpointAhead = checkpointRows.Contains( x + 1 )
						|| checkpointRows.Contains( x + 2 )
						|| checkpointRows.Contains( x + 3 );

					// Jagged edges
					if ( (y <= 1 || y >= WorldWidth - 2) && Game.Random.Int( 1 ) == 1 )
						continue;

					if ( riverFreq <= 0 && !checkpointAhead )
					{
						for ( int lane = 0; lane < 2; lane++, x++ )
							SpawnRiverLane( x, lane == 1 );

						riverFreq = Game.Random.Int( 12 );
						continue;
					}

					if ( roadFreq <= 0 && !rowWasRoad && !checkpointAhead )
					{
						rowWasRoad = true;
						CreateRoad( RoadPrefab, x * TileSize + 48 );

						for ( int lane = 0; lane < 2; lane++, x++ )
							SpawnRoadLane( x, lane == 1 );

						roadFreq = Game.Random.Int( 24 );
						continue;
					}

					if ( bigRoadFreq <= 0 && !rowWasRoad && !checkpointAhead )
					{
						rowWasRoad = true;
						CreateRoad( BigRoadPrefab, x * TileSize + 96 );

						// Every lane of a big road runs the same way.
						bool flipped = Game.Random.Int( 1 ) == 1;
						for ( int lane = 0; lane < 3; lane++, x++ )
							SpawnRoadLane( x, flipped );

						bigRoadFreq = Game.Random.Int( 32 );
						continue;
					}

					if ( lillyFreq <= 0 )
					{
						for ( int k = 0; k < WorldWidth; k++ )
						{
							if ( Game.Random.Int( 1 ) == 1 )
								continue;

							CreateDecoration( LillyPrefab, new Vector3( x * TileSize, k * TileSize - TileSize * halfWidth, 12 ), 1f );
						}
						lillyFreq = Game.Random.Int( 16 );
						break;
					}

					CreateTile( currentPosition, Color.White );

					if ( Game.Random.Int( 28 ) == 28 )
						CreateDecoration( Game.Random.FromArray( new[] { TreePrefab, RockPrefab } ), new Vector3( currentPosition, 32 ), Game.Random.Float( 0.8f, 1.2f ) );

					if ( Game.Random.Int( 24 ) == 24 )
						CreateDecoration( PebblesPrefab, new Vector3( currentPosition, 32 ), Game.Random.Float( 0.8f, 1.2f ) );
				}
			}
		}

		private void CreateTile( Vector3 position, Color color )
		{
			var root = TilePrefab.Clone( position );
			root.Components.Get<ModelRenderer>().Tint = color;
			root.SetParent( GameObject );
			root.NetworkSpawn();
		}

		private void CreateFence( Vector3 position )
		{
			if ( !FencePrefab.IsValid() )
				return;

			GameObject fence = FencePrefab.Clone( position );
			fence.SetParent( GameObject );
			fence.NetworkSpawn();
		}

		private void CreateSpawnPoint( Vector3 position )
		{
			GameObject spawnPoint = new GameObject( true, "SpawnPoint" );
			spawnPoint.Components.Create<SpawnPoint>();
			spawnPoint.WorldPosition = position;
			spawnPoint.SetParent( GameObject );
			spawnPoint.NetworkSpawn();
		}

		private void CreateDecoration( GameObject prefab, Vector3 position, float scale )
		{
			var root = prefab.Clone( position, Rotation.FromYaw( Game.Random.Float( 0, 360 ) ), scale );
			root.SetParent( GameObject );
			root.NetworkSpawn();
		}

		private void CreateRoad( GameObject prefab, float x )
		{
			var road = prefab.Clone( new Vector3( x, 0, 0 ) );
			road.SetParent( GameObject );
			road.NetworkSpawn();
		}

		private void SpawnRiverLane( int row, bool flipped )
			=> SpawnLane( row, 0, Rotation.Identity, LogPrefab, 3, 9, flipped );

		private void SpawnRoadLane( int row, bool flipped )
			=> SpawnLane( row, 30, Rotation.FromYaw( flipped ? 90 : -90 ), CarPrefab, 0.9f, 5, flipped );

		// Spawners sit off the far edge of the world and feed entities across it.
		private void SpawnLane( int row, float height, Rotation rotation, GameObject prefab, float delayMin, float delayMax, bool flipped )
		{
			float y = WorldWidthY * (flipped ? -1 : 1);
			GameObject root = SpawnerPrefab.Clone( new Vector3( row * TileSize, y, height ), rotation );
			root.SetParent( GameObject );

			_ = root.Components.Get<EntitySpawner>().SpawnEntities( prefab, delayMin, delayMax, flipped );
		}

		// Bots live at the scene root (not as Manager children) so they survive world regeneration.
		private void EnsureBots()
		{
			if ( !Networking.IsHost )
				return;

			var bots = Scene.GetAllComponents<Frog>().Where( f => f.IsBot ).ToList();
			int target = int.Max( 0, BotCount );

			for ( int i = bots.Count; i < target; i++ )
				SpawnBot( i );

			for ( int i = target; i < bots.Count; i++ )
				bots[i].GameObject.Destroy();
		}

		private void SpawnBot( int index )
		{
			if ( PlayerPrefab is null )
				return;

			string name = GetBotName( index );
			var bot = PlayerPrefab.Clone( new Transform( Vector3.Up * 100 ), name: $"Bot - {name}" );

			Frog frog = bot.Components.Get<Frog>();
			frog.IsBot = true;
			frog.BotName = name;

			// No owning connection — host-authoritative so every client sees the same bot.
			bot.NetworkSpawn();
			RespawnFrog( frog );
		}

		private static string GetBotName( int index )
		{
			return index < botNames.Length ? botNames[index] : $"Frog {index + 1}";
		}

		private void RespawnAllFrogs()
		{
			foreach ( Frog frog in Scene.GetAllComponents<Frog>() )
			{
				// A fresh round wipes checkpoint progress so everyone starts back in the pen.
				frog.CheckpointIndex = -1;
				RespawnFrog( frog );
			}
		}

		private void ForgetStaleSpawns( Frog frog )
		{
			recentSpawns.RemoveAll( entry => !entry.frog.IsValid() || entry.frog == frog || Time.Now - entry.time > recentSpawnMemory );
		}

		private bool IsSpawnPointOccupied( Vector3 spot, Frog ignore )
		{
			float radius = TileSize * 0.5f;

			foreach ( var entry in recentSpawns )
			{
				if ( (entry.position - spot).WithZ( 0 ).Length < radius )
					return true;
			}

			foreach ( Frog other in Scene.GetAllComponents<Frog>() )
			{
				if ( other == ignore || other.IsDead )
					continue;

				if ( (other.WorldPosition - spot).WithZ( 0 ).Length < radius )
					return true;
			}

			return false;
		}
	}
}
