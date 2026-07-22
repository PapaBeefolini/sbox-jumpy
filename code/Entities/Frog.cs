using Sandbox;
using Sandbox.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jumpy
{
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

		// Odds that a given hop ignores oncoming traffic — perfect dodging reads as robotic. A bot
		// still won't leap into water or a wall; that's not "getting hit by a car".
		private const float botRecklessMin = 0.04f;
		private const float botRecklessMax = 0.18f;

		// How close to the kill border a log rider gets before it stops waiting for the way forward to
		// clear and takes whatever escape hop it can find.
		private const float botDriftBailoutTime = 2.0f;

		// How long a bot remembers the spot it last backed out of.
		private const float abandonedTileMemory = 3.0f;

		private const float cameraDistance = 800f;
		private const float cameraFollowRate = 4f;
		private const float baseFieldOfView = 70f;

		// The HUD's death card counts this exact value down, so it lives here rather than in both.
		public const float DeathHoldSeconds = 3.0f;

		private const float deathShakeTrauma = 0.9f;
		private const float deathFovPunch = 12f;
		private const float deathFovPunchTime = 0.35f;
		private const float deathZoomIn = 260f;
		private const float deathDriftHeight = 90f;

		private static readonly Vector3 jumpClearance = Vector3.Up * 33;
		private static readonly string[] ignoreTags = { "player", "car", "train" };
		private static readonly string[] wallIgnoreTags = { "player", "car", "train", "log" };
		private static readonly Vector3[] allDirections = { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right };

		private const float nameColorFloor = 0.85f;
		private const float nameColorWash = 0.25f;

		// Hand-picked rather than random so each frog reads as a distinct, saturated silhouette against
		// the grass and water — random rolls muddy greys and near-blacks.
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

		[Sync] public bool IsDead { get; set; }
		[Sync] public bool HasFinished { get; set; }
		[Sync] public bool IsBot { get; set; }
		[Sync] public string BotName { get; set; } = "";
		[Sync] public bool IsGrounded { get; set; }
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
		private Vector3 abandonedTile;
		private RealTimeSince abandonedAt;

		private RealTimeSince deathAt;
		private int deathSequence;

		protected override void OnAwake()
		{
			renderer = Components.Get<SkinnedModelRenderer>();
			collider = Components.Get<SphereCollider>();
			colliderCenter = collider.Center;
		}

		protected override void OnStart()
		{
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
			// along with it. Pinned to the water below instead, a hop chasing a fast log would spend
			// most of its distance catching back up to where the frog started.
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

			// Frogs sharing a spot perch on top of each other. Visual only — the collider is pushed
			// back down to ground level so cars still hit the whole stack.
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

			if ( other.Tags.Has( "train" ) )
				_ = Die( DeathType.Train );
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
			abandonedAt = abandonedTileMemory;
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
		// frogs. Past frogColors.Length frogs the palette is exhausted, so we stop being fussy.
		//
		// Two frogs spawning on the same tick on different clients can still land on one colour; that
		// settles into a duplicate rather than a broken state, which beats routing this via the host.
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

		// Sole owner of the camera between respawns; anything written elsewhere is overwritten next
		// frame. Drift and punch are pure functions of IsDead and deathAt, so nothing accumulates.
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
			if ( !TryTraceHop( direction, out SceneTraceResult landing, out Vector3 target ) )
				return;

			bool landingOnLog = landing.GameObject.Tags.Has( "log" );

			if ( !Manager.Instance.IsGameActive && !Manager.Instance.IsWithinStartArea( target ) )
				return;

			IsGrounded = false;
			LastJumpTime = Time.Now;
			landingTraceOrigin = target + jumpClearance;

			CurrentLog = landingOnLog ? landing.GameObject : null;
			LogOffset = landingOnLog ? CurrentLog.Transform.World.PointToLocal( target ).Round( 1 ) : Vector3.Zero;

			TilePosition = target;
			jumpOffset += Vector3.Up * jumpHeight;
			WorldRotation = Rotation.LookAt( direction, Vector3.Up );

			UpdateAnimation( false );
			SpawnJumpParticles( WorldPosition );
		}

		// Where a hop in this direction would land: false when a wall blocks it or nothing is there to
		// land on. Shared by Move and the bot's safety check so the two can't disagree.
		private bool TryTraceHop( Vector3 direction, out SceneTraceResult landing, out Vector3 target )
		{
			// A log rider sits wherever the log has carried it, off the grid entirely — snapping first
			// would shorten or stretch the hop by up to half a tile depending on the log's drift.
			Vector3 origin = CurrentLog.IsValid() ? TilePosition : SnapToGrid( TilePosition );

			return TryTraceHop( origin, direction, out landing, out target );
		}

		// Takes the origin rather than reading it off the frog, so a bot can ask what a hop would look
		// like from a tile it hasn't jumped to yet.
		private bool TryTraceHop( Vector3 origin, Vector3 direction, out SceneTraceResult landing, out Vector3 target )
		{
			landing = default;
			target = default;

			Vector3 requestedJump = origin + jumpClearance;

			landing = TraceDown( requestedJump + (direction * jumpDistance) );
			if ( !landing.Hit )
				return false;

			// Tiles only mean anything on solid ground; a spot on a log is wherever the log is now.
			target = landing.GameObject.Tags.Has( "log" )
				? landing.EndPosition.Round( 1 )
				: SnapToGrid( landing.EndPosition );

			// Sweep to where the frog comes to rest, not to where the hop was aimed. A log rider aims
			// from off the grid, so a hop that squeaked past the side of a tree used to pass this check
			// and then get snapped straight into the trunk it had just missed.
			SceneTraceResult wall = Scene.Trace.Sphere( collider.Radius, requestedJump, target + jumpClearance ).WithoutTags( wallIgnoreTags ).Run();

			return !wall.Hit || wall.Normal.Angle( Vector3.Up ) <= maxJumpAngle;
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

			// Push forward, steer around obstacles sideways, and retreat only when boxed in, so a frog
			// never freezes for a whole round. Right after a retreat, try the sides first: that moves
			// the frog to a new column instead of hopping straight back into the same dead end.
			//
			// A log rider only considers forward. Sideways there just slides it along the log it's
			// already on, and the gap ahead lines itself up as the rows drift past each other.
			Vector3[] choices;

			if ( CurrentLog.IsValid() && !IsDriftingOffWorld() )
				choices = new[] { Vector3.Forward };
			else if ( reroutingSideways )
				choices = new[] { sideFirst, -sideFirst, Vector3.Forward, Vector3.Backward };
			else
				choices = new[] { Vector3.Forward, sideFirst, -sideFirst, Vector3.Backward };

			foreach ( Vector3 direction in choices )
			{
				if ( !IsHopSafe( direction, reckless ) )
					continue;

				nextBotHopTime = Time.Now + Game.Random.Float( botHopMin, botHopMax );
				reroutingSideways = direction == Vector3.Backward;

				// Backing out means this spot led nowhere, so remember it rather than rediscovering
				// that the moment we've shuffled far enough to face it again.
				if ( reroutingSideways )
				{
					abandonedTile = TilePosition;
					abandonedAt = 0;
				}

				return direction;
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
		// border. True once the current log is within botDriftBailoutTime of doing exactly that.
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

		// A reckless hop skips the traffic check only — water and walls stay fatal, so it's a traffic
		// gamble rather than a suicide.
		private bool IsHopSafe( Vector3 direction, bool ignoreTraffic = false )
		{
			if ( !TryTraceHop( direction, out SceneTraceResult landing, out Vector3 target ) )
				return false;

			if ( landing.GameObject.Tags.Has( "water" ) )
				return false;

			if ( IsRecentlyAbandoned( target ) )
				return false;

			if ( !ignoreTraffic && IsTrafficDanger( target ) )
				return false;

			return HasWayOnward( landing, target, direction );
		}

		// HasWayOnward only sees one hop, so a bot can be promised an exit that is itself a dead end,
		// back out of it, and hop straight back in — the same loop one tile beyond what the lookahead
		// can see. Forgetting after a few seconds keeps this a nudge to try elsewhere, not a permanent
		// no. Riders are exempt: their world moves underneath them, so where a spot led a moment ago
		// says nothing about where it leads now.
		private bool IsRecentlyAbandoned( Vector3 target )
		{
			return !CurrentLog.IsValid()
				&& abandonedAt < abandonedTileMemory
				&& (target - abandonedTile).WithZ( 0 ).Length < Manager.TileSize * 0.5f;
		}

		// One hop of lookahead, because a bot that only checks where it lands will strand itself. A
		// lily row is a scatter of static pads over open water: land on one whose neighbours are all
		// water and the only move left is the one that got you there, so the bot hops on, retreats, and
		// spends the rest of the round doing that.
		//
		// Logs are exempt — a rider is supposed to sit still and be carried, and what's out of reach
		// from a log this instant has drifted into reach a second later.
		private bool HasWayOnward( SceneTraceResult landing, Vector3 target, Vector3 direction )
		{
			if ( landing.GameObject.Tags.Has( "log" ) )
				return true;

			foreach ( Vector3 onward in allDirections )
			{
				if ( onward == -direction )
					continue;

				// Traffic is deliberately not checked: cars pass, so a lane thick with them is still a
				// way out, just not this second. Only terrain makes a spot a dead end.
				if ( TryTraceHop( target, onward, out SceneTraceResult next, out _ ) && !next.GameObject.Tags.Has( "water" ) )
					return true;
			}

			return false;
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

			// A train crosses the whole world in under half a second, so the car's "will it arrive
			// within exposureTime" window doesn't transfer — anything close enough to time a hop
			// against is already on top of you. Any train still running on the row means wait.
			foreach ( MovingEntity train in Scene.GetAllComponents<MovingEntity>() )
			{
				if ( !train.GameObject.Tags.Has( "train" ) )
					continue;

				if ( float.Abs( train.WorldPosition.x - target.x ) <= rowTolerance )
					return true;
			}

			return false;
		}

		private SceneTraceResult TraceLandingSurface() => TraceDown( landingTraceOrigin );

		// Whoever jumped most recently perches on top, so count settled frogs at my landing spot that
		// jumped before me. Ties break on object id.
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
			// A hop that aimed at a log rode it down and is already attached. Any other hop has to find
			// out what it actually came down on — maybe a log that drifted underneath and saved it.
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
			NotifyChatMessage( $"☠️ {(IsBot ? BotName : Network.Owner.DisplayName)} {DeathVerb( deathType )}!" );

			// Fired and forgotten, so a hold cut short by a round restart still wakes up later. Without
			// the ticket it would respawn the frog out from under a newer death.
			int sequence = ++deathSequence;

			await Task.DelayRealtimeSeconds( DeathHoldSeconds );

			if ( !this.IsValid() || !Manager.Instance.IsValid() || !IsDead || deathSequence != sequence )
				return;

			if ( Manager.Instance.IsGameOver )
				return;

			Manager.Instance.RespawnFrog( this );
		}

		private static string DeathVerb( DeathType deathType ) => deathType switch
		{
			DeathType.Car => "got flattened",
			DeathType.Train => "got railroaded",
			DeathType.Drift => "floated away",
			_ => "drowned"
		};

		[Rpc.Broadcast]
		private void SpawnDeathParticles( DeathType deathType, Vector3 position )
		{
			switch ( deathType )
			{
				// Flattened either way, so the train reuses the car's splat.
				case DeathType.Car:
				case DeathType.Train:
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

		[Rpc.Broadcast]
		private void NotifyChatMessage( string message )
		{
			Chat.AddText( message );
		}

		// These RPCs are broadcast per-frog, so a crowd spawning or dying at once fires N identical
		// positional sounds on the same frame and they stack into a wall of noise. Play at most one
		// per short window instead.
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
}
