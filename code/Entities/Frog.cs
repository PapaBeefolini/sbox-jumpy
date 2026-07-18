using Sandbox;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Jumpy;
using System.Linq;

public sealed class Frog : Component, Component.ITriggerListener
{
	private const float tileSize = 96;
	private const float jumpDistance = tileSize;
	private const float jumpHeight = 6;
	private const float maxJumpAngle = 35;

	private const float stackHeight = 18;
	private const float stackRadius = 16;

	private const float botHopMin = 0.10f;
	private const float botHopMax = 0.28f;
	private const float idleHopMin = 0.5f;
	private const float idleHopMax = 1.4f;

	// Bots are otherwise perfect traffic dodgers, which reads as robotic. Each one gets a personal
	// recklessness: the odds that a given hop decision mistimes and ignores oncoming cars (it still
	// won't leap into water or a wall — that's not "getting hit by a car"). Some frogs are daredevils.
	private const float botRecklessMin = 0.04f;
	private const float botRecklessMax = 0.18f;

	// Roughly how long a hop's landing lerp takes to settle. Bots use it to predict how far a
	// moving log will drift mid-hop, so they don't leap onto a spot the log has floated away from.
	private const float botJumpDuration = 0.22f;

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
	[Sync] public bool HasFinished { get; set; } = false;
	[Sync] public bool IsBot { get; set; } = false;
	[Sync] public string BotName { get; set; } = "";
	[Sync] public bool IsGrounded { get; set; } = false;
	[Sync] public float LastJumpTime { get; set; }

	// Furthest checkpoint band reached this run; -1 means none yet (respawn back at the start pen).
	[Sync] public int CheckpointIndex { get; set; } = -1;

	public Manager Manager { get; set; }
	public Vector3 TilePosition { get; set; }
	public GameObject CurrentLog { get; set; }
	public Vector3 LogOffset { get; set; }

	private SkinnedModelRenderer renderer;
	private SphereCollider collider;

	private Vector3 jumpOffset;
	private float stackOffset;
	private Vector3 colliderCenter;
	private Vector3 landingTraceOrigin;
	private float nextBotHopTime;
	private float nextIdleHopTime;
	private bool reroutingSideways;
	private float botRecklessness = -1f;

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
		colliderCenter = collider.Center;
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

		float elapsedTime = Time.Now - LastJumpTime;
		float jumpAmount = float.Pow( elapsedTime * 24.0f, 2.5f );

		// Frogs sharing a spot perch on top of each other. Visual only: the collider is
		// pushed back down to ground level so cars still hit the whole stack.
		stackOffset = stackOffset.LerpTo( GetStackIndex() * stackHeight, Time.Delta * 10 );
		Vector3 stackBump = Vector3.Up * stackOffset;
		collider.Center = colliderCenter - stackBump;

		Vector3 hopBump = jumpOffset;
		WorldPosition = WorldPosition.LerpTo( TilePosition + stackBump, Time.Delta * jumpAmount ) + hopBump;
		jumpOffset = jumpOffset.LerpTo( Vector3.Zero, Time.Delta * 12 );

		float distanceToLand = (WorldPosition - hopBump - stackBump - TilePosition).Length;

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
		HasFinished = false;
		CurrentLog = null;
		IsGrounded = false;
		LastJumpTime = Time.Now;
		stackOffset = 0;
		reroutingSideways = false;
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
		Scene.Camera.FieldOfView = 75;
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
		LastJumpTime = Time.Now;
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

		if ( botRecklessness < 0f )
			botRecklessness = Game.Random.Float( botRecklessMin, botRecklessMax );

		// Occasionally the frog misjudges the gap and darts out without checking for cars.
		bool reckless = Game.Random.Float() < botRecklessness;

		Vector3 sideFirst = Game.Random.Int( 1 ) == 0 ? Vector3.Left : Vector3.Right;

		// Normally push forward, using sideways hops to steer around obstacles, and fall back to a
		// backward retreat only when boxed in (tree ahead, water or walls to both sides) so a frog
		// never freezes for the whole round. Right after a retreat, try the sides first: this moves
		// the frog to a new column before it re-advances, instead of hopping straight back into the
		// same dead-end.
		Vector3[] choices = reroutingSideways
			? new[] { sideFirst, -sideFirst, Vector3.Forward, Vector3.Backward }
			: new[] { Vector3.Forward, sideFirst, -sideFirst, Vector3.Backward };

		foreach ( Vector3 direction in choices )
		{
			if ( IsHopSafe( direction, reckless ) )
			{
				nextBotHopTime = Time.Now + Game.Random.Float( botHopMin, botHopMax );
				reroutingSideways = direction == Vector3.Backward;
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

	// Mirrors Move's traces to judge whether a hop is survivable. A reckless hop skips the traffic
	// check only — terrain (water, walls, missing logs) is always fatal, so it stays a car gamble.
	private bool IsHopSafe( Vector3 direction, bool ignoreTraffic = false )
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

		// A log that's under the target right now keeps drifting during the ~botJumpDuration hop.
		// Sideways hops against the current are lethal: the trailing edge recedes and the frog
		// lands in the water the log left behind. Only commit if a log will still be there on landing.
		if ( landing.GameObject.Tags.Has( "log" ) && !LogWillHoldLanding( landing, traceOrigin ) )
			return false;

		if ( !ignoreTraffic && IsTrafficDanger( SnapToGrid( landing.EndPosition ) ) )
			return false;

		return true;
	}

	// Will the target log still sit under the landing point once the hop settles? A log point that
	// ends up under traceOrigin currently sits back along the log's travel by velocity * duration,
	// so we trace there and confirm a log is present. Same-row logs share a velocity, so any log hit
	// means one will be underneath at landing.
	private bool LogWillHoldLanding( SceneTraceResult landing, Vector3 traceOrigin )
	{
		MovingEntity log = landing.GameObject.Components.Get<MovingEntity>();
		if ( log is null )
			return true;

		Vector3 predictedOrigin = traceOrigin - Vector3.Right * log.Speed * botJumpDuration;
		SceneTraceResult predicted = Scene.Trace.Ray( new Ray( predictedOrigin, Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();

		return predicted.Hit && predicted.GameObject.Tags.Has( "log" );
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

	// How many frogs am I perched on? Whoever jumped most recently lands on top, so count
	// settled frogs at my landing spot that jumped before me. Ties break on object id.
	private int GetStackIndex()
	{
		int index = 0;

		foreach ( Frog other in Scene.GetAllComponents<Frog>() )
		{
			if ( other == this || other.IsDead || !other.IsGrounded )
				continue;

			if ( (other.WorldPosition - TilePosition).WithZ( 0 ).Length > stackRadius )
				continue;

			if ( other.LastJumpTime < LastJumpTime || (other.LastJumpTime == LastJumpTime && other.GameObject.Id.CompareTo( GameObject.Id ) < 0) )
				index++;
		}

		return index;
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
		PlayCrowdSound( dead ? DeathSound : RespawnSound, WorldPosition );
	}

	// This RPC is broadcast per-frog, so a crowd spawning or dying at once (round start,
	// a car wiping a stack) makes every client fire N identical positional sounds on the
	// same frame. They stack constructively into an ear-splitting wall. Coalesce them:
	// play at most one instance of a given sound per short window on this client.
	private static readonly Dictionary<SoundEvent, RealTimeSince> lastCrowdSound = new();

	private static void PlayCrowdSound( SoundEvent sound, Vector3 position, float minInterval = 0.1f )
	{
		if ( sound is null )
			return;

		if ( lastCrowdSound.TryGetValue( sound, out var since ) && since < minInterval )
			return;

		lastCrowdSound[sound] = 0;
		Sound.Play( sound, position );
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
