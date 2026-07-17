using Sandbox;
using System.Threading.Tasks;
using System;
using Jumpy;
using System.Linq;

public sealed class Frog : Component, Component.ITriggerListener
{
	private const float tileSize = 96;
	private const float jumpDistance = tileSize;
	private const float jumpHeight = 6;
	private const float maxJumpAngle = 35;

	private const float botHopMin = 0.10f;
	private const float botHopMax = 0.28f;
	private const float idleHopMin = 0.5f;
	private const float idleHopMax = 1.4f;

	private static readonly Vector3 jumpClearance = Vector3.Up * 33;
	private static readonly string[] ignoreTags = { "player", "car" };

	[Property] public GameObject JumpParticles { get; set; }
	[Property] public GameObject DeathParticlesCar { get; set; }
	[Property] public GameObject DeathParticlesWater { get; set; }

	[Property] public SoundEvent JumpSound { get; set; }
	[Property] public SoundEvent RespawnSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	[Sync] public Color FrogColor { get; set; } = Color.White;
	[Sync] public bool IsDead { get; set; } = false;
	[Sync] public bool IsBot { get; set; } = false;
	[Sync] public string BotName { get; set; } = "";

	public Manager Manager { get; set; }
	public Vector3 TilePosition { get; set; }
	public bool IsGrounded { get; set; } = false;
	public GameObject CurrentLog { get; set; }
	public Vector3 LogOffset { get; set; }

	private SkinnedModelRenderer renderer;
	private SphereCollider collider;

	private Vector3 jumpOffset;
	private float timeJumpStarted;
	private Vector3 landingTraceOrigin;
	private float nextBotHopTime;
	private float nextIdleHopTime;

	private enum DeathType
	{
		Car,
		Water
	}

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
		if ( IsProxy || IsDead || (!Manager.IsGameActive && Manager.CountdownRemaining <= 0) )
			return;

		if ( IsGrounded )
		{
			if ( CurrentLog.IsValid() )
				TilePosition = CurrentLog.WorldPosition.Round( 1 ) + LogOffset.Round( 1 );

			Vector3 moveDirection = IsBot ? GetBotDirection() : GetInputDirection();
			if ( moveDirection != Vector3.Zero )
				Move( moveDirection );
		}
		else
		{
			SceneTraceResult landing = TraceLandingSurface();
			if ( landing.Hit )
				TilePosition = SnapToGrid( landing.EndPosition );
		}

		float elapsedTime = Time.Now - timeJumpStarted;
		float jumpAmount = float.Pow( elapsedTime * 24.0f, 2.5f );

		Vector3 hopBump = jumpOffset;
		WorldPosition = WorldPosition.LerpTo( TilePosition, Time.Delta * jumpAmount ) + hopBump;
		jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

		float distanceToLand = (WorldPosition - hopBump - TilePosition).Length;

		if ( !IsGrounded )
		{
			if ( distanceToLand <= 16.0f )
				UpdateAnimation( true );

			if ( distanceToLand < 0.2f )
			{
				IsGrounded = true;
				Land();
			}
		}

		float killBorder = (Manager.GetWorldWidthY() / 2) + Manager.GetTileSize();
		if ( WorldPosition.y <= -killBorder || WorldPosition.y >= killBorder )
			_ = Die( DeathType.Car );
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( IsProxy || !Manager.IsValid() || !Manager.IsGameActive )
			return;

		if ( other.Tags.Has( "car" ) )
			_ = Die( DeathType.Car );
	}

	[Rpc.Broadcast]
	public void Respawn( Vector3 position )
	{
		if ( IsProxy )
			return;

		IsDead = false;
		CurrentLog = null;
		IsGrounded = false;
		TilePosition = position;
		WorldPosition = position;
		landingTraceOrigin = position + jumpClearance;
		WorldRotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
		FrogColor = Color.Random;
		UpdateAppearance( IsDead );
		UpdateAnimation( true );
		ResetCamera();
	}

	private void UpdateCamera()
	{
		// Bots run on the host so they aren't proxies, but they must never drive the local camera.
		if ( IsProxy || IsBot )
			return;

		Scene.Camera.WorldPosition = Vector3.Lerp( Scene.Camera.WorldPosition, WorldPosition + Scene.Camera.WorldRotation.Backward * 800, Time.Delta * 4 );
	}

	private void ResetCamera()
	{
		if ( IsProxy || IsBot )
			return;

		Scene.Camera.WorldPosition = WorldPosition + Scene.Camera.WorldRotation.Backward * 800;
		Scene.Camera.WorldRotation = new Angles( 30, 15, 0 ).ToRotation();
		Scene.Camera.FieldOfView = 65;
	}

	private void Move( Vector3 direction )
	{
		Vector3 requestedJump = SnapToGrid( TilePosition ) + jumpClearance;

		SceneTraceResult wall = Scene.Trace.Ray( new Ray( requestedJump, direction ), jumpDistance ).WithoutTags( ignoreTags ).Run();
		if ( wall.Hit && wall.Normal.Angle( Vector3.Up ) > maxJumpAngle )
			return;

		Vector3 traceOrigin = requestedJump + (direction * jumpDistance);
		SceneTraceResult landing = Scene.Trace.Ray( new Ray( traceOrigin, Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();
		if ( !landing.Hit )
			return;

		if ( !Manager.IsGameActive && !Manager.IsWithinStartArea( SnapToGrid( landing.EndPosition ) ) )
			return;

		IsGrounded = false;
		timeJumpStarted = Time.Now;
		landingTraceOrigin = traceOrigin;

		CurrentLog = null;
		LogOffset = Vector3.Zero;

		TilePosition = SnapToGrid( landing.EndPosition );
		jumpOffset += Vector3.Up * jumpHeight;
		WorldRotation = Rotation.LookAt( direction, Vector3.Up );

		UpdateAnimation( false );
		SpawnJumpParticles( WorldPosition );
	}

	private Vector3 GetInputDirection()
	{
		if ( Input.Down( "Forward" ) )
			return Vector3.Forward;
		if ( Input.Down( "Backward" ) )
			return Vector3.Backward;
		if ( Input.Down( "Left" ) )
			return Vector3.Left;
		if ( Input.Down( "Right" ) )
			return Vector3.Right;

		return Vector3.Zero;
	}

	private Vector3 GetBotDirection()
	{
		if ( !Manager.IsGameActive )
			return GetIdleShuffleDirection();

		if ( Time.Now < nextBotHopTime )
			return Vector3.Zero;

		Vector3 sideFirst = Game.Random.Int( 1 ) == 0 ? Vector3.Left : Vector3.Right;
		Vector3[] choices = { Vector3.Forward, sideFirst, -sideFirst };

		foreach ( Vector3 direction in choices )
		{
			if ( IsHopSafe( direction ) )
			{
				nextBotHopTime = Time.Now + Game.Random.Float( botHopMin, botHopMax );
				return direction;
			}
		}

		nextBotHopTime = Time.Now + 0.1f;
		return Vector3.Zero;
	}

	// Move() rejects hops that would leave the pen, so a bad pick just skips the hop.
	private Vector3 GetIdleShuffleDirection()
	{
		if ( Time.Now < nextIdleHopTime )
			return Vector3.Zero;

		nextIdleHopTime = Time.Now + Game.Random.Float( idleHopMin, idleHopMax );

		Vector3[] directions = { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right };
		return directions[Game.Random.Int( directions.Length - 1 )];
	}

	// Mirrors Move's traces to judge whether a hop is survivable.
	private bool IsHopSafe( Vector3 direction )
	{
		Vector3 requestedJump = SnapToGrid( TilePosition ) + jumpClearance;

		SceneTraceResult wall = Scene.Trace.Ray( new Ray( requestedJump, direction ), jumpDistance ).WithoutTags( ignoreTags ).Run();
		if ( wall.Hit && wall.Normal.Angle( Vector3.Up ) > maxJumpAngle )
			return false;

		Vector3 traceOrigin = requestedJump + (direction * jumpDistance);
		SceneTraceResult landing = Scene.Trace.Ray( new Ray( traceOrigin, Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();

		if ( !landing.Hit )
			return false;

		if ( landing.GameObject.Tags.Has( "water" ) )
			return false;

		if ( IsTrafficDanger( SnapToGrid( landing.EndPosition ) ) )
			return false;

		return true;
	}

	private bool IsTrafficDanger( Vector3 target )
	{
		const float rowTolerance = 64f;
		const float carHalfLength = 100f;
		const float exposureTime = 0.55f;

		foreach ( Car car in Scene.GetAllComponents<Car>() )
		{
			Vector3 carPos = car.WorldPosition;
			if ( float.Abs( carPos.x - target.x ) > rowTolerance )
				continue;

			if ( float.Abs( carPos.y - target.y ) < carHalfLength )
				return true;

			// Cars travel along Y; Vector3.Right is -Y, so velocity.y = -Speed.
			float velocityY = -car.Speed;
			if ( float.Abs( velocityY ) < 1f )
				continue;

			float timeToReach = (target.y - carPos.y) / velocityY;
			if ( timeToReach > 0f && timeToReach < exposureTime )
				return true;
		}

		return false;
	}

	private SceneTraceResult TraceLandingSurface()
	{
		return Scene.Trace.Ray( new Ray( landingTraceOrigin, Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();
	}

	private void Land()
	{
		SceneTraceResult result = TraceLandingSurface();

		if ( result.Hit && result.GameObject.Tags.Has( "log" ) )
		{
			CurrentLog = result.GameObject;
			LogOffset = CurrentLog.Transform.World.PointToLocal( result.EndPosition ).Round( 1 );
			return;
		}

		CurrentLog = null;
		LogOffset = Vector3.Zero;

		if ( result.Hit && result.GameObject.Tags.Has( "water" ) )
			_ = Die( DeathType.Water );
	}

	private async Task Die( DeathType deathType )
	{
		if ( IsProxy || IsDead )
			return;

		IsDead = true;
		CurrentLog = null;
		UpdateAppearance( IsDead );
		SpawnDeathParticles( deathType, WorldPosition );

		await Task.DelaySeconds( 3.0f );

		if ( IsDead )
			Manager.RespawnFrog( this );
	}

	[Rpc.Broadcast]
	private void SpawnDeathParticles( DeathType deathType, Vector3 position )
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

	[Rpc.Broadcast]
	private void SpawnJumpParticles( Vector3 position )
	{
		JumpParticles.Clone( position, Rotation.FromPitch( -90 ) );
		Sound.Play( JumpSound, position );
	}

	[Rpc.Broadcast]
	private void UpdateAppearance( bool dead )
	{
		collider.Enabled = !dead;
		collider.IsTrigger = !dead;
		renderer.Enabled = !dead;
		Sound.Play( dead ? DeathSound : RespawnSound, WorldPosition );
	}

	[Rpc.Broadcast]
	private void UpdateAnimation( bool grounded )
	{
		renderer.Set( "Grounded", grounded );
	}

	private Vector3 SnapToGrid( Vector3 position )
	{
		return new Vector3(
			float.Round( position.x / tileSize ) * tileSize,
			float.Round( position.y / tileSize ) * tileSize,
			float.Round( position.z, 1 ) );
	}
}
