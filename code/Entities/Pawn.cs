using Sandbox;
using System;
using static Sandbox.Event;

namespace Jumpy
{
	public partial class Pawn : AnimatedEntity
	{
		public Vector3 WorldPosition { get; set; }
		public bool IsGrounded { get; set; } = false;
		
		private float jumpDistance = 96;
		private float jumpHeight = 8;
		private float maxJumpAngle = 35;
		
		private Vector3 jumpOffset;
		private float timeJumpStarted;

		public Sandbox.Entity Log { get; set; }
		private Vector3 logOffset;

		public override void Spawn()
		{
			base.Spawn();

			SetModel( "models/frog.vmdl" );
			RenderColor = Color.Random;

			Predictable = false;
			EnableAllCollisions = false;
			EnableShadowCasting = true;
			EnableDrawing = true;
			EnableHideInFirstPerson = true;
			EnableShadowInFirstPerson = true;
		}

		public override void Simulate( IClient cl )
		{
			base.Simulate( cl );
			
			float elapsedTime = Time.Now - timeJumpStarted;
			float jumpAmount = MathF.Pow( elapsedTime * 24.0f, 2.5f );

			if ( Log != null )
				WorldPosition = Log.Position + logOffset;

			Position = Position.LerpTo( WorldPosition, Time.Delta * jumpAmount ) + jumpOffset;

			jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

			if ( Input.Pressed( InputButton.Reload ) )
				(JumpyGame.Current as JumpyGame)?.RespawnPawn( this );

			if ( IsGrounded )
			{
				if ( Input.Down( InputButton.Forward ) )
					Move( Vector3.Forward );
				else if ( Input.Down( InputButton.Back ) )
					Move( Vector3.Backward );
				else if ( Input.Down( InputButton.Left ) )
					Move( Vector3.Left );
				else if ( Input.Down( InputButton.Right ) )
					Move( Vector3.Right );
			}

			float distanceToLand = (Position - WorldPosition).Length;

			if ( distanceToLand <= 16.0f )
				CurrentSequence.Name = "idle";

			if ( distanceToLand < 0.4f )
				IsGrounded = true;
		}

		[Client.Frame]
		private void ClientFrame()
		{
			if ( !Owner.IsAuthority )
				return;

			Camera.Position = Vector3.Lerp( Camera.Position, Position + Vector3.Backward * 500 + Vector3.Up * 375 + Vector3.Right * 150, Time.Delta * 6 );
			Camera.Rotation = new Angles( 35, 15, 0 ).ToRotation();
			Camera.FieldOfView = 80;
		}

		private void Move( Vector3 direction )
		{
			Vector3 jumpClearance = Vector3.Up * 33;
			Vector3 requestedJump = SnapToGrid( WorldPosition ) + jumpClearance;

			Trace wallTrace = Trace.Ray( new Ray( requestedJump, direction ), jumpDistance );
			var wallTraceResult = wallTrace.Run();
			if ( !wallTraceResult.Hit || wallTraceResult.Normal.Angle( Vector3.Up ) <= maxJumpAngle )
			{
				Trace trace = Trace.Ray( new Ray( requestedJump + (direction * jumpDistance), Vector3.Down ), 500 );
				var result = trace.Run();
				if ( result.Hit )
				{
					IsGrounded = false;
					timeJumpStarted = Time.Now;

					if ( result.Entity is TreeLog )
					{
						Log = result.Entity;
						logOffset = Log.Transform.PointToLocal( result.EndPosition );
					}
					else
					{
						Log = null;
						logOffset = Vector3.Zero;
					}

					WorldPosition = SnapToGrid( result.EndPosition );
					jumpOffset += Vector3.Up * jumpHeight;

					if ( direction != Vector3.Up || direction != Vector3.Down )
						Rotation = Rotation.LookAt( direction, Vector3.Up );

					CurrentSequence.Name = "jump";

					using ( Prediction.Off() )
						Sound.FromWorld( "jumpy.hop", Position );
				}
			}
		}

		private Vector3 SnapToGrid(Vector3 position)
		{
			Vector3 snapped = new Vector3(
				MathF.Round( position.x / 96 ) * 96,
				MathF.Round( position.y / 96 ) * 96, position.z );
			return snapped;
		}
	}
}
