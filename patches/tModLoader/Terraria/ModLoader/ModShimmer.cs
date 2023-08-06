﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terraria.ModLoader;
// TML: #AdvancedShimmerTransformations

/// <summary>
/// Represents the behavior and output of a shimmer transformation, the <see cref="IModShimmerable"/>(s) that can use it are stored via <see cref="Transformations"/>
/// which is updated via <see cref="Register()"/> and its overloads. Uses a similar syntax to <see cref="Recipe"/>, usually starting with
/// <see cref="ModNPC.CreateShimmerTransformation"/> or <see cref="ModItem.CreateShimmerTransformation"/>
/// </summary>
public sealed class ModShimmer : IComparable<ModShimmer>, ICloneable
{
	#region Managers

	/// <summary>
	/// Dictionary containing every <see cref="ModShimmer"/> registered to tMod indexed by <see cref="ModShimmerTypeID"/> and the entities type, automatically done in
	/// <see cref="Register()"/> and its overloads
	/// </summary>
	public static Dictionary<(ModShimmerTypeID, int), List<ModShimmer>> Transformations { get; } = new();

	/// <summary>
	/// Incremented for every transformation with type <see cref="ModShimmerTypeID.Custom"/>
	/// </summary>
	private static int customShimmerTypeCounter = -1;

	/// <summary>
	/// Use this to get the id type for use with <see cref="ModShimmerTypeID.Custom"/>. Only for custom type implementations of <see cref="IModShimmerable"/>, where an
	/// integer maps to a specific set of <see cref="ModShimmer"/> transformations
	/// </summary>
	public static int GetNextCustomShimmerID()
	{
		customShimmerTypeCounter++;
		return customShimmerTypeCounter;
	}

	internal static void Unload()
	{
		Transformations.Clear();
		customShimmerTypeCounter = -1;
	}

	#endregion Managers

	#region Redirects

	public static Dictionary<(ModShimmerTypeID, int), (ModShimmerTypeID, int)> Redirects { get; } = new();

	public static void AddRedirect(IModShimmerable redirectFrom, (ModShimmerTypeID, int) redirectTo)
		=> AddRedirect(redirectFrom.StorageKey, redirectTo);

	public static void AddRedirect((ModShimmerTypeID, int) redirectFrom, (ModShimmerTypeID, int) redirectTo)
		=> Redirects.Add(redirectFrom, redirectTo);

	/// <summary>
	/// First <see cref="Redirects"/> is checked for an entry, if one exists it is applied over <paramref name="source"/>, then if <paramref name="source"/> is
	/// <see cref="ModShimmerTypeID.Item"/>, it checks <see cref="ItemID.Sets.ShimmerCountsAsItem"/><br/> Is not recursive
	/// </summary>
	public static (ModShimmerTypeID, int) GetRedirectedKey((ModShimmerTypeID, int) source)
	{
		if (Redirects.TryGetValue(source, out (ModShimmerTypeID, int) value))
			source = value;
		if (source.Item1 == ModShimmerTypeID.Item && ItemID.Sets.ShimmerCountsAsItem[source.Item2] > 0)
			source = source with { Item2 = ItemID.Sets.ShimmerCountsAsItem[source.Item2] };
		return source;
	}

	/// <summary>
	/// First <see cref="Redirects"/> is checked for an entry, if one exists and it has the same <see cref="ModShimmerTypeID"/> it is applied over
	/// <paramref name="source"/>, then if <paramref name="source"/> is <see cref="ModShimmerTypeID.Item"/>, it checks <see cref="ItemID.Sets.ShimmerCountsAsItem"/><br/>
	/// Is not recursive
	/// </summary>
	public static int GetRedirectedKeySameShimmerID((ModShimmerTypeID, int) source)
	{
		if (Redirects.TryGetValue(source, out (ModShimmerTypeID, int) value) && value.Item1 == source.Item1)
			source = value;
		if (source.Item1 == ModShimmerTypeID.Item && ItemID.Sets.ShimmerCountsAsItem[source.Item2] > 0)
			source = source with { Item2 = ItemID.Sets.ShimmerCountsAsItem[source.Item2] };
		return source.Item2;
	}

	#endregion Redirects

	#region Constructors

	/// <inheritdoc cref="ModShimmer"/>
	/// <param name="source"> Assigned to <see cref="SourceStorageKey"/> for use with the parameterless <see cref="Register()"/> </param>
	public ModShimmer(IModShimmerable source)
	{
		SourceStorageKey = source.StorageKey;
	}

	/// <inheritdoc cref="ModShimmer"/>
	public ModShimmer()
	{ }

	#endregion Constructors

	#region FunctionalityVariables

	/// <summary>
	/// The <see cref="IModShimmerable"/> that was used to create this transformation, does not have to be used when registering
	/// </summary>
	private (ModShimmerTypeID, int)? SourceStorageKey { get; init; } // Private as it has no Modder use

	/// <summary>
	/// Every condition must be true for the transformation to occur
	/// </summary>
	public List<Condition> Conditions { get; init; } = new();

	/// <summary>
	/// The results that the transformation produces.
	/// </summary>
	public List<ModShimmerResult> Results { get; init; } = new();

	/// <summary>
	/// Vanilla disallows a transformation if the result includes either a bone or a lihzahrd brick when skeletron or golem are undefeated respectively
	/// </summary>
	public bool IgnoreVanillaItemConstraints { get; private set; }

	/// <summary>
	/// Gives a priority to the shimmer operation, lower numbers are sorted lower, higher numbers are sorted higher, clamps between -10 and 10
	/// </summary>
	public int Priority { get; private set; } = 0;

	/// <summary>
	/// Called in addition to conditions to check if the <see cref="IModShimmerable"/> shimmers
	/// </summary>
	/// <param name="transformation"> The transformation </param>
	/// <param name="source"> The <see cref="IModShimmerable"/> to be shimmered </param>
	public delegate bool CanShimmerCallBack(ModShimmer transformation, IModShimmerable source);

	/// <inheritdoc cref="CanShimmerCallBack"/>
	public CanShimmerCallBack CanShimmerCallBacks { get; private set; }

	/// <summary>
	/// Called once before a transformation, use this to edit the transformation or source beforehand
	/// </summary>
	/// <param name="transformation"> The transformation, editing this does not change the stored transformation, only this time </param>
	/// <param name="source"> The <see cref="IModShimmerable"/> to be shimmered </param>
	public delegate void ModifyShimmerCallBack(ModShimmer transformation, IModShimmerable source);

	/// <inheritdoc cref="ModifyShimmerCallBack"/>
	public ModifyShimmerCallBack ModifyShimmerCallBacks { get; private set; }

	/// <summary>
	/// Called after <see cref="IModShimmerable"/> shimmers
	/// </summary>
	/// <param name="transformation"> The transformation </param>
	/// <param name="spawnedEntities"> A list of the spawned Entities </param>
	/// <param name="source"> The <see cref="IModShimmerable"/> that was shimmered </param>
	public delegate void OnShimmerCallBack(ModShimmer transformation, IModShimmerable source, List<IModShimmerable> spawnedEntities);

	/// <inheritdoc cref="OnShimmerCallBack"/>
	public OnShimmerCallBack OnShimmerCallBacks { get; private set; }

	#endregion FunctionalityVariables

	#region ControllerMethods

	/// <summary>
	/// Adds a condition to <see cref="Conditions"/>. <inheritdoc cref="Conditions"/>
	/// </summary>
	/// <param name="condition"> The condition to be added </param>
	public ModShimmer AddCondition(Condition condition)
	{
		Conditions.Add(condition);
		return this;
	}

	#region AddResultMethods

	/// <summary>
	/// Adds a result to <see cref="Results"/>, this will be spawned when the <see cref="IModShimmerable"/> successfully shimmers
	/// </summary>
	/// <param name="result"> The result to be added </param>
	/// <exception cref="ArgumentException">
	/// thrown when <paramref name="result"/> does not have a valid spawn <see cref="ModShimmerTypeID"/> or has a <see cref="ModShimmerResult.Count"/> that is not greater
	/// than 0
	/// </exception>
	public ModShimmer AddResult(ModShimmerResult result)
	{
		if (!result.ModShimmerTypeID.IsValidSpawnedType())
			throw new ArgumentException("ModShimmerTypeID must be a valid spawn type, check Example Mod for details", nameof(result));
		if (result.Count <= 0)
			throw new ArgumentException("A Count greater than 0 is required", nameof(result));

		Results.Add(result);
		return this;
	}

	/// <inheritdoc cref=" AddItemResult(int, int)"/>
	public ModShimmer AddModItemResult<T>(int stack) where T : ModItem
		=> AddItemResult(ModContent.ItemType<T>(), stack);

	/// <inheritdoc cref=" AddResult(ModShimmerResult)"/>
	/// <param name="type"> The <see cref="Item.type"/> of the <see cref="Item"/> </param>
	/// <param name="stack"> The amount of Item to be spawned </param>
	public ModShimmer AddItemResult(int type, int stack)
		=> AddResult(new(ModShimmerTypeID.Item, type, stack));

	/// <inheritdoc cref="AddNPCResult(int, int)"/>
	public ModShimmer AddModNPCResult<T>(int count) where T : ModNPC
		=> AddNPCResult(ModContent.NPCType<T>(), count);

	/// <inheritdoc cref=" AddResult(ModShimmerResult)"/>
	/// <param name="type"> The <see cref="NPC.type"/> of the <see cref="NPC"/> </param>
	/// <param name="count"> The amount of NPC to be spawned </param>
	public ModShimmer AddNPCResult(int type, int count)
		=> AddResult(new(ModShimmerTypeID.NPC, type, count));

	/// <inheritdoc cref=" AddResult(ModShimmerResult)"/>
	/// <param name="coinLuck"> The amount of coin luck to be added </param>
	public ModShimmer AddCoinLuckResult(int coinLuck)
		=> AddResult(new(ModShimmerTypeID.CoinLuck, -1, coinLuck));

	/// <inheritdoc cref=" AddResult(ModShimmerResult)"/>
	/// <param name="count"> The number of times <see cref="ModShimmerResult.CustomSpawner"/> will be called </param>
	/// <param name="spawnShimmer"> custom shimmer spawn function </param>
	/// <param name="customShimmerType"> unused by tModLoader, will still be in <see cref="ModShimmerResult"/> so can be used in <paramref name="spawnShimmer"/> </param>
	public ModShimmer AddCustomShimmerResult(int count = 1, ModShimmerResult.SpawnShimmer spawnShimmer = null, int customShimmerType = -1)
		=> AddResult(new ModShimmerResult(ModShimmerTypeID.Custom, customShimmerType, count) { CustomSpawner = spawnShimmer });

	#endregion AddResultMethods

	/// <inheritdoc cref="IgnoreVanillaItemConstraints"/>
	public ModShimmer DisableVanillaItemConstraints()
	{
		IgnoreVanillaItemConstraints = true;
		return this;
	}

	/// <inheritdoc cref="Priority"/>
	public ModShimmer SetPriority(int priority)
	{
		Priority = Math.Clamp(priority, -10, 10);
		return this;
	}

	/// <summary>
	/// Adds a delegate to <see cref="CanShimmerCallBacks"/> that will be called if the shimmer transformation succeeds
	/// </summary>
	public ModShimmer AddCanShimmerCallBack(CanShimmerCallBack callBack)
	{
		CanShimmerCallBacks += callBack;
		return this;
	}

	/// <summary>
	/// Adds a delegate to <see cref="ModifyShimmerCallBacks"/> that will be called before the transformation
	/// </summary>
	public ModShimmer AddModifyShimmerCallBack(ModifyShimmerCallBack callBack)
	{
		ModifyShimmerCallBacks += callBack;
		return this;
	}

	/// <summary>
	/// Adds a delegate to <see cref="OnShimmerCallBacks"/> that will be called if the shimmer transformation succeeds
	/// </summary>
	public ModShimmer AddOnShimmerCallBack(OnShimmerCallBack callBack)
	{
		OnShimmerCallBacks += callBack;
		return this;
	}

	/// <inheritdoc cref="Register(ModShimmerTypeID, int)"/>
	/// <exception cref="InvalidOperationException"> Thrown if this <see cref="ModShimmer"/> instance was not created from an Entity </exception>
	public void Register()
	{
		if (SourceStorageKey == null)
			throw new InvalidOperationException("The transformation must be created from an entity for the parameterless Register() to be used.");
		Register(SourceStorageKey.Value);
	}

	/// <inheritdoc cref="Register(ValueTuple{ModShimmerTypeID, int})"/>
	public void Register(ModShimmerTypeID modShimmerTypeID, int type)
		=> Register((modShimmerTypeID, type));

	/// <inheritdoc cref="Register(ModShimmerTypeID, int)"/>
	public void Register(IEnumerable<(ModShimmerTypeID, int)> sourceKeys)
	{
		foreach ((ModShimmerTypeID, int) ID in sourceKeys)
			Register(ID);
	}

	/// <summary>
	/// Finalizes transformation, adds to <see cref="Transformations"/>
	/// </summary>
	/// <exception cref="ArgumentException"> Thrown if <paramref name="sourceKey"/> field Item1 of type <see cref="ModShimmerTypeID"/> is an invalid source type </exception>
	public void Register((ModShimmerTypeID, int) sourceKey)
	{
		if (!sourceKey.Item1.IsValidSourceType())
			throw new ArgumentException("A valid source key for ModShimmerTypeID must be passed here", nameof(sourceKey));

		if (!Transformations.TryAdd(sourceKey, new() { this })) //Try add a new entry for the tuple
			Transformations[sourceKey].Add(this); // If it fails, entry exists, therefore add to list

		Transformations[sourceKey].Sort();
	}

	#endregion ControllerMethods

	#region Shimmering

	/// <summary>
	/// Checks if the <see cref="IModShimmerable"/> supplied can undergo a shimmer transformation, should not alter game state / read only
	/// </summary>
	/// <param name="shimmerable"> The <see cref="IModShimmerable"/> being shimmered </param>
	/// <returns>
	/// true if the following are all true in order
	/// <list type="number">
	/// <item/> All <see cref="Conditions"/> return true
	/// <item/> All added <see cref="CanShimmerCallBack"/> return true
	/// <item/> <see cref="IModShimmerable.CanShimmer"/> returns true for <paramref name="shimmerable"/>
	/// <item/> None of the results contain bone or lihzahrd brick while skeletron or golem are undefeated if <see cref="IgnoreVanillaItemConstraints"/> is false (default)
	/// <item/> The amount of empty NPC slots under slot 200 is less than the number of NPCs this transformation spawns
	/// </list>
	/// </returns>
	public bool CanModShimmer(IModShimmerable shimmerable)
		=> CanModShimmer_Transformation(shimmerable)
		&& shimmerable.CanShimmer();

	/// <summary>
	/// Checks the conditions for this transformation
	/// </summary>
	/// <returns>
	/// true if the following are all true in order
	/// <list type="number">
	/// <item/> All <see cref="Conditions"/> return true
	/// <item/>  All added <see cref="CanShimmerCallBack"/> return true
	/// <item/> None of the results contain bone or lihzahrd brick while skeletron or golem are undefeated if <see cref="IgnoreVanillaItemConstraints"/> is false (default)
	/// <item/> The amount of empty NPC slots under slot 200 is less than the number of NPCs this transformation spawns
	/// </list>
	/// </returns>
	public bool CanModShimmer_Transformation(IModShimmerable shimmerable)
		=> Conditions.All((condition) => condition.IsMet())
		&& (CheckCanShimmerCallBacks(shimmerable))
		&& (IgnoreVanillaItemConstraints || !Results.Any((result) => result.ModShimmerTypeID == ModShimmerTypeID.Item && (result.Type == ItemID.Bone && !NPC.downedBoss3 || result.Type == ItemID.LihzahrdBrick && !NPC.downedGolemBoss)))
		&& (GetCurrentAvailableNPCSlots() >= GetNPCSpawnCount());

	/// <summary>
	/// Checks all <see cref="CanShimmerCallBacks"/> for <paramref name="shimmerable"/>
	/// </summary>
	/// <returns> Returns true if all delegates in <see cref="CanShimmerCallBacks"/> return true </returns>
	public bool CheckCanShimmerCallBacks(IModShimmerable shimmerable)
	{
		foreach (CanShimmerCallBack callBack in CanShimmerCallBacks?.GetInvocationList()?.Cast<CanShimmerCallBack>() ?? Array.Empty<CanShimmerCallBack>()) {
			if (!callBack.Invoke(this, shimmerable))
				return false;
		}
		return true;
	}

	/// <summary>
	/// Checks every <see cref="ModShimmer"/> for this <see cref="IModShimmerable"/> and returns true when if finds one that passes
	/// <see cref="CanModShimmer_Transformation(IModShimmerable)"/>. <br/> Does not check <see cref="IModShimmerable.CanShimmer"/>
	/// </summary>
	/// <returns> True if there is a mod transformation this <see cref="IModShimmerable"/> could undergo </returns>
	public static bool AnyValidModShimmer(IModShimmerable shimmerable)
	{
		if (!Transformations.ContainsKey(shimmerable.RedirectedStorageKey))
			return false;

		foreach (ModShimmer modShimmer in Transformations[shimmerable.RedirectedStorageKey]) {
			if (modShimmer.CanModShimmer_Transformation(shimmerable))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Tries to complete a shimmer operation on the <see cref="IModShimmerable"/> passed, should not be called on multiplayer clients
	/// </summary>
	/// <param name="source"> The <see cref="IModShimmerable"/> to be shimmered </param>
	/// <returns> True if the transformation is successful, false if it is should fall through to vanilla as normal </returns>
	public static bool TryModShimmer(IModShimmerable source)
	{
		List<ModShimmer> transformations = Transformations.GetValueOrDefault(source.RedirectedStorageKey);
		if (!(transformations?.Count > 0)) // Invers to catch null
			return false;

		foreach (ModShimmer transformation in transformations) { // Loops possible transformations
			if (transformation.Results.Count > 0 && transformation.CanModShimmer(source)) { // Checks conditions and callback in CanShimmer
				ModShimmer copy = (ModShimmer)transformation.Clone(); // Make a copy
				copy.ModifyShimmerCallBacks?.Invoke(copy, source); // As to not be effected by any changes made here
				DoModShimmer(source, copy);
				return true;
			}
		}
		return false;
	}

	public const int SingleShimmerNPCSpawnCap = 50;

	public int GetNPCSpawnCount()
		=> Results.Sum((ModShimmerResult result) => result.ModShimmerTypeID == ModShimmerTypeID.NPC ? result.Count : 0);

	private static int GetCurrentAvailableNPCSlots() => NPC.GetAvailableAmountOfNPCsToSpawnUpToSlot(SingleShimmerNPCSpawnCap, 200);

	/// <summary>
	/// Called by <see cref="TryModShimmer(IModShimmerable)"/> once it finds a valid transformation
	/// </summary>
	public static void DoModShimmer(IModShimmerable source, ModShimmer transformation)
	{
		// 200 and 50 are the values vanilla uses for the highest slot to count with and the maximum NPCs to spawn in one transformation set
		int npcSpawnCount = transformation.GetNPCSpawnCount();
		int usableStack = npcSpawnCount != 0 ? Math.Min((int)MathF.Floor(GetCurrentAvailableNPCSlots() / (float)npcSpawnCount), source.Stack) : source.Stack;

		SpawnModShimmerResults(source, transformation, usableStack, out List<IModShimmerable> spawned); // Spawn results, output stack amount used
		source.Remove(usableStack); // Removed amount used
		transformation.OnShimmerCallBacks?.Invoke(transformation, source, spawned);

		ShimmerEffect(source.Center);
	}

	public static void SpawnModShimmerResults(IModShimmerable source, ModShimmer transformation, int stackUsed, out List<IModShimmerable> spawned)
	{
		spawned = new(); // List to be passed for onShimmerCallBacks
		foreach (ModShimmerResult result in transformation.Results)
			SpawnModShimmerResult(source, result, stackUsed, ref spawned); //Spawns the individual result, adds it to the list
	}

	/// <summary>
	/// Added the the velocity of the <see cref="IModShimmerable"/> to prevent stacking
	/// </summary>
	public static Vector2 GetShimmerSpawnVelocityModifier()
		// What vanilla does for items with more than one ingredient, flings stuff everywhere as it's never supposed to do more than 15
		// => new(count * (1f + count * 0.05f) * ((count % 2 == 0) ? -1 : 1), 0);
		=> new Vector2(Main.rand.Next(-30, 31), Main.rand.Next(-40, -15)) * 0.1f; //So we're using the random spawn values from shimmered items instead, items push each other away when in the shimmer state anyway, so this is more for NPCs

	/// <summary>
	/// Acts on the <paramref name="shimmerResult"/>. Result depends on <see cref="ModShimmerResult.ModShimmerTypeID"/>, usually spawns an <see cref="Item"/> or
	/// <see cref="NPC"/>. <br/> Does not despawn <paramref name="shimmerResult"/> or decrement <see cref="IModShimmerable.Stack"/>, use <see cref="IModShimmerable.Remove(int)"/>
	/// </summary>
	/// <param name="source"> The <see cref="IModShimmerable"/> that is shimmering, does not affect this </param>
	/// <param name="shimmerResult"> The Result to be spawned </param>
	/// <param name="stackUsed"> The amount of the <see cref="IModShimmerable"/> that is used, actual spawned amount will be <paramref name="stackUsed"/> * <see cref="ModShimmerResult.Count"/> </param>
	/// <param name="spawned"> A list of <see cref="IModShimmerable"/> passed to <see cref="OnShimmerCallBacks"/> </param>
	public static void SpawnModShimmerResult(IModShimmerable source, ModShimmerResult shimmerResult, int stackUsed, ref List<IModShimmerable> spawned)
	{
		int spawnTotal = shimmerResult.Count * stackUsed;
		switch (shimmerResult.ModShimmerTypeID) {
			case ModShimmerTypeID.Item: {
				while (spawnTotal > 0) {
					Item item = Main.item[Item.NewItem(source.GetSource_ForShimmer(), source.Center, source.Dimensions.ToVector2(), shimmerResult.Type)];
					item.stack = Math.Min(item.maxStack, spawnTotal);
					item.shimmerTime = 1f;
					item.shimmered = item.shimmerWet = item.wet = true;
					item.velocity = source.ShimmerVelocity + GetShimmerSpawnVelocityModifier();
					item.playerIndexTheItemIsReservedFor = Main.myPlayer;
					NetMessage.SendData(MessageID.SyncItemsWithShimmer, -1, -1, null, item.whoAmI, 1f); // net sync spawning the item

					spawned.Add(item);
					spawnTotal -= item.stack;
				}
				break;
			}

			case ModShimmerTypeID.NPC: {
				while (spawnTotal > 0) {
					NPC newNPC = NPC.NewNPCDirect(source.GetSource_ForShimmer(), source.Center, shimmerResult.Type);

					//syncing up some values that vanilla intentionally sets after SetDefaults() is NPC transformations, mostly self explanatory
					if (source is NPC nPC && shimmerResult.KeepVanillaTransformationConventions) {
						newNPC.extraValue = nPC.extraValue;
						newNPC.CopyInteractions(nPC);
						newNPC.spriteDirection = nPC.spriteDirection;

						if (nPC.value == 0f) // Statue stuff
							newNPC.value = 0f;
						newNPC.SpawnedFromStatue = nPC.SpawnedFromStatue;
						newNPC.shimmerTransparency = nPC.shimmerTransparency;
						newNPC.buffType = nPC.buffType[..]; // Pretty sure the manual way vanilla does it is actually the slowest way that isn't LINQ
						newNPC.buffTime = nPC.buffTime[..];
					}
					else {
						newNPC.shimmerTransparency = 1f;
					}
					newNPC.velocity = source.ShimmerVelocity + GetShimmerSpawnVelocityModifier();
					newNPC.TargetClosest();

					if (Main.netMode == NetmodeID.Server) {
						NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, newNPC.whoAmI);
						NetMessage.SendData(MessageID.NPCBuffs, -1, -1, null, newNPC.whoAmI);
						newNPC.netUpdate = true;
					}

					spawned.Add(newNPC);
					spawnTotal--;
				}

				break;
			}

			case ModShimmerTypeID.CoinLuck:
				Main.player[Main.myPlayer].AddCoinLuck(source.Center, spawnTotal);
				NetMessage.SendData(MessageID.ShimmerActions, -1, -1, null, 1, source.Center.X, source.Center.Y, spawnTotal);
				break;

			case ModShimmerTypeID.Custom:
				for (int i = 0; i < shimmerResult.Count; i++) {
					shimmerResult.CustomSpawner.Invoke(source, shimmerResult, source.ShimmerVelocity + GetShimmerSpawnVelocityModifier(), ref spawned);
				}
				break;
		}
	}

	/// <summary>
	/// Creates the shimmer effect checking either single player or server
	/// </summary>
	/// <param name="position"> The position to create the effect </param>
	public static void ShimmerEffect(Vector2 position)
	{
		if (Main.netMode == NetmodeID.SinglePlayer)
			Item.ShimmerEffect(position);
		else if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.ShimmerActions, -1, -1, null, 0, (int)position.X, (int)position.Y);
	}

	/// <summary>
	/// Creates a deep clone of <see cref="ModShimmer"/>.
	/// </summary>
	public object Clone()
		=> new ModShimmer() {
			SourceStorageKey = SourceStorageKey,
			Priority = Priority,
			Conditions = new List<Condition>(Conditions), // Technically I think the localization for the conditions can be changed
			Results = new List<ModShimmerResult>(Results), // List is new, ModShimmerResult is a readonly struct
			IgnoreVanillaItemConstraints = IgnoreVanillaItemConstraints, // Assigns by value
			CanShimmerCallBacks = (CanShimmerCallBack)CanShimmerCallBacks?.Clone(), // Stored values are immutable
			ModifyShimmerCallBacks = (ModifyShimmerCallBack)ModifyShimmerCallBacks?.Clone(),
			OnShimmerCallBacks = (OnShimmerCallBack)OnShimmerCallBacks?.Clone(),
		};

	public int CompareTo(ModShimmer other)
		=> other.Priority - Priority;

	#endregion Shimmering

	#region Helpers

	public bool ContainsResult((ModShimmerTypeID, int) type)
		=> Results.Any((result) => result.IsResult(type));

	public bool ContainsAnyResult((ModShimmerTypeID, int)[] types)
		=> types.Any((type) => ContainsResult(type));

	public bool ContainsResults((ModShimmerTypeID, int)[] types)
		=> types.All((type) => ContainsResult(type));

	#endregion Helpers
}

/// <summary>
/// Value used by <see cref="ModShimmerResult"/> to identify what type of <see cref="IModShimmerable"/> to spawn. <br/> When <see cref="Custom"/> it will try a null
/// checked call to the delegate <see cref="ModShimmerResult.CustomSpawner"/>
/// </summary>
public enum ModShimmerTypeID
{
	NPC,
	Item,
	CoinLuck,
	Custom,
}

/// <summary>
/// Extensions for <see cref="ModShimmerTypeID"/>
/// </summary>
public static class ModShimmerTypeIDExtensions
{
	/// <summary>
	/// <see cref="ModShimmerTypeID.NPC"/>, <see cref="ModShimmerTypeID.Item"/>, and <see cref="ModShimmerTypeID.Custom"/>
	/// </summary>
	public static bool IsValidSourceType(this ModShimmerTypeID id)
		=> id == ModShimmerTypeID.NPC || id == ModShimmerTypeID.Item || id == ModShimmerTypeID.Custom;

	/// <summary>
	/// <see cref="ModShimmerTypeID.NPC"/>, <see cref="ModShimmerTypeID.Item"/>, <see cref="ModShimmerTypeID.CoinLuck"/>, and <see cref="ModShimmerTypeID.Custom"/>
	/// </summary>
	public static bool IsValidSpawnedType(this ModShimmerTypeID id)
		=> id == ModShimmerTypeID.NPC || id == ModShimmerTypeID.Item || id == ModShimmerTypeID.CoinLuck || id == ModShimmerTypeID.Custom;
}

/// <summary>
/// A record representing the information to spawn an <see cref="IModShimmerable"/> during a shimmer transformation
/// </summary>
/// <param name="ModShimmerTypeID"> The type of shimmer operation this represents </param>
/// <param name="Type">
/// The type of the <see cref="IModShimmerable"/> to spawn, ignored when <paramref name="ModShimmerTypeID"/> is <see cref="ModShimmerTypeID.CoinLuck"/> or
/// <see cref="ModShimmerTypeID.Custom"/> although it is passed into <see cref="CustomSpawner"/> so can be used for custom logic
/// </param>
/// <param name="Count">
/// The number of this <see cref="IModShimmerable"/> to spawn, if <paramref name="ModShimmerTypeID"/> is <see cref="ModShimmerTypeID.CoinLuck"/> this is the coin luck
/// value, if <see cref="ModShimmerTypeID.Custom"/>, this is the amount of times <see cref="CustomSpawner"/> will be called
/// </param>
/// <param name="KeepVanillaTransformationConventions">
/// Keeps <see cref="ModShimmer"/> roughly in line with vanilla as far as base functionality goes, e.g. NPC's spawned via statues stay keep their spawned NPCs from a
/// statue when shimmered, if you have no reason to disable, don't
/// </param>
public readonly record struct ModShimmerResult(ModShimmerTypeID ModShimmerTypeID, int Type, int Count, bool KeepVanillaTransformationConventions)
{
	/// <inheritdoc cref="ModShimmerResult(ModShimmerTypeID, int, int, bool)"/>
	public ModShimmerResult() : this(default, default, default, default) { }

	/// <inheritdoc cref="ModShimmerResult(ModShimmerTypeID, int, int, bool)"/>
	public ModShimmerResult(ModShimmerTypeID ResultType, int Type, int Count) : this(ResultType, Type, Count, true) { }

	/// <summary>
	/// Called when an instance of <see cref="ModShimmerResult"/> is set as <see cref="ModShimmerTypeID.Custom"/>
	/// </summary>
	/// <param name="spawner"> The <see cref="IModShimmerable"/> that caused this transformation </param>
	/// <param name="shimmerResult"> The <see cref="ModShimmerResult"/> that caused this </param>
	/// <param name="velocity"> The velocity to spawn the new instance at </param>
	/// <param name="spawned"> A <see cref="List{T}"/> of <see cref="IModShimmerable"/> that the new instance of <see cref="IModShimmerable"/> should be added to </param>
	public delegate void SpawnShimmer(IModShimmerable spawner, ModShimmerResult shimmerResult, Vector2 velocity, ref List<IModShimmerable> spawned);

	/// <inheritdoc cref="SpawnShimmer"/>
	public SpawnShimmer CustomSpawner { get; init; } = null;

	public (ModShimmerTypeID, int) CompleteResultType => (ModShimmerTypeID, Type);

	public bool IsResult((ModShimmerTypeID, int) result)
		=> CompleteResultType == result;
}

/// <summary>
/// Marks a class to be used with <see cref="ModShimmer"/> as a source, most implementations for <see cref="NPC"/> and <see cref="Item"/> wrap normal values,
/// <see cref="ModShimmer.TryModShimmer(IModShimmerable)"/> must be called manually for implementing types
/// </summary>
public interface IModShimmerable
{
	/// <inheritdoc cref="Entity.Center"/>
	public abstract Vector2 Center { get; set; }

	/// <summary>
	/// Wraps <see cref="Entity.width"/> and <see cref="Entity.height"/>
	/// </summary>
	public abstract Point Dimensions { get; set; }

	/// <summary>
	/// Wraps <see cref="Entity.velocity"/>
	/// </summary>
	public abstract Vector2 ShimmerVelocity { get; set; }

	/// <summary>
	/// Internal as types implementing outside tModLoader should always use <see cref="ModShimmerTypeID.Custom"/>, use <see cref="StorageKey"/> for modder access
	/// </summary>
	internal virtual ModShimmerTypeID ModShimmerTypeID => ModShimmerTypeID.Custom;

	/// <summary>
	/// Should return a value from <see cref="ModShimmer.GetNextCustomShimmerID"/> when overriding that is constant for this type
	/// </summary>
	public abstract int ShimmerType { get; }

	/// <summary>
	/// Used as the key when both setting and retrieving for this <see cref="IModShimmerable"/> from <see cref="ModShimmer.Transformations"/>
	/// </summary>
	public (ModShimmerTypeID, int) StorageKey => (ModShimmerTypeID, ShimmerType);

	/// <summary>
	/// <see cref="StorageKey"/> passed through <see cref="ModShimmer.GetRedirectedKey(ValueTuple{ModShimmerTypeID, int})"/>
	/// </summary>
	public (ModShimmerTypeID, int) RedirectedStorageKey => ModShimmer.GetRedirectedKey(StorageKey);

	/// <summary>
	/// When this undergoes a <see cref="ModShimmer"/> this is the amount contained within one instance of the type, returns 1 for <see cref="NPC"/>, and
	/// <see cref="Item.stack"/> for <see cref="Item"/><br/> returns 1 by default
	/// </summary>
	public virtual int Stack => 1;

	/// <summary>
	/// Checks if this <see cref="IModShimmerable"/> can currently undergo a shimmer transformation. This includes both vanilla and <br/> Should not makes changes to game
	/// state. <br/> Treat as read only.
	/// </summary>
	/// <returns> True if the <see cref="IModShimmerable"/> currently has a valid shimmer operation it can use. </returns>
	public virtual bool CanShimmer() => true;

	/// <summary>
	/// Called at the end of shimmer
	/// </summary>
	public virtual void OnShimmer() { }

	/// <summary>
	/// Called once an entity Shimmers, int <see cref="Item"/> decrements <see cref="Item.stack"/>, handles despawning when <see cref="Stack"/> reaches 0
	/// </summary>
	public abstract void Remove(int amount);

	/// <summary>
	/// Returns <see cref="Entity.GetSource_Misc(string)"/> with passed value "shimmer" in for <see cref="NPC"/> and <see cref="Item"/>, used only for <see cref="ModShimmer"/>
	/// </summary>
	public abstract IEntitySource GetSource_ForShimmer();
}