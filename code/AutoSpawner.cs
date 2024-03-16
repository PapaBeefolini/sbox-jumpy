using Sandbox;
using System.Threading.Tasks;

namespace Jumpy
{
	public sealed class AutoSpawner : Component
	{
		[Property] public GameObject Prefab { get; set; }
		[Property] public bool flipped { get; set; } = false;

		protected override void OnStart()
		{
			_ = Components.Get<EntitySpawner>().SpawnEntities( Prefab, 0.9f, 5, flipped );
		}
	}
}
