namespace Concentus.Structs;

internal class VorbisLayout
{
	internal int nb_streams;

	internal int nb_coupled_streams;

	internal byte[] mapping;

	internal static readonly VorbisLayout[] vorbis_mappings = new VorbisLayout[8]
	{
		new VorbisLayout(1, 0, new byte[1]),
		new VorbisLayout(1, 1, new byte[2] { 0, 1 }),
		new VorbisLayout(2, 1, new byte[3] { 0, 2, 1 }),
		new VorbisLayout(2, 2, new byte[4] { 0, 1, 2, 3 }),
		new VorbisLayout(3, 2, new byte[5] { 0, 4, 1, 2, 3 }),
		new VorbisLayout(4, 2, new byte[6] { 0, 4, 1, 2, 3, 5 }),
		new VorbisLayout(4, 3, new byte[7] { 0, 4, 1, 2, 3, 5, 6 }),
		new VorbisLayout(5, 3, new byte[8] { 0, 6, 1, 2, 3, 4, 5, 7 })
	};

	internal VorbisLayout(int streams, int coupled_streams, byte[] map)
	{
		nb_streams = streams;
		nb_coupled_streams = coupled_streams;
		mapping = map;
	}
}
