using Sandbox;
using System;

namespace Jumpy
{
	public partial class TreeLog : MovingEntity
	{
		public override void Spawn()
		{
			base.Spawn();

			Speed = 350;

			SetModel( "models/log.vmdl" );
		}
	}
}
