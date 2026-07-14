using System.Drawing;
using System.Reflection;

namespace LiveYOLO
{
    internal static class IconLoader
    {
        public static Bitmap Get(string fileName) 
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("LiveYOLO.Resources." + fileName))
                return s == null ? null : new Bitmap(s);
        }
    }
}