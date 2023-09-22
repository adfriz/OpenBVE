namespace Formats.OpenBve
{
	internal struct IndexedValue
	{
		internal int Index;

		internal string Value;

		internal IndexedValue(int index, string value)
		{
			Index = index;
			Value = value;
		}
	}
}
