using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Jumpy
{
	public sealed class GameManager : Component, Component.INetworkListener
	{
		[Property] public GameObject PlayerPrefab { get; set; }
		[Property] public GameObject TilePrefab { get; set; }

		[Property] public GameObject CarPrefab { get; set; }
		[Property] public GameObject RoadPrefab { get; set; }
		[Property] public GameObject BigRoadPrefab { get; set; }

		[Property] public GameObject LogPrefab { get; set; }
		[Property] public GameObject LillyPrefab { get; set; }
		[Property] public GameObject TreePrefab { get; set; }
		[Property] public GameObject RockPrefab { get; set; }
		[Property] public GameObject PebblesPrefab { get; set; }

		public bool IsGameActive { get; set; } = false;
		public bool IsGameOver { get; set; } = false;

		private int worldLength = 64;
		private int worldWidth = 28;
		private int tileSize = 96;
		private int winTilePosition = 0;
		private bool lastGenerationWasRoad = false;


		protected override async Task OnLoad()
		{
			if ( Scene.IsEditor )
				return;

			if ( !GameNetworkSystem.IsActive )
			{
				LoadingScreen.Title = "Creating Lobby";
				await Task.DelayRealtimeSeconds( 0.1f );
				GameNetworkSystem.CreateLobby();
			}
		}


		public void OnActive( Connection connection )
		{
			Log.Info( $"Player '{connection.DisplayName}' has joined the game" );

			if ( PlayerPrefab is null )
				return;

			var player = PlayerPrefab.Clone( new Transform(), name: $"Player - {connection.DisplayName}" );
			player.Components.Get<Frog>().Jumpy = this;
			player.NetworkSpawn( connection );
		}


		protected override void OnStart()
		{
			_ = StartNewGame();
		}


		protected override void OnUpdate()
		{
			if ( !IsGameActive )
				return;
		}


		public async Task StartNewGame()
		{
			if ( !Networking.IsHost )
				return;

			IsGameActive = false;
			IsGameOver = false;

			GenerateWorld();
			RespawnAllFrogs();

			await Task.DelaySeconds( 1.5f );

			IsGameActive = true;
		}


		public async Task EndGame()
		{
			if ( !Networking.IsHost )
				return;

			IsGameActive = false;
			IsGameOver = true;

			await Task.DelaySeconds( 3.0f );

			_ = StartNewGame();
		}


		private void ClearWorld()
		{
			foreach ( GameObject child in GameObject.Children )
				child.Destroy();
		}


		private void GenerateWorld()
		{
			ClearWorld();

			int roadFreq = Game.Random.Int( 8 );
			int bigRoadFreq = Game.Random.Int( 12 );
			int riverFreq = Game.Random.Int( 12 );
			int lillyFreq = Game.Random.Int( 16 );

			for ( int x = 0; x < worldLength; x++ )
			{
				roadFreq--;
				bigRoadFreq--;
				riverFreq--;
				lillyFreq--;

				lastGenerationWasRoad = false;

				for ( int y = 0; y < worldWidth; y++ )
				{
					Vector3 currentPosition = new Vector3( x * tileSize, y * tileSize - tileSize * 14, Sandbox.Utility.Noise.Perlin( x * 32, y * 32 ) * 8 );

					// Starter tiles at first row
					if ( x == 0 )
					{
						CreateTile( currentPosition, new Color( 0.75f, 1, 0.75f ) );
						GameObject spawnPoint = new GameObject( true, "SpawnPoint" );
						spawnPoint.Components.Create<SpawnPoint>();
						spawnPoint.Transform.Position = currentPosition + Vector3.Up * 32;
						spawnPoint.SetParent( GameObject );
						spawnPoint.NetworkSpawn();
						continue;
					}

					// Win tiles at last row
					if ( x >= worldLength - 1 )
					{
						CreateTile( currentPosition, new Color( 1, 0.75f, 0.75f ) );
						winTilePosition = (int)currentPosition.x;
						continue;
					}

					// Throw in some jagged edges
					if ( y <= 1 || y >= 26 )
					{
						if ( Game.Random.Int( 1 ) == 1 )
							continue;
					}

					// Rivers
					if ( riverFreq <= 0 )
					{
						for ( int i = 0; i < 2; i++ )
						{
							float offset = tileSize * 28;
							bool flipped = false;
							if ( i == 1 )
							{
								offset = -offset;
								flipped = true;
							}
							EntitySpawner spawner = CreateEntitySpawner();
							spawner.Transform.Position = new Vector3( x * tileSize, offset, 0 );
							_ = spawner.SpawnEntities( LogPrefab, 3, 9, flipped );
							x++;
						}
						riverFreq = Game.Random.Int( 12 );
						continue;
					}

					// Roads
					if ( roadFreq <= 0 && !lastGenerationWasRoad )
					{
						lastGenerationWasRoad = true;

						var road = RoadPrefab.Clone();
						road.Transform.Position = new Vector3( x * tileSize + 48, 0, 0 );
						road.SetParent( GameObject );
						road.NetworkSpawn();
						for ( int i = 0; i < 2; i++ )
						{
							float offset = tileSize * 28;
							float angle = -90;
							bool flipped = false;
							if ( i == 1 )
							{
								offset = -offset;
								angle = 90;
								flipped = true;
							}
							EntitySpawner spawner = CreateEntitySpawner();
							spawner.Transform.Position = new Vector3( x * tileSize, offset, 30 );
							spawner.Transform.Rotation = Rotation.FromYaw( angle );
							_ = spawner.SpawnEntities( CarPrefab, 0.9f, 5, flipped );
							x++;
						}
						roadFreq = Game.Random.Int( 24 );
						continue;
					}

					if ( bigRoadFreq <= 0 && !lastGenerationWasRoad )
					{
						lastGenerationWasRoad = true;

						var road = BigRoadPrefab.Clone();
						road.Transform.Position = new Vector3( x * tileSize + 96, 0, 0 );
						road.SetParent( GameObject );
						road.NetworkSpawn();
						float offset = tileSize * 28;
						float angle = -90;
						bool flipped = false;
						if ( Game.Random.Int( 1 ) == 1 )
						{
							offset = -offset;
							angle = 90;
							flipped = true;
						}
						for ( int i = 0; i < 3; i++ )
						{
							EntitySpawner spawner = CreateEntitySpawner();
							spawner.Transform.Position = new Vector3( x * tileSize, offset, 30 );
							spawner.Transform.Rotation = Rotation.FromYaw( angle );
							_ = spawner.SpawnEntities( CarPrefab, 0.9f, 5, flipped );
							x++;
						}
						bigRoadFreq = Game.Random.Int( 32 );
						continue;
					}

					// Lillypads
					if ( lillyFreq <= 0 )
					{
						for ( int k = 0; k < worldWidth; k++ )
						{
							if ( Game.Random.Int( 1 ) == 1 )
								continue;
							var lilly = LillyPrefab.Clone();
							lilly.Transform.Position = new Vector3( x * tileSize, k * tileSize - tileSize * 14, 12 );
							lilly.Transform.Rotation = Rotation.FromYaw( Game.Random.Float( 0, 360 ) );
							lilly.SetParent( GameObject );
							lilly.NetworkSpawn();
						}
						lillyFreq = Game.Random.Int( 16 );
						break;
					}

					// Regular tiles.
					CreateTile( currentPosition );

					// Obstacles
					if ( Game.Random.Int( 28 ) == 28 )
					{
						var prefab = Game.Random.FromArray( new GameObject[] { TreePrefab, RockPrefab } );
						CreateDecoration( prefab, new Vector3( currentPosition, 32 ) );
					}

					// Misc Debris
					if ( Game.Random.Int( 24 ) == 24 )
					{
						CreateDecoration( PebblesPrefab, new Vector3( currentPosition, 32 ) );
					}
				}
			}
		}


		private void CreateTile( Vector3 position, Color color )
		{
			var root = TilePrefab.Clone();
			root.Transform.Position = position;
			root.Components.Get<ModelRenderer>().Tint = color;
			root.SetParent( GameObject );
			root.NetworkSpawn();
		}


		private void CreateTile( Vector3 position )
		{
			CreateTile( position, Color.White );
		}

		private void CreateDecoration( GameObject prefab, Vector3 position )
		{
			var root = prefab.Clone();
			root.Transform.Position = position;
			root.Transform.Rotation = Rotation.FromYaw( Game.Random.Float( 0, 360 ) );
			root.Transform.Scale = Game.Random.Float( 0.8f, 1.2f );
			root.SetParent( GameObject );
			root.NetworkSpawn();
		}


		private EntitySpawner CreateEntitySpawner()
		{
			GameObject root = new GameObject(true, "Spawner");
			EntitySpawner spawner = root.Components.Create<EntitySpawner>();
			root.SetParent( GameObject );
			return spawner;
		}


		private void RespawnAllFrogs()
		{
			foreach ( Frog frog in Scene.GetAllComponents<Frog>() )
				RespawnFrog( frog );
		}


		public void RespawnFrog( Frog frog )
		{
			frog.Respawn( GetSpawnPoint() );
		}


		public Vector3 GetSpawnPoint()
		{
			var spawnPoint = Scene.GetAllComponents<SpawnPoint>().OrderBy( x => Guid.NewGuid() ).FirstOrDefault();
			if ( spawnPoint != null )
				return spawnPoint.Transform.Position;
			return Vector3.Zero;
		}


		public float GetWorldWidthY()
		{
			return worldWidth * tileSize;
		}


		public float GetTileSize()
		{
			return tileSize;
		}
	}
}