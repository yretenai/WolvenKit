using static WolvenKit.RED4.Types.Enums;

namespace WolvenKit.RED4.Types
{
	public partial class OdaCementBagControllerPS : ScriptableDeviceComponentPS
	{
		[Ordinal(107)] 
		[RED("cementEffectCooldown")] 
		public CFloat CementEffectCooldown
		{
			get => GetPropertyValue<CFloat>();
			set => SetPropertyValue<CFloat>(value);
		}

		public OdaCementBagControllerPS()
		{
			DeviceName = "LocKey#17265";
			TweakDBRecord = "Devices.CementContainer";
			TweakDBDescriptionRecord = 153526934731;

			PostConstruct();
		}

		partial void PostConstruct();
	}
}
