using UnityEngine;

namespace ZeyWinAds.UI
{
    internal static class ZeyWinFont
    {
        private static readonly string[] PreferredFamilies =
        {
            "Roboto",
            "Noto Sans",
            "NotoSans",
            "Helvetica",
            "Arial"
        };

        private static Font _preferred;

        public static Font GetPreferred(int size = 32)
        {
            if (_preferred != null)
                return _preferred;

            try
            {
                _preferred = Font.CreateDynamicFontFromOSFont(PreferredFamilies, size);
            }
            catch
            {
                _preferred = null;
            }

            if (_preferred == null)
                _preferred = Resources.GetBuiltinResource<Font>("Arial.ttf");

            return _preferred;
        }
    }
}
