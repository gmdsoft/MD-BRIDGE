namespace MD.BRIDGE.Utils
{
    public static class StringExtensions
    {
        public static bool IsNullOrEmpty(this string s)
        {
            if (s == null)
                return true;

            return s.Length == 0;
        }
    }
}
