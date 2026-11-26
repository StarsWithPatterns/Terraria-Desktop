using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TerrariaDesktop.Common
{
	public class DesktopConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(true)]
		public bool Enabled;

		[DefaultValue(true)]
		public bool IgnoreWorldLighting;

		[Range(10, 60)]
		[DefaultValue(60)]
		public int FramesPerSecond;

		[Range(1024, 65535)]
		[DefaultValue(FrameProtocol.DefaultPort)]
		[ReloadRequired]
		public int Port;
	}
}
