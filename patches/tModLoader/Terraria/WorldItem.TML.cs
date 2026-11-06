using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.GameContent.Prefixes;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terraria;
public partial class WorldItem
{
	public bool master => inner.master;

	public ModItem ModItem => inner.ModItem;

	// TML: #AdvancedShimmerTransformations
	Vector2 IModShimmerable.Velocity { get => velocity; set => velocity = value; }
	int IModShimmerable.ShimmerRedirectedType => ShimmerLoader<WorldItem>.GetRedirectedType(type);
	int IModShimmerable.Type => type;
	int IModShimmerable.Stack => stack;
	public bool PreventingChainedShimmers { get; set; }
	void IModShimmerable.ShimmerRemoveStacked(int amount)
	{
		stack -= amount;
		shimmerWet = wet = shimmered = true;
		velocity *= 0.1f;

		if (stack <= 0) {
			inner.TurnToAir();
			shimmerTime = 0f;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncItemsWithShimmer, -1, -1, null, whoAmI, 1f);
		}
		else {
			shimmerTime = 1f;
		}
	}

	// TODO KOGSEY: this

	/// <summary> <inheritdoc/> <br/>
	/// For this <see cref="Item"/> override, calls <see cref="ItemLoader.OnShimmer(Item)"/>
	/// </summary>
	public void OnShimmer() { }

	//ItemLoader.OnShimmer(this);
	//public IEntitySource GetSource_ForShimmer() => GetSource_Misc(ItemSourceID.ToContextString(ItemSourceID.Shimmer));
}
