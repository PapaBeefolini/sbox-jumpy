using Sandbox;
using System.Threading.Tasks;
using System;
using Jumpy;
using System.Diagnostics;

public sealed class Frog : Component, Component.ITriggerListener
{
	[Property] public SoundEvent JumpSound { get; set; }
	[Property] public SoundEvent RespawnSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	public Jumpy.GameManager Jumpy { get; set; }
	public Vector3 WorldPosition { get; set; }
	public bool IsGrounded { get; set; } = false;
	public GameObject Log { get; set; }
	public Vector3 LogOffset { get; set; }

	public bool IsDead { get; set; } = false;
	private enum DeathType
	{
		Car,
		Water
	}

	private SkinnedModelRenderer renderer;
	private SphereCollider collider;

	private float jumpDistance = 96;
	private float jumpHeight = 3;
	private float maxJumpAngle = 35;

	private Vector3 jumpOffset;
	private float timeJumpStarted;


	protected override void OnAwake()
	{
		renderer = Components.Get<SkinnedModelRenderer>();
		renderer.Tint = Color.Random;
		collider = Components.Get<SphereCollider>();
	}


	protected override void OnStart()
	{
		if ( !IsProxy )
			ResetCamera();
	}


	protected override void OnUpdate()
	{
		if ( IsProxy || IsDead )
			return;

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
			WorldPosition = Log.Transform.Position.Round( 1 ) + LogOffset.Round( 1 );

		float elapsedTime = Time.Now - timeJumpStarted;
		float jumpAmount = MathF.Pow( elapsedTime * 24.0f, 2.5f );

		Transform.Position = Transform.Position.LerpTo( WorldPosition, Time.Delta * jumpAmount ) + jumpOffset;
		jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

		float distanceToLand = (Transform.Position - WorldPosition).Length;

		if ( distanceToLand <= 16.0f )
			renderer.Set( "Grounded", true );

		if ( distanceToLand < 0.4f )
			IsGrounded = true;

		if ( Networking.IsHost )
		{
			if ( Jumpy == null )
				return;
			float killBorder = (Jumpy.GetWorldWidthY() / 2) + Jumpy.GetTileSize();
			if ( Transform.Position.y <= -killBorder || Transform.Position.y >= killBorder )
				_ = Die( DeathType.Car );
		}

		UpdateCamera();
	}


	private void UpdateCamera()
	{
		if ( IsProxy )
			return;
		Scene.Camera.Transform.Position = Vector3.Lerp( Scene.Camera.Transform.Position, Transform.Position + Scene.Camera.Transform.Rotation.Backward * 800, Time.Delta * 4 );
	}


	private void ResetCamera()
	{
		if ( IsProxy )
			return;
		Scene.Camera.Transform.Position = Transform.Position + Scene.Camera.Transform.Rotation.Backward * 800;
		Scene.Camera.Transform.Rotation = new Angles( 30, 15, 0 ).ToRotation();
		Scene.Camera.FieldOfView = 65;
	}


	private void Move( Vector3 direction )
	{
		Vector3 jumpClearance = Vector3.Up * 33;
		Vector3 requestedJump = SnapToGrid( WorldPosition ) + jumpClearance;
		String[] ignoreTags = { "player", "car" };

		SceneTraceResult wallTraceResult = Scene.Trace.Ray( new Ray( requestedJump, direction ), jumpDistance ).WithoutTags( ignoreTags ).Run();
		if ( !wallTraceResult.Hit || wallTraceResult.Normal.Angle( Vector3.Up ) <= maxJumpAngle )
		{
			SceneTraceResult result = Scene.Trace.Ray( new Ray( requestedJump + (direction * jumpDistance), Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();
			if ( result.Hit )
			{
				IsGrounded = false;
				timeJumpStarted = Time.Now;

				if ( result.GameObject.Tags.Has("log") )
				{
					Log = result.GameObject;
					LogOffset = Log.Transform.World.PointToLocal( result.EndPosition ).Round( 1 );
				}
				else
				{
					Log = null;
					LogOffset = Vector3.Zero;
				}

				WorldPosition = SnapToGrid( result.EndPosition );
				jumpOffset += Vector3.Up * jumpHeight;

				if ( direction != Vector3.Up || direction != Vector3.Down )
					Transform.Rotation = Rotation.LookAt( direction, Vector3.Up );

				renderer.Set( "Grounded", false );
				Sound.Play( JumpSound, Transform.Position );
				//Particles.Create( "particles/jump_particles.vpcf", Position );
			}
		}
	}


	public void OnTriggerEnter( Collider other )
	{
		if ( !Jumpy.IsGameActive )
			return;

		if ( other.Tags.Has( "car" ) )
			_ = Die( DeathType.Car );
		else if ( other.Tags.Has( "water" ) )
			_ = Die( DeathType.Water );
		else if ( other.Tags.Has( "player" ) )
			_ = Die( DeathType.Car );
	}


	public void Respawn( Vector3 position )
	{
		IsDead = false;
		Log = null;
		IsGrounded = false;
		WorldPosition = position;
		Transform.Position = position;
		Transform.Rotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
		renderer.Tint = Color.Random;
		collider.Enabled = true;
		renderer.Enabled = true;
		renderer.Set( "Grounded", true );
		Sound.Play( RespawnSound, Transform.Position );
		ResetCamera();
	}


	private async Task Die( DeathType deathType )
	{
		if ( !Networking.IsHost || IsDead )
			return;

		IsDead = true;
		Log = null;
		collider.Enabled = false;
		renderer.Enabled = false;
		Sound.Play( DeathSound, Transform.Position );

		/*switch ( deathType )
		{
			case DeathType.Car:
				Particles.Create( "particles/death_particles_car.vpcf", Position + Vector3.Up * 8 );
				break;
			case DeathType.Water:
				Particles.Create( "particles/death_particles_water.vpcf", Position + Vector3.Down * 2 );
				break;
		}*/

		await Task.DelaySeconds( 5.0f );

		if ( IsDead )
			Jumpy.RespawnFrog( this );
	}


	private Vector3 SnapToGrid( Vector3 position )
	{
		Vector3 snapped = new Vector3(
			MathF.Round( position.x / 96 ) * 96,
			MathF.Round( position.y / 96 ) * 96, MathF.Round( position.z, 1 ) );
		return snapped;
	}
}
