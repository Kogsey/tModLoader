namespace Terraria.ModLoader;

public abstract class DamageableEntity : Entity
{
	public abstract int[] BuffType { get; }
	public abstract int[] BuffTime { get; }
	public abstract bool[] BuffImmune { get; }

	/// <summary>
	/// Returns the number of buffs in <see cref="BuffType"/>
	/// </summary>
	public abstract int CountBuffs();
	public abstract void AddBuff(int buffType, int time, bool quiet = false);

	public void AddBuff<T>(int time, bool quiet = false) where T : ModBuff
		=> AddBuff(ModContent.BuffType<T>(), time, quiet);

	public abstract void ClearBuff(int buffType);

	public void ClearBuff<T>() where T : ModBuff
		=> ClearBuff(ModContent.BuffType<T>());

	public abstract void DelBuff(int buffIndex);

	/// <summary> Returns whether or not this <see cref="DamageableEntity"/> currently has a (de)buff of the provided type. </summary>
	public bool HasBuff(int buffType)
		=> FindBuffIndex(buffType) != -1;

	/// <inheritdoc cref="HasBuff(int)"/>
	public bool HasBuff<T>() where T : ModBuff
		=> HasBuff(ModContent.BuffType<T>());

	/// <summary>
	/// Returns the index of <paramref name="buffType"/> in <see cref="BuffType"/> or -1 if not found.
	/// </summary>
	public abstract int FindBuffIndex(int buffType);

	/// <inheritdoc cref="FindBuffIndex(int)"/>
	public void FindBuffIndex<T>() where T : ModBuff
		=> FindBuffIndex(ModContent.BuffType<T>());
}