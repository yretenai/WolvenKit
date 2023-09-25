using static WolvenKit.RED4.Types.Enums;

namespace WolvenKit.RED4.Types
{
	public partial class characterCreationGenderBackstoryBtn : inkButtonController
	{
		[Ordinal(13)] 
		[RED("selector")] 
		public inkWidgetReference Selector
		{
			get => GetPropertyValue<inkWidgetReference>();
			set => SetPropertyValue<inkWidgetReference>(value);
		}

		[Ordinal(14)] 
		[RED("fluffText")] 
		public inkWidgetReference FluffText
		{
			get => GetPropertyValue<inkWidgetReference>();
			set => SetPropertyValue<inkWidgetReference>(value);
		}

		public characterCreationGenderBackstoryBtn()
		{
			Selector = new inkWidgetReference();
			FluffText = new inkWidgetReference();

			PostConstruct();
		}

		partial void PostConstruct();
	}
}
