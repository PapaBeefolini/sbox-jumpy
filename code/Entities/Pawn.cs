using Sandbox;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Jumpy
{
	public partial class Pawn : AnimatedEntity
	{
		[Net, Predicted] public Vector3 WorldPosition { get; set; }
		[Net, Predicted] public bool IsGrounded { get; set; } = false;

		[Net] public bool IsDead { get; set; } = false;
		private enum DeathType
		{
			Car,
			Water
		}

		private float jumpDistance = 96;
		private float jumpHeight = 8;
		private float maxJumpAngle = 35;

		private Vector3 jumpOffset;
		private float timeJumpStarted;

		[Net, Predicted] public Sandbox.Entity Log { get; set; }
		[Net, Predicted] public Vector3 LogOffset { get; set; }

		public override void Spawn()
		{
			base.Spawn();

			SetModel( "models/frog.vmdl" );
			RenderColor = Color.Random;

			SetupPhysicsFromSphere( PhysicsMotionType.Keyframed, Vector3.Zero, 8.0f );

			EnableAllCollisions = true;
			EnableShadowCasting = true;
			EnableDrawing = true;
			EnableHideInFirstPerson = true;
			EnableShadowInFirstPerson = true;
			EnableLagCompensation = true;

			Tags.Add( "player" );

			if ( IsAuthority )
				ResetCamera();
		}

		public override void Simulate( IClient cl )
		{
			base.Simulate( cl );

			JumpyGame current = (JumpyGame.Current as JumpyGame);

			if(CurrentSequence.Name == string.Empty)
				CurrentSequence.Name = "idle";

			if ( Input.Released( "Use" ))
				_ = current.StartNewGame();

			if ( IsDead || !current.IsGameActive )
				return;

			using ( LagCompensation() )
			{
				if ( IsGrounded && !Game.IsMainMenuVisible )
				{
					if ( Input.Down( "Forward" ) )
						Move( Vector3.Forward );
					else if ( Input.Down( "Backward" ) )
						Move( Vector3.Backward );
					else if ( Input.Down( "Left" ) )
						Move( Vector3.Left );
					else if ( Input.Down( "Right" ) )
						Move( Vector3.Right );
				}

				if ( Log != null )
					WorldPosition = Log.Position.Round( 1 ) + LogOffset.Round( 1 );
			}

			float elapsedTime = Time.Now - timeJumpStarted;
			float jumpAmount = MathF.Pow( elapsedTime * 24.0f, 2.5f );

			Position = Position.LerpTo( WorldPosition, Time.Delta * jumpAmount ) + jumpOffset;
			jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

			float distanceToLand = (Position - WorldPosition).Length;

			if ( distanceToLand <= 16.0f )
				CurrentSequence.Name = "idle";

			if ( distanceToLand < 0.4f )
				IsGrounded = true;

			if ( Game.IsServer )
			{
				if ( current == null )
					return;
				float killBorder = (current.GetWorldWidthY() / 2) + current.GetTileSize();
				if ( Position.y <= -killBorder || Position.y >= killBorder )
					_ = Die( DeathType.Car );
			}
		}

		[GameEvent.Client.Frame]
		private void ClientFrame()
		{
			if ( !Owner.IsAuthority )
				return;
			Camera.Position = Vector3.Lerp( Camera.Position, Position + Camera.Rotation.Backward * 800, Time.Delta * 4 );
		}

		[ClientRpc]
		private void ResetCamera()
		{
			if ( !Owner.IsAuthority )
				return;
			Camera.Position = Position + Camera.Rotation.Backward * 800;
			Camera.Rotation = new Angles( 30, 15, 0 ).ToRotation();
			Camera.FieldOfView = 65;
		}

		private void Move( Vector3 direction )
		{
			Vector3 jumpClearance = Vector3.Up * 33;
			Vector3 requestedJump = SnapToGrid( WorldPosition ) + jumpClearance;
			String[] ignoreTags = { "player", "car" };

			Trace wallTrace = Trace.Ray( new Ray( requestedJump, direction ), jumpDistance ).WithoutTags( ignoreTags );
			var wallTraceResult = wallTrace.Run();
			if ( !wallTraceResult.Hit || wallTraceResult.Normal.Angle( Vector3.Up ) <= maxJumpAngle )
			{
				Trace trace = Trace.Ray( new Ray( requestedJump + (direction * jumpDistance), Vector3.Down ), 500 ).WithoutTags( ignoreTags );
				var result = trace.Run();
				if ( result.Hit )
				{
					IsGrounded = false;
					timeJumpStarted = Time.Now;

					if ( result.Entity is TreeLog )
					{
						Log = result.Entity;
						LogOffset = Log.Transform.PointToLocal( result.EndPosition ).Round( 1 );
					}
					else
					{
						Log = null;
						LogOffset = Vector3.Zero;
					}

					WorldPosition = SnapToGrid( result.EndPosition );
					jumpOffset += Vector3.Up * jumpHeight;

					if ( direction != Vector3.Up || direction != Vector3.Down )
						Rotation = Rotation.LookAt( direction, Vector3.Up );

					CurrentSequence.Name = "jump";
					Sound.FromWorld( "jumpy.hop", Position );
					Particles.Create( "particles/jump_particles.vpcf", Position );
				}
			}
		}

		public override void Touch( Entity other )
		{
			base.Touch( other );

			if ( other.Tags.Has( "car" ) )
				_ = Die(DeathType.Car);
			else if ( other.Tags.Has( "water" ) )
				_ = Die(DeathType.Water);
			else if ( other.Tags.Has( "player" ) )
				_ = Die( DeathType.Car );
			else if ( other.Tags.Has( "goal" ) )
				(JumpyGame.Current as JumpyGame)?.RespawnPawn( this );
		}

		public void Respawn( Vector3 position )
		{
			IsDead = false;
			Log = null;
			IsGrounded = false;
			WorldPosition = position;
			Position = position;
			Rotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
			RenderColor = Color.Random;
			EnableAllCollisions = true;
			EnableDrawing = true;
			CurrentSequence.Name = "idle";
			Sound.FromWorld( "jumpy.respawn", Position );
			ResetCamera( To.Single( Owner ) );
		}

		private async Task Die(DeathType deathType)
		{
			if ( !Game.IsServer || IsDead )
				return;

			IsDead = true;
			Log = null;
			EnableAllCollisions = false;
			EnableDrawing = false;
			Sound.FromWorld( "jumpy.death", Position );

			switch ( deathType )
			{
				case DeathType.Car:
					Particles.Create( "particles/death_particles_car.vpcf", Position + Vector3.Up * 8 );
					break;
				case DeathType.Water:
					Particles.Create( "particles/death_particles_water.vpcf", Position + Vector3.Down * 2 );
					break;
			}

			await Task.DelaySeconds( 5.0f );

			if ( IsDead )
				(JumpyGame.Current as JumpyGame)?.RespawnPawn( this );
		}

		private Vector3 SnapToGrid(Vector3 position)
		{
			Vector3 snapped = new Vector3(
				MathF.Round( position.x / 96 ) * 96,
				MathF.Round( position.y / 96 ) * 96, MathF.Round( position.z, 1 ) );
			return snapped;
		}
	}
}
