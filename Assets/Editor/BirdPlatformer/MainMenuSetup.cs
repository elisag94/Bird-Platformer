using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BirdPlatformer.EditorTools
{
    /// <summary>
    /// Builds and styles the main menu: title, name field, status line, and the
    /// Play button.
    ///
    /// A note on units. MainMenu's Canvas is Scale With Screen Size against a
    /// 1920x1080 reference, whereas Level01's is Constant Pixel Size. The
    /// numbers here therefore mean something different from the ones in
    /// WinScreenSetup despite looking identical — these are "pixels if the
    /// window were 1920 wide", those are "pixels, full stop". Two scale modes
    /// in one project is a genuine inconsistency; unifying them is a worthwhile
    /// tidy-up for a quiet afternoon.
    /// </summary>
    public static class MainMenuSetup
    {
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

        private const float TitleY = 180f;
        private const float LabelY = 78f;
        private const float InputY = 26f;
        private const float StatusY = -22f;
        private const float ButtonY = -100f;

        [MenuItem("Tools/Bird Platformer/MainMenu — Build Name Field", false, 40)]
        public static void BuildNameField()
        {
            if (SceneLookup.BlockedByPlayMode())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MenuScenePath)
            {
                if (!EditorUtility.DisplayDialog("Open MainMenu?",
                        $"This tool edits {MenuScenePath}. Open it now?", "Open MainMenu", "Cancel"))
                {
                    return;
                }

                scene = EditorSceneManager.OpenScene(MenuScenePath);
            }

            GameObject canvas = SceneLookup.Find(scene, "Canvas");
            if (canvas == null)
            {
                Debug.LogError("[MainMenu] No 'Canvas' in MainMenu.");
                return;
            }

            // Remove a stray placeholder from an earlier attempt: an object
            // called NameInput that is not actually an input field.
            Transform stray = canvas.transform.Find("NameInput");
            if (stray != null && stray.GetComponent<TMP_InputField>() == null)
            {
                Debug.LogWarning("[MainMenu] Removed a 'NameInput' that was not an input field. Ctrl+Z restores it.");
                Undo.DestroyObjectImmediate(stray.gameObject);
            }

            TMP_InputField input = canvas.GetComponentInChildren<TMP_InputField>(true);
            if (input == null)
            {
                // TMP's own factory, which is the exact call Unity's GameObject
                // menu makes one line underneath. Driving the menu by name
                // instead is fragile — Unity 6.5 renamed "GameObject/UI/..." to
                // "GameObject/UI (Canvas)/...", and a menu path is a label, not
                // an API.
                //
                // Building the field by hand is worse still: it is five nested
                // objects — background, viewport with a mask, placeholder, text,
                // caret — and reproducing that is how you get a field that
                // looks right and silently refuses keystrokes.
                GameObject created = TMP_DefaultControls.CreateInputField(UiPalette.StandardResources());
                Undo.RegisterCreatedObjectUndo(created, "Create NameInput");
                created.name = "NameInput";
                created.transform.SetParent(canvas.transform, false);
                created.layer = canvas.layer;

                input = created.GetComponent<TMP_InputField>();
                if (input == null)
                {
                    Debug.LogError("[MainMenu] TMP_DefaultControls did not produce a TMP_InputField.");
                    return;
                }
            }

            StyleInputField(input);

            TMP_Text title = SceneLookup.EnsureText(canvas, "Title");
            if (string.IsNullOrEmpty(title.text))
            {
                title.text = "BIRD PLATFORMER";
            }
            UiPalette.Place(title, new Vector2(0f, TitleY), new Vector2(900f, 90f), 64f, TextAlignmentOptions.Center);
            UiPalette.Tint(title, UiPalette.Sunset);
            Undo.RecordObject(title, "Style title");
            title.fontStyle = FontStyles.Bold;
            // Letter-spacing is what separates a title from a label. It costs
            // one property and does more for the look than another font would.
            title.characterSpacing = 14f;

            TMP_Text label = SceneLookup.EnsureText(canvas, "NameLabel");
            if (string.IsNullOrEmpty(label.text))
            {
                label.text = "ENTER YOUR NAME";
            }
            UiPalette.Place(label, new Vector2(0f, LabelY), new Vector2(500f, 30f), 18f, TextAlignmentOptions.Center);
            UiPalette.Tint(label, UiPalette.Faint);
            Undo.RecordObject(label, "Style label");
            label.characterSpacing = 8f;

            TMP_Text status = SceneLookup.EnsureText(canvas, "NameStatus");
            UiPalette.Place(status, new Vector2(0f, StatusY), new Vector2(600f, 26f), 14f, TextAlignmentOptions.Center);
            UiPalette.Tint(status, UiPalette.Faint);

            StylePlayButton(canvas);
            Wire(scene, input, status);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MainMenu] Name field built, styled and wired. Scene saved.");
        }

        /// <summary>
        /// The white box problem.
        ///
        /// TMP's default input field ships with an opaque white background,
        /// which is correct for a form on a grey editor window and completely
        /// wrong on a dusk sky — it reads as a hole punched through the
        /// artwork. Replacing it with translucent navy makes it read as glass
        /// laid over the scene instead: the stars stay faintly visible through
        /// the field, and nothing about the layout changed.
        /// </summary>
        private static void StyleInputField(TMP_InputField input)
        {
            RectTransform rect = (RectTransform)input.transform;
            Undo.RecordObject(rect, "Lay out name field");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(360f, 52f);
            rect.anchoredPosition = new Vector2(0f, InputY);

            Undo.RecordObject(input, "Style name field");
            input.characterLimit = 32;      // matches MAX_NAME_LENGTH in api/app.py
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.pointSize = 22f;

            // A caret in the accent colour is a small thing that makes a field
            // feel designed rather than defaulted.
            input.customCaretColor = true;
            input.caretColor = UiPalette.Sunset;
            input.caretWidth = 2;
            input.selectionColor = new Color(UiPalette.Sunset.r, UiPalette.Sunset.g, UiPalette.Sunset.b, 0.35f);

            UiPalette.Tint(input.GetComponent<Image>(), UiPalette.Glass);

            if (input.textComponent != null)
            {
                UiPalette.Tint(input.textComponent, UiPalette.Cream);
                Undo.RecordObject(input.textComponent, "Style input text");
                input.textComponent.fontSize = 22f;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                Undo.RecordObject(placeholder, "Style placeholder");
                placeholder.text = "your name";
                placeholder.fontSize = 20f;
                placeholder.fontStyle = FontStyles.Italic;
                UiPalette.Tint(placeholder, UiPalette.Faint);
            }

            EditorUtility.SetDirty(input);
        }

        /// <summary>
        /// The Play button gets the accent colour as a FILL rather than as
        /// text. One filled element on a screen of text is what tells the eye
        /// where to go — and the menu had nothing playing that role, which is
        /// part of why it looked like default Unity UI.
        /// </summary>
        private static void StylePlayButton(GameObject canvas)
        {
            Transform buttonTransform = canvas.transform.Find("Button")
                                        ?? canvas.transform.Find("PlayButton");
            if (buttonTransform == null)
            {
                Debug.LogWarning("[MainMenu] No Play button found under Canvas; skipped styling it.");
                return;
            }

            if (buttonTransform is RectTransform buttonRect)
            {
                Undo.RecordObject(buttonRect, "Lay out play button");
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.sizeDelta = new Vector2(260f, 60f);
                buttonRect.anchoredPosition = new Vector2(0f, ButtonY);
                EditorUtility.SetDirty(buttonRect);
            }

            UiPalette.Tint(buttonTransform.GetComponent<Image>(), UiPalette.Sunset);

            TMP_Text label = buttonTransform.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Undo.RecordObject(label, "Style play button label");
                label.text = "PLAY";
                label.fontSize = 26f;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 6f;
                label.alignment = TextAlignmentOptions.Center;
                // Dark text on the light fill, not the other way round. The
                // navy is pulled from the top of the sky, so the button belongs
                // to the same picture.
                label.color = UiPalette.Midnight;
                EditorUtility.SetDirty(label);
            }
            else
            {
                UiPalette.Tint(buttonTransform.Find("Text (Legacy)"), UiPalette.Midnight);
            }
        }

        private static void Wire(Scene scene, TMP_InputField input, TMP_Text status)
        {
            GameObject controllerObject = SceneLookup.Find(scene, "MainMenuController");
            MainMenuController controller = controllerObject != null
                ? controllerObject.GetComponent<MainMenuController>()
                : Object.FindAnyObjectByType<MainMenuController>();

            if (controller == null)
            {
                Debug.LogWarning("[MainMenu] No MainMenuController found — wire the two slots yourself.");
                return;
            }

            SerializedObject serialized = new SerializedObject(controller);
            SceneLookup.SetReference(serialized, "nameInput", input);
            SceneLookup.SetReference(serialized, "nameStatusText", status);
            serialized.ApplyModifiedProperties();
        }
    }
}
