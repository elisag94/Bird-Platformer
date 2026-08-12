using TMPro;
using UnityEditor;
using UnityEngine;

namespace BirdPlatformer.EditorTools
{
    /// <summary>
    /// The one place colours and text-placement rules are written down.
    ///
    /// Sampled from the game's own art rather than invented: the glow on the
    /// horizon, the pale sand under it, the blue of the dusk sky, the red of
    /// the bird. Picking colours that already exist in the scene is what makes
    /// UI look like part of the game instead of a debug overlay pasted on top.
    ///
    /// Static and shared so the win screen and the main menu cannot drift
    /// apart — two copies of a palette stay in sync for about a week.
    /// </summary>
    public static class UiPalette
    {
        /// <summary>The glow on the horizon. Headlines, ranks, calls to action.</summary>
        public static readonly Color Sunset = Hex("#F2B27A");

        /// <summary>The pale sand. The brightest value on screen; use sparingly.</summary>
        public static readonly Color Cream = Hex("#F6EBDA");

        /// <summary>Cool blue-grey. Reference information that should recede.</summary>
        public static readonly Color Dusk = Hex("#B9BFD4");

        /// <summary>Dimmer still. Labels, hints, status lines.</summary>
        public static readonly Color Faint = Hex("#7E86A3");

        /// <summary>The bird's own red. Failure states.</summary>
        public static readonly Color Ember = Hex("#E0745A");

        /// <summary>Near-black navy, pulled from the top of the sky. Text sitting ON a light fill.</summary>
        public static readonly Color Midnight = Hex("#141B33");

        /// <summary>
        /// The same navy at partial opacity, for input backgrounds.
        ///
        /// Translucent rather than solid on purpose: a solid box reads as a
        /// window cut into the artwork, while a translucent one reads as glass
        /// laid over it. The stars stay faintly visible through the field,
        /// which is what stops it looking like default Unity UI.
        /// </summary>
        public static readonly Color Glass = new Color(0.078f, 0.106f, 0.200f, 0.66f);

        /// <summary>
        /// Position, size and type-set a TMP element in one call.
        ///
        /// Anchors are pinned to the parent's centre and only the offsets vary.
        /// Anchoring everything to the centre and moving it with
        /// anchoredPosition keeps a layout readable as a list of numbers —
        /// mixed anchors are where UI stops being predictable.
        /// </summary>
        public static void Place(TMP_Text text, Vector2 position, Vector2 size, float fontSize,
                                 TextAlignmentOptions alignment)
        {
            if (text == null)
            {
                Debug.LogWarning("[UiPalette] Skipped a missing text object.");
                return;
            }

            RectTransform rect = (RectTransform)text.transform;

            Undo.RecordObject(rect, "Lay out UI");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Undo.RecordObject(text, "Lay out UI");
            text.fontSize = fontSize;
            text.alignment = alignment;

            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(text);
        }

        public static void Tint(TMP_Text text, Color color)
        {
            if (text == null)
            {
                return;
            }

            Undo.RecordObject(text, "Apply colour");
            text.color = color;
            EditorUtility.SetDirty(text);
        }

        /// <summary>
        /// Overload for legacy UI.Text, which is a completely different
        /// component from TMP_Text despite doing the same job. Level01's
        /// headlines are still legacy; there is no reason to convert them just
        /// to change a colour.
        /// </summary>
        public static void Tint(Transform legacyText, Color color)
        {
            if (legacyText == null)
            {
                return;
            }

            UnityEngine.UI.Text text = legacyText.GetComponent<UnityEngine.UI.Text>();
            if (text == null)
            {
                return;
            }

            Undo.RecordObject(text, "Apply colour");
            text.color = color;
            EditorUtility.SetDirty(text);
        }

        public static void Tint(UnityEngine.UI.Graphic graphic, Color color)
        {
            if (graphic == null)
            {
                return;
            }

            Undo.RecordObject(graphic, "Apply colour");
            graphic.color = color;
            EditorUtility.SetDirty(graphic);
        }

        /// <summary>
        /// The default UI sprites, loaded the way TMPro's own menu handler does.
        /// GetBuiltinExtraResource reaches into art shipped inside the editor
        /// binary rather than anything in Assets/ — which is why a brand new
        /// project has working button and input-field art with nothing imported.
        /// </summary>
        public static TMP_DefaultControls.Resources StandardResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
        }

        public static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.white;
        }
    }
}
