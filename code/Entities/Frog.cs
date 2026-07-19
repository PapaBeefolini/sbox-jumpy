using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jumpy;

public sealed class Frog : Component, Component.ITriggerListener
{
	private const float jumpDistance = Manager.TileSize;
	private const float jumpHeight = 6;
	private const float maxJumpAngle = 35;

	private const float stackHeight = 18;
	private const float stackRadius = 16;

	private const float botHopMin = 0.10f;
	private const float botHopMax = 0.28f;
	private const float idleHopMin = 0.5f;
	private const float idleHopMax = 1.4f;

	// Bots are otherwise perfect traffic dodgers, which reads as robotic. Each one gets a personal
	// recklessness: the odds that a given hop ignores oncoming cars. It still won't leap into water
	// or a wall — that's not "getting hit by a car".
	private const float botRecklessMin = 0.04f;
	private const float botRecklessMax = 0.18f;

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
	private static readonly Vector3[] allDirections = { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right };

	// How bright a name colour is forced to get, and how far it's then washed toward white.
	private const float nameColorFloor = 0.85f;
	private const float nameColorWash = 0.25f;

	// Hand-picked instead of Color.Random so each frog reads as a distinct, saturated
	// silhouette against the grass and water — random rolls muddy greys and near-blacks.
	private static readonly Color[] frogColors =
	{
		new Color( 0.00f, 0.78f, 0.06f ), // green
		new Color( 0.98f, 0.00f, 0.00f ), // red
		new Color( 0.00f, 0.88f, 1.00f ), // cyan
		new Color( 0.00f, 0.14f, 1.00f ), // blue
		new Color( 1.00f, 0.88f, 0.00f ), // yellow
		new Color( 0.50f, 1.00f, 0.00f ), // lime
		new Color( 0.00f, 0.42f, 0.02f ), // dark green
		new Color( 0.60f, 0.24f, 0.00f ), // brown
		new Color( 1.00f, 0.40f, 0.00f ), // orange
		new Color( 0.60f, 0.00f, 1.00f ), // purple
		new Color( 1.00f, 0.16f, 0.62f ), // pink
		new Color( 0.00f, 0.64f, 0.50f ), // teal
		new Color( 1.00f, 0.90f, 0.42f ), // cream
	};

	[Property] public GameObject JumpParticles { get; set; }
	[Property] public GameObject DeathParticlesCar { get; set; }
	[Property] public GameObject DeathParticlesWater { get; set; }

	[Property] public SoundEvent JumpSound { get; set; }
	[Property] public SoundEvent RespawnSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	// The palette slot rather than the colour itself, so uniqueness compares ints instead of
	// floats that a sync round trip might not return bit-identical. -1 means not yet assigned.
	[Sync] public int ColorIndex { get; set; } = -1;

	[Sync] public bool IsDead { get; set; } = false;
	[Sync] public bool HasFinished { get; set; } = false;
	[Sync] public bool IsBot { get; set; } = false;
	[Sync] public string BotName { get; set; } = "";
	[Sync] public bool IsGrounded { get; set; } = false;
	[Sync] public float LastJumpTime { get; set; }

	// Furthest checkpoint band reached this run; -1 means none yet (respawn back at the start pen).
	[Sync] public int CheckpointIndex { get; set; } = -1;

	[Sync] public DeathType LastDeathType { get; set; } = DeathType.Car;

	public Color FrogColor => frogColors[int.Clamp( ColorIndex, 0, frogColors.Length - 1 )];

	public Color NameColor
	{
		get
		{
			ColorHsv hsv = FrogColor.ToHsv();
			Color lifted = hsv.WithValue( float.Max( hsv.Value, nameColorFloor ) ).ToColor();

			return Color.Lerp( lifted, Color.White, nameColorWash );
		}
	}

	public bool IsLocalPlayer => !IsProxy && !IsBot;

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
		renderer = Components.Get<SkinnedModelRenderer>();
		collider = Components.Get<SphereCollider>();
		colliderCenter = collider.Center;
		if ( IsLocalPlayer )
			AddComponent<AudioListener>();
	}

	protected override void OnUpdate()
	{
		UpdateCamera();
		renderer.Tint = FrogColor;
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || IsDead || (!Manager.Instance.IsGameActive && Manager.Instance.CountdownRemaining <= 0) )
			return;

		// A hop aimed at a log stays locked to that log for the whole flight, so the target drifts
		// along with it. Pinned to the water it was floating over instead, a hop chasing a fast log
		// would spend most of its distance just catching back up to where the frog started.
		if ( CurrentLog.IsValid() )
		{
			TilePosition = CurrentLog.WorldPosition.Round( 1 ) + LogOffset.Round( 1 );
		}
		else if ( !IsGrounded )
		{
			SceneTraceResult landing = TraceLandingSurface();
			if ( landing.Hit )
				TilePosition = SnapToGrid( landing.EndPosition );
		}

		if ( IsGrounded )
		{
			Vector3 moveDirection = IsBot ? GetBotDirection() : GetInputDirection();
			if ( moveDirection != Vector3.Zero )
				Move( moveDirection );
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

		if ( float.Abs( WorldPosition.y ) >= GetKillBorder() )
			_ = Die( DeathType.Drift );
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( IsProxy || !Manager.Instance.IsValid() || !Manager.Instance.IsGameActive )
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

		if ( ColorIndex < 0 )
			ColorIndex = PickColorIndex();

		UpdateAppearance( IsDead );
		UpdateAnimation( true );
		ResetCamera();
	}

	// Claims a palette slot nobody else is wearing, so a full lobby reads as a row of distinct
	// frogs. Past frogColors.Length frogs the palette is exhausted and duplicates are the only
	// option left, so we stop being fussy and take any slot.
	//
	// Only ever called on the owner of a frog with no colour yet, so the pool it sees is every
	// other frog's synced slot. Two frogs spawning in the same tick on different clients can still
	// land on the same colour — it settles into a duplicate rather than a broken state, which is a
	// fine trade for not routing colour assignment through the host.
	private int PickColorIndex()
	{
		var taken = Scene.GetAllComponents<Frog>()
			.Where( other => other != this )
			.Select( other => other.ColorIndex )
			.ToHashSet();

		var free = Enumerable.Range( 0, frogColors.Length ).Where( i => !taken.Contains( i ) ).ToList();

		return free.Count > 0
			? Game.Random.FromList( free )
			: Game.Random.Int( frogColors.Length - 1 );
	}

	// Sole owner of the camera's position and FOV between respawns; anything written elsewhere
	// gets overwritten next frame. Drift and punch are pure functions of IsDead and deathAt, so
	// nothing accumulates and respawn needs no unwinding.
	private void UpdateCamera()
	{
		if ( !IsLocalPlayer || !Scene.Camera.IsValid() )
			return;

		float drift = IsDead ? ((float)deathAt / DeathHoldSeconds).EaseOutCubic() : 0f;

		Vector3 target = WorldPosition
			+ (Scene.Camera.WorldRotation.Backward * (cameraDistance - (deathZoomIn * drift)))
			+ (Vector3.Up * deathDriftHeight * drift);

		Scene.Camera.WorldPosition = Vector3.Lerp( Scene.Camera.WorldPosition, target, Time.Delta * cameraFollowRate );

		float punch = IsDead ? 1f - float.Clamp( (float)deathAt / deathFovPunchTime, 0f, 1f ) : 0f;

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
		if ( !TryTraceHop( direction, out SceneTraceResult landing, out Vector3 traceOrigin ) )
			return;

		// Tiles only mean anything on solid ground; a spot on a log is wherever the log currently
		// happens to be, so snapping it would drag the landing back onto a grid the frog isn't on.
		bool landingOnLog = landing.GameObject.Tags.Has( "log" );
		Vector3 target = landingOnLog ? landing.EndPosition.Round( 1 ) : SnapToGrid( landing.EndPosition );

		if ( !Manager.Instance.IsGameActive && !Manager.Instance.IsWithinStartArea( target ) )
			return;

		IsGrounded = false;
		LastJumpTime = Time.Now;
		landingTraceOrigin = traceOrigin;

		CurrentLog = landingOnLog ? landing.GameObject : null;
		LogOffset = landingOnLog ? CurrentLog.Transform.World.PointToLocal( target ).Round( 1 ) : Vector3.Zero;

		TilePosition = target;
		jumpOffset += Vector3.Up * jumpHeight;
		WorldRotation = Rotation.LookAt( direction, Vector3.Up );

		UpdateAnimation( false );
		SpawnJumpParticles( WorldPosition );
	}

	// Where a hop in this direction would put us: false when a wall blocks it or nothing is
	// there to land on. Shared by Move and the bot's survivability check so they can't disagree.
	private bool TryTraceHop( Vector3 direction, out SceneTraceResult landing, out Vector3 traceOrigin )
	{
		landing = default;

		// Hops run tile to tile on solid ground, but a log rider sits wherever the log has carried
		// it, off the grid entirely — snapping there first would quietly shorten or stretch the hop
		// by up to half a tile depending on where the log happened to have drifted to.
		Vector3 origin = CurrentLog.IsValid() ? TilePosition : SnapToGrid( TilePosition );

		Vector3 requestedJump = origin + jumpClearance;
		traceOrigin = requestedJump + (direction * jumpDistance);

		SceneTraceResult wall = Scene.Trace.Ray( new Ray( requestedJump, direction ), jumpDistance ).WithoutTags( ignoreTags ).Run();
		if ( wall.Hit && wall.Normal.Angle( Vector3.Up ) > maxJumpAngle )
			return false;

		landing = TraceDown( traceOrigin );

		return landing.Hit;
	}

	private SceneTraceResult TraceDown( Vector3 origin )
	{
		return Scene.Trace.Ray( new Ray( origin, Vector3.Down ), 500 ).WithoutTags( ignoreTags ).Run();
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
		if ( !Manager.Instance.IsGameActive )
			return GetIdleShuffleDirection();

		if ( Time.Now < nextBotHopTime )
			return Vector3.Zero;

		if ( botRecklessness < 0f )
			botRecklessness = Game.Random.Float( botRecklessMin, botRecklessMax );

		bool reckless = Game.Random.Float() < botRecklessness;
		Vector3 sideFirst = Game.Random.Int( 1 ) == 0 ? Vector3.Left : Vector3.Right;

		// Normally push forward, using sideways hops to steer around obstacles, and fall back to a
		// backward retreat only when boxed in, so a frog never freezes for the whole round. Right
		// after a retreat, try the sides first: that moves the frog to a new column before it
		// re-advances instead of hopping straight back into the same dead-end.
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

		return Game.Random.FromArray( allDirections );
	}

	// Logs never turn around or wrap, so a rider that waits forever gets carried over the kill
	// border. True once the current log is within botDriftBailoutTime of taking the frog with it.
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

	private float GetKillBorder() => (Manager.Instance.WorldWidthY / 2) + Manager.TileSize;

	// A reckless hop skips the traffic check only — terrain (water, walls, missing logs) is
	// always fatal, so it stays a car gamble rather than a suicide.
	private bool IsHopSafe( Vector3 direction, bool ignoreTraffic = false )
	{
		if ( !TryTraceHop( direction, out SceneTraceResult landing, out _ ) )
			return false;

		if ( landing.GameObject.Tags.Has( "water" ) )
			return false;

		if ( !ignoreTraffic && IsTrafficDanger( SnapToGrid( landing.EndPosition ) ) )
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

	private SceneTraceResult TraceLandingSurface() => TraceDown( landingTraceOrigin );

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
		// A hop that aimed at a log rode it down and is already attached. Only a hop that aimed
		// somewhere else has to find out what it actually came down on — which may still be a log
		// that drifted underneath and saved it.
		if ( CurrentLog.IsValid() )
			return;

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

		if ( !this.IsValid() || !Manager.Instance.IsValid() || !IsDead || deathSequence != sequence )
			return;

		Manager.Instance.RespawnFrog( this );
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

	// These RPCs are broadcast per-frog, so a crowd spawning or dying at once (round start, a car
	// wiping a stack) makes every client fire N identical positional sounds on the same frame, and
	// they stack constructively into a wall of noise. Play at most one per short window instead.
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

	private static Vector3 SnapToGrid( Vector3 position )
	{
		return new Vector3(
			position.x.SnapToGrid( Manager.TileSize ),
			position.y.SnapToGrid( Manager.TileSize ),
			float.Round( position.z, 1 ) );
	}
}
