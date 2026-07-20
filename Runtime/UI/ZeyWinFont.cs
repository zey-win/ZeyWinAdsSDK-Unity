using UnityEngine;

namespace ZeyWinAds.UI
{
    internal static class ZeyWinFont
    {
        private static readonly string[] PreferredFamilies =
        {
            // Latin / Cyrillic / Greek / Vietnamese
            "Roboto",
            "Noto Sans",
            "NotoSans",
            "Helvetica",
            "Arial",

            // Chinese (Simplified / Traditional)
            "Noto Sans CJK SC",
            "PingFang SC",
            "Noto Sans CJK TC",
            "PingFang TC",

            // Japanese
            "Noto Sans CJK JP",
            "Hiragino Sans",

            // Korean
            "Noto Sans CJK KR",
            "Apple SD Gothic Neo",

            // Thai
            "Noto Sans Thai",
            "Thonburi",

            // Hebrew (glyphs render correctly; right-to-left ordering is not
            // supported by legacy UI.Text, so reading direction is unaffected)
            "Noto Sans Hebrew",
            "Arial Hebrew"
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
