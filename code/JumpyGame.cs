using Jumpy.UI;
using Sandbox;
using Sandbox.PostProcess;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace Jumpy
{
	partial class JumpyGame : GameManager
	{
		[Net] public bool IsGameActive { get; set; } = false;
		[Net] public bool IsGameOver { get; set; } = false;

		private int worldLength = 64;
		private int worldWidth = 28;
		private int tileSize = 96;
		private int winTilePosition = 0;
		private bool lastGenerationWasRoad = false;
		private List<Entity> worldEntities = new List<Entity>();

		public JumpyGame()
		{
			if ( Game.IsServer )
				_ = new UI.GameUI();

			if ( Game.IsClient )
				Sound.FromScreen( "jumpy.music" );
		}

		public override void ClientJoined( IClient client )
		{
			base.ClientJoined( client );

			var pawn = new Pawn();
			client.Pawn = pawn;
			RespawnPawn( pawn );
		}

		public override void PostLevelLoaded()
		{
			Sky sky = new Sky
			{
				Skyname = "materials/skybox/light_test_sky_sunny03.vmat"
			};

			if ( !Game.IsServer )
				return;

			CreateLighting();
			CreateWater();
			_ = StartNewGame();
		}

		public override void Simulate( IClient cl )
		{
			base.Simulate( cl );

			if ( !IsGameActive )
				return;

			foreach ( Pawn pawn in All.OfType<Pawn>() )
			{
				if ( pawn.Position.x >= winTilePosition )
				{
					_ = EndGame();
					return;
				}
			}
		}

		public async Task StartNewGame()
		{
			if ( !Game.IsServer )
				return;

			IsGameActive = false;
			IsGameOver = false;

			GenerateWorld();
			RespawnAllPlayers();

			await Task.DelaySeconds( 1.5f );

			IsGameActive = true;
		}

		public async Task EndGame()
		{
			if ( !Game.IsServer )
				return;

			IsGameActive = false;
			IsGameOver = true;

			await Task.DelaySeconds( 3.0f );

			_ = StartNewGame();
		}

		private void CreateLighting()
		{
			EnvironmentLightEntity sun = new EnvironmentLightEntity
			{
				Rotation = new Angles( 35, 85, 110 ).ToRotation(),
				Brightness = 1.5f,
				DynamicShadows = true
			};

			PostProcessingEntity ppe = new PostProcessingEntity
			{
				PostProcessingFile = "postprocess/jumpy.vpost"
			};
		}

		private void CreateWater()
		{
			ModelEntity water = new ModelEntity( "models/water.vmdl" );
			water.Position = Vector3.Up * 12;
			water.SetupPhysicsFromModel( PhysicsMotionType.Static );
			water.Tags.Add( "water" );

			ModelEntity seabed = new ModelEntity( "models/water.vmdl" );
			seabed.Position = Vector3.Up * 8;
			seabed.Rotation = Rotation.FromYaw( 180 );
		}

		private void ClearWorld()
		{
			foreach ( Entity entity in worldEntities )
				entity.Delete();
			worldEntities.Clear();

			foreach ( MovingEntity entity in All.OfType<MovingEntity>().ToList() )
				entity.Delete();
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
						SpawnPoint spawnPoint = new SpawnPoint();
						spawnPoint.Position = currentPosition + Vector3.Up * 32;
						worldEntities.Add( spawnPoint );
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
							EntitySpawner spawner = new EntitySpawner();
							spawner.Position = new Vector3( x * tileSize, offset, 0 );
							_ = spawner.SpawnEntities<TreeLog>( 3, 9, flipped );
							worldEntities.Add( spawner );
							x++;
						}
						riverFreq = Game.Random.Int( 12 );
						continue;
					}

					// Roads
					if ( roadFreq <= 0 && !lastGenerationWasRoad )
					{
						lastGenerationWasRoad = true;

						ModelEntity road = new ModelEntity( "models/road.vmdl" );
						road.Position = new Vector3( x * tileSize + 48, 0, 0 );
						road.SetupPhysicsFromModel( PhysicsMotionType.Static );
						worldEntities.Add( road );
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
							EntitySpawner spawner = new EntitySpawner();
							spawner.Position = new Vector3( x * tileSize, offset, 30 );
							spawner.Rotation = Rotation.FromYaw( angle );
							_ = spawner.SpawnEntities<Car>( 0.9f, 5, flipped );
							worldEntities.Add( spawner );
							x++;
						}
						roadFreq = Game.Random.Int( 24 );
						continue;
					}

					if ( bigRoadFreq <= 0 && !lastGenerationWasRoad )
					{
						lastGenerationWasRoad = true;

						ModelEntity road = new ModelEntity( "models/big-road.vmdl" );
						road.Position = new Vector3( x * tileSize + 96, 0, 0 );
						road.SetupPhysicsFromModel( PhysicsMotionType.Static );
						worldEntities.Add( road );
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
							EntitySpawner spawner = new EntitySpawner();
							spawner.Position = new Vector3( x * tileSize, offset, 30 );
							spawner.Rotation = Rotation.FromYaw( angle );
							_ = spawner.SpawnEntities<Car>( 0.9f, 5, flipped );
							worldEntities.Add( spawner );
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
							ModelEntity lilly = new ModelEntity( "models/lilly.vmdl" );
							lilly.Position = new Vector3( x * tileSize, k * tileSize - tileSize * 14, 12 );
							lilly.Rotation = Rotation.FromYaw( Game.Random.Float( 0, 360 ) );
							lilly.SetupPhysicsFromModel( PhysicsMotionType.Static );
							worldEntities.Add( lilly );
						}
						lillyFreq = Game.Random.Int( 16 );
						break;
					}

					// Regular tiles.
					CreateTile( currentPosition );

					// Obstacles
					if ( Game.Random.Int( 28 ) == 28 )
					{
						string[] models = new string[] { "models/tree-lowpoly-1.vmdl", "models/rock.vmdl" };
						CreateDecoration( models, new Vector3( currentPosition, 32 ) );
					}

					// Misc Debris
					if ( Game.Random.Int( 24 ) == 24 )
					{
						string[] models = new string[] { "models/pebbles.vmdl" };
						CreateDecoration( models, new Vector3( currentPosition, 32 ) );
					}
				}
			}
		}

		private void CreateTile( Vector3 position, Color color )
		{
			ModelEntity starterTile = new ModelEntity( "models/tile.vmdl" );
			starterTile.Position = position;
			starterTile.SetupPhysicsFromModel( PhysicsMotionType.Static );
			starterTile.RenderColor = color;
			worldEntities.Add( starterTile );
		}

		private void CreateTile( Vector3 position )
		{
			CreateTile( position, Color.White );
		}

		private void CreateDecoration(string[] models, Vector3 position)
		{
			ModelEntity model = new ModelEntity( Game.Random.FromArray( models ) );
			model.Position = position;
			model.Rotation = Rotation.FromYaw( Game.Random.Float( 0, 360 ) );
			model.Scale = Game.Random.Float( 0.8f, 1.2f );
			model.SetupPhysicsFromModel( PhysicsMotionType.Static );
			worldEntities.Add( model );
		}

		private void RespawnAllPlayers()
		{
			foreach ( Pawn pawn in All.OfType<Pawn>() )
				RespawnPawn( pawn );
		}

		public void RespawnPawn( Pawn pawn )
		{
			pawn.Respawn( GetSpawnPoint() );
		}

		public Vector3 GetSpawnPoint()
		{
			var spawnPoint = All.OfType<SpawnPoint>().OrderBy( x => Guid.NewGuid() ).FirstOrDefault();
			if ( spawnPoint != null )
				return spawnPoint.Position;
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
