using Sandbox;
using Sandbox.PostProcess;
using System;
using System.Linq;

namespace Jumpy
{
	partial class JumpyGame : GameManager
	{
		private int worldLength = 128;
		private int worldWidth = 28;
		private int tileSize = 96;

		public JumpyGame()
		{
			if ( IsServer )
				_ = new UI.GameUI();
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

			if ( !IsServer )
				return;

			CreateLighting();
			CreateWater();
			GenerateWorld();
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

			ModelEntity seabed = new ModelEntity( "models/water.vmdl" );
			seabed.Position = Vector3.Up * 8;
			seabed.Rotation = Rotation.FromYaw( 180 );
		}

		private void GenerateWorld()
		{
			int roadFreq = Game.Random.Int( 1, 10 );
			int riverFreq = Game.Random.Int( 12 );

			for ( int x = 0; x < worldLength; x++ )
			{
				roadFreq--;
				riverFreq--;

				for ( int y = 0; y < worldWidth; y++ )
				{
					Vector3 currentPosition = new Vector3( x * tileSize, y * tileSize - tileSize * 14, Sandbox.Utility.Noise.Perlin( x * 32, y * 32 ) * 8 );

					// Starter tiles at first row
					if ( x == 0 )
					{
						CreateTile( currentPosition, new Color( 0.75f, 1, 0.75f ) );
						SpawnPoint spawnPoint = new SpawnPoint();
						spawnPoint.Position = currentPosition + Vector3.Up * 32;
						continue;
					}

					// Win tiles at last row
					if ( x >= worldLength - 1 )
					{
						CreateTile( currentPosition, new Color( 1, 0.75f, 0.75f ) );
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
							x++;
						}
						riverFreq = Game.Random.Int( 12 );
						continue;
					}

					// Roads
					if ( roadFreq <= 0 )
					{
						ModelEntity road = new ModelEntity( "models/road.vmdl" );
						road.Position = new Vector3( x * tileSize + 48, 0, 0 );
						road.SetupPhysicsFromModel( PhysicsMotionType.Static );
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
							_ = spawner.SpawnEntities<Car>( 0.6f, 4, flipped );
							x++;
						}
						roadFreq = Game.Random.Int( 1, 10 );
						continue;
					}

					// Regular tiles.
					CreateTile( currentPosition );

					// Obstacles
					if ( Game.Random.Int( 28 ) == 28 )
					{
						string[] models = new string[] { "models/tree-lowpoly-1.vmdl", "models/rock.vmdl" };
						CreateDecoration( models, new Vector3( x * tileSize, y * tileSize - tileSize * 14, 32 ) );
					}

					// Misc Debris
					if ( Game.Random.Int( 24 ) == 24 )
					{
						string[] models = new string[] { "models/pebbles.vmdl" };
						CreateDecoration( models, new Vector3( x * tileSize, y * tileSize - tileSize * 14, 32 ) );
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
		}

		public void RespawnPawn( Pawn pawn )
		{
			var spawnPoint = All.OfType<SpawnPoint>().OrderBy( x => Guid.NewGuid() ).FirstOrDefault();
			if ( spawnPoint != null )
			{
				pawn.Log = null;
				pawn.WorldPosition = spawnPoint.Position;
			}
		}
	}
}
