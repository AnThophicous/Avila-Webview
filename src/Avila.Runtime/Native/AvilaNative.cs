using System.Runtime.InteropServices;
using System.Text;

namespace Avila.Runtime.Native;

internal static class AvilaNative
{
    [DllImport("AvilaNative.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int avila_fast_hash(byte[] data, int length, StringBuilder output, int outputLength);

    public static bool TryFastHash(byte[] data, out string hash)
    {
        hash = "";
        var output = new StringBuilder(65);

        try
        {
            var result = avila_fast_hash(data, data.Length, output, output.Capacity);
            if (result != 0)
            {
                return false;
            }

            hash = output.ToString();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
