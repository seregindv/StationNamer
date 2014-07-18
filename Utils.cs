using System.Text;

namespace StationNamer
{
    static internal class Utils
    {
        public static string Left(string s, int len)
        {
            if (s.Length > len)
                return s.Substring(0, len);
            return s;
        }

        public static string Decode(string s)
        {
            return Encoding.UTF8.GetString(Encoding.Default.GetBytes(s));
        }
    }
}