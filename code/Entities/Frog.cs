using Sandbox;
using System.Threading.Tasks;
using System;
using Jumpy;
using System.Diagnostics;
using System.Numerics;
using Sandbox.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Linq;

public sealed class Frog : Component, Component.ITriggerListener
{
	[Property] public GameObject JumpParticles { get; set; }
	[Property] public GameObject DeathParticlesCar { get; set; }
	[Property] public GameObject DeathParticlesWater { get; set; }

	[Property] public SoundEvent JumpSound { get; set; }
	[Property] public SoundEvent RespawnSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	[Sync] public Color FrogColor { get; set; } = Color.White;

	public Manager Manager { get; set; }
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
	private float jumpHeight = 6;
	private float maxJumpAngle = 35;

	private Vector3 jumpOffset;
	private float timeJumpStarted;


	protected override void OnAwake()
	{
		Manager = Scene.GetAllComponents<Manager>().FirstOrDefault();
		renderer = Components.Get<SkinnedModelRenderer>();
		collider = Components.Get<SphereCollider>();
	}


	protected override void OnUpdate()
	{
		UpdateCamera();

		renderer.Tint = FrogColor;
	}


	protected override void OnFixedUpdate()
	{
		if ( IsProxy || IsDead || !Manager.IsGameActive )
			return;

		if ( IsGrounded )
		{
			if ( Input.Down( "Forward" ) )
				Move( Vector3.Forward );
			else if ( Input.Down( "Backward" ) )
				Move( Vector3.Backward );
			else if ( Input.Down( "Left" ) )
				Move( Vector3.Left );
			else if ( Input.Down( "Right" ) )
				Move( Vector3.Right );
			else if ( Input.Pressed( "Use" ) )
				_ = Manager.EndGame();
		}

		if ( Log != null )
			WorldPosition = Log.Transform.Position.Round( 1 ) + LogOffset.Round( 1 );

		float elapsedTime = Time.Now - timeJumpStarted;
		float jumpAmount = MathF.Pow( elapsedTime * 24.0f, 2.5f );

		Transform.Position = Transform.Position.LerpTo( WorldPosition, Time.Delta * jumpAmount ) + jumpOffset;
		jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

		float distanceToLand = (Transform.Position - WorldPosition).Length;

		if ( distanceToLand <= 16.0f )
			UpdateAnimation( true );

		if ( distanceToLand < 0.2f )
			IsGrounded = true;

		float killBorder = (Manager.GetWorldWidthY() / 2) + Manager.GetTileSize();
		if ( Transform.Position.y <= -killBorder || Transform.Position.y >= killBorder )
			_ = Die( DeathType.Car );
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

				UpdateAnimation( false );
				SpawnJumpParticles( Transform.Position );
			}
		}
	}


	public void OnTriggerEnter( Collider other )
	{
		if ( IsProxy || !Manager.IsValid() || !Manager.IsGameActive )
			return;

		if ( other.Tags.Has( "car" ) )
			_ = Die( DeathType.Car );
		else if ( other.Tags.Has( "water" ) )
			_ = Die( DeathType.Water );
	}


	[Broadcast]
	public void Respawn( Vector3 position )
	{
		if ( IsProxy )
			return;

		IsDead = false;
		Log = null;
		IsGrounded = false;
		WorldPosition = position;
		Transform.Position = position;
		Transform.Rotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
		FrogColor = Color.Random;
		UpdateAppearance( IsDead );
		UpdateAnimation( true );
		ResetCamera();
	}


	private async Task Die( DeathType deathType )
	{
		if ( IsProxy || IsDead )
			return;

		IsDead = true;
		Log = null;
		UpdateAppearance( IsDead );
		SpawnDeathParticles( deathType, Transform.Position );

		await Task.DelaySeconds( 5.0f );

		if ( IsDead )
			Manager.RespawnFrog( this );
	}


	[Broadcast]
	private void SpawnDeathParticles(DeathType deathType, Vector3 position)
	{
		switch ( deathType )
		{
			case DeathType.Car:
				DeathParticlesCar.Clone( position + Vector3.Up * 8 );
				break;
			case DeathType.Water:
				DeathParticlesWater.Clone( position + Vector3.Up * 2, Rotation.FromPitch( -90 ) );
				break;
		}
	}


	[Broadcast]
	private void SpawnJumpParticles(Vector3 position)
	{
		JumpParticles.Clone( position, Rotation.FromPitch( -90 ) );
		Sound.Play( JumpSound, position );
	}


	[Broadcast]
	private void UpdateAppearance(bool dead)
	{
		if ( dead )
		{
			collider.Enabled = false;
			renderer.Enabled = false;
			Sound.Play( DeathSound, Transform.Position );
		}
		else
		{
			collider.Enabled = true;
			renderer.Enabled = true;
			Sound.Play( RespawnSound, Transform.Position );
		}
	}


	[Broadcast]
	private void UpdateAnimation( bool grounded )
	{
		renderer.Set( "Grounded", grounded );
	}


	private Vector3 SnapToGrid( Vector3 position )
	{
		Vector3 snapped = new Vector3(
			MathF.Round( position.x / 96 ) * 96,
			MathF.Round( position.y / 96 ) * 96, MathF.Round( position.z, 1 ) );
		return snapped;
	}
}
