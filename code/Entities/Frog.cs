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

	// How close to being carried over the kill border a log rider gets before it stops waiting
	// for the way forward to clear and takes whatever escape hop it can find.
	private const float botDriftBailoutTime = 2.0f;

	private const float cameraDistance = 800f;
	private const float cameraFollowRate = 4f;
	private const float baseFieldOfView = 75f;

	// The HUD's death card counts this exact value down, so it lives here rather than in both.
	public const float DeathHoldSeconds = 3.0f;

	private const float deathShakeTrauma = 0.9f;
	private const float deathFovPunch = 12f;
	private const float deathFovPunchTime = 0.35f;
	private const float deathZoomIn = 260f;
	private const float deathDriftHeight = 90f;

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

	[Sync] public DeathType LastDeathType { get; set; } = DeathType.Car;

	// Bots have no owning connection, so on the host IsProxy is false for every one of them —
	// IsProxy alone would let a bot death shake the host's camera and nobody else's. All
	// local-only effects gate on this.
	public bool IsLocalPlayer => !IsProxy && !IsBot;

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

	private RealTimeSince deathAt;
	private int deathSequence;

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

		float killBorder = GetKillBorder();
		if ( WorldPosition.y <= -killBorder || WorldPosition.y >= killBorder )
			_ = Die( DeathType.Drift );
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

	// Sole owner of the camera's position and FOV between respawns; anything written elsewhere
	// gets overwritten next frame. Drift and punch are pure functions of IsDead and deathAt, so
	// nothing accumulates and respawn needs no unwinding.
	private void UpdateCamera()
	{
		if ( !IsLocalPlayer || !Scene.Camera.IsValid() )
			return;

		float drift = IsDead
			? float.Clamp( (float)deathAt / DeathHoldSeconds, 0f, 1f ).EaseOutCubic()
			: 0f;

		Vector3 target = WorldPosition
			+ (Scene.Camera.WorldRotation.Backward * (cameraDistance - (deathZoomIn * drift)))
			+ (Vector3.Up * deathDriftHeight * drift);

		Scene.Camera.WorldPosition = Vector3.Lerp( Scene.Camera.WorldPosition, target, Time.Delta * cameraFollowRate );

		float punch = IsDead
			? 1f - float.Clamp( (float)deathAt / deathFovPunchTime, 0f, 1f )
			: 0f;

		Scene.Camera.FieldOfView = baseFieldOfView + (deathFovPunch * punch * punch);
	}

	private void ResetCamera()
	{
		if ( !IsLocalPlayer || !Scene.Camera.IsValid() )
			return;

		Scene.Camera.WorldPosition = WorldPosition + (Scene.Camera.WorldRotation.Backward * cameraDistance);
		Scene.Camera.WorldRotation = new Angles( 30, 15, 0 ).ToRotation();
		Scene.Camera.FieldOfView = baseFieldOfView;

		// Otherwise the tail of the death shake carries onto the fresh spawn.
		Scene.Camera.GameObject.Components.GetOrCreate<CameraShake>().Clear();
	}

	private void AddCameraShake( float trauma )
	{
		if ( !IsLocalPlayer || !Scene.Camera.IsValid() )
			return;

		Scene.Camera.GameObject.Components.GetOrCreate<CameraShake>().AddTrauma( trauma );
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
		//
		// Riding a log is the exception: sideways there just slides the frog along the log it's
		// already on, and the gap ahead lines itself up as the rows drift past each other. So a
		// rider only considers forward and otherwise waits, rather than twitching side to side.
		Vector3[] choices;

		if ( CurrentLog.IsValid() && !IsDriftingOffWorld() )
			choices = new[] { Vector3.Forward };
		else if ( reroutingSideways )
			choices = new[] { sideFirst, -sideFirst, Vector3.Forward, Vector3.Backward };
		else
			choices = new[] { Vector3.Forward, sideFirst, -sideFirst, Vector3.Backward };

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

	// Logs never turn around or wrap, so a rider that waits forever gets carried over the kill
	// border. True once the current log is within botDriftBailoutTime of taking the frog with it,
	// which is the cue to stop holding out for the way forward and jump off while there's still
	// map to jump onto.
	private bool IsDriftingOffWorld()
	{
		MovingEntity log = CurrentLog.Components.Get<MovingEntity>();
		if ( log is null )
			return false;

		// Logs travel along Y; Vector3.Right is -Y, so velocity.y = -Speed.
		float velocityY = -log.Speed;
		if ( float.Abs( velocityY ) < 1f )
			return false;

		float border = float.Sign( velocityY ) * GetKillBorder();
		return (border - WorldPosition.y) / velocityY < botDriftBailoutTime;
	}

	private float GetKillBorder() => (Manager.GetWorldWidthY() / 2) + Manager.GetTileSize();

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

		// Both must land before IsDead flips: the camera and HUD react to IsDead next frame and
		// would otherwise read the previous death's cause and clock.
		deathAt = 0f;
		LastDeathType = deathType;
		IsDead = true;
		CurrentLog = null;

		UpdateAppearance( IsDead );
		SpawnDeathParticles( deathType, WorldPosition );
		AddCameraShake( deathShakeTrauma );

		// This is fired and forgotten, so a hold cut short by a round restart still wakes up later.
		// Without the ticket it would respawn the frog out from under a newer death.
		int sequence = ++deathSequence;

		await Task.DelayRealtimeSeconds( DeathHoldSeconds );

		if ( !this.IsValid() || !Manager.IsValid() || !IsDead || deathSequence != sequence )
			return;

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

			// Drifting off the map happens out over open water, so it splashes.
			case DeathType.Water:
			case DeathType.Drift:
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
