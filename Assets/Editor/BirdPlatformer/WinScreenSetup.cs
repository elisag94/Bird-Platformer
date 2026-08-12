using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BirdPlatformer.EditorTools
{
    /// <summary>
    /// Lays out and colours the Level01 win screen.
    ///
    /// This exists because every TMP object Unity creates is born at position
    /// (0,0) in an identical 200x50 box, so a win panel built by hand starts
    /// life with the time, the rank and the leaderboard all printed through
    /// each other. Eight RectTransforms is exactly the amount of fiddly,
    /// repetitive work worth automating and no more.
    ///
    /// Safe to run repeatedly: it finds what exists and creates only what is
    /// missing.
    /// </summary>
    public static class WinScreenSetup
    {
        private const string LevelScenePath = "Assets/Scenes/Level01.unity";

        // Vertical layout in pixels from screen centre. Level01's Canvas is set
        // to Constant Pixel Size, so these are literal pixels that do not
        // rescale with the window — which is the only reason they can be
        // written down as constants at all. MainMenu's Canvas uses a different
        // scale mode, so its numbers are in a different unit entirely.
        private const float HeadlineY = 200f;
        private const float TimeY = 150f;
        private const float RankY = 110f;
        private const float StatusY = 80f;
        private const float BoardTopY = 60f;
        private const float ButtonsY = -180f;

        [MenuItem("Tools/Bird Platformer/Level01 — Lay Out Win Screen", false, 20)]
        public static void LayOutWinScreen()
        {
            if (SceneLookup.BlockedByPlayMode())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != LevelScenePath)
            {
                if (!EditorUtility.DisplayDialog("Open Level01?",
                        $"This tool edits {LevelScenePath}. Open it now?", "Open Level01", "Cancel"))
                {
                    return;
                }

                scene = EditorSceneManager.OpenScene(LevelScenePath);
            }

            GameObject winPanel = SceneLookup.Find(scene, "WinPanel");
            if (winPanel == null)
            {
                Debug.LogError("[WinScreen] No GameObject named 'WinPanel' in Level01.");
                return;
            }

            TMP_Text status = SceneLookup.EnsureText(winPanel, "LeaderboardStatus");
            TMP_Text winTime = SceneLookup.FindChildText(winPanel, "WinTime");
            TMP_Text winRank = SceneLookup.FindChildText(winPanel, "WinRank");
            TMP_Text board = SceneLookup.FindChildText(winPanel, "LeaderboardText");

            UiPalette.Place(winTime, new Vector2(0f, TimeY), new Vector2(420f, 50f), 34f, TextAlignmentOptions.Center);
            UiPalette.Place(winRank, new Vector2(0f, RankY), new Vector2(420f, 34f), 22f, TextAlignmentOptions.Center);
            UiPalette.Place(status, new Vector2(0f, StatusY), new Vector2(420f, 26f), 16f, TextAlignmentOptions.Center);

            if (board != null)
            {
                UiPalette.Place(board, new Vector2(0f, BoardTopY), new Vector2(420f, 200f), 15f, TextAlignmentOptions.Top);

                // Pinned by its TOP edge rather than its centre, so a list of
                // three names and a list of ten both start on the same line
                // instead of creeping upward as rows arrive.
                RectTransform rect = (RectTransform)board.transform;
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, BoardTopY);
            }

            // The legacy "REUNITED!" headline stays a legacy Text component —
            // no reason to convert it — but its RectTransform was full-stretch,
            // which drew the words straight through everything else.
            if (SceneLookup.FindLegacyHeadline(winPanel) is RectTransform headline)
            {
                Undo.RecordObject(headline, "Move headline");
                headline.anchorMin = new Vector2(0.5f, 0.5f);
                headline.anchorMax = new Vector2(0.5f, 0.5f);
                headline.pivot = new Vector2(0.5f, 0.5f);
                headline.sizeDelta = new Vector2(420f, 60f);
                headline.anchoredPosition = new Vector2(0f, HeadlineY);
                EditorUtility.SetDirty(headline);
            }

            MoveButton(winPanel, "RestartButton", new Vector2(-90f, ButtonsY));
            MoveButton(winPanel, "MenuButton", new Vector2(90f, ButtonsY));

            LeaderboardPanel panel = winPanel.GetComponent<LeaderboardPanel>();
            if (panel == null)
            {
                panel = Undo.AddComponent<LeaderboardPanel>(winPanel);
                Debug.Log("[WinScreen] Added LeaderboardPanel to WinPanel.");
            }

            SerializedObject panelObject = new SerializedObject(panel);
            SceneLookup.SetReference(panelObject, "statusText", status);
            if (board != null)
            {
                SceneLookup.SetReference(panelObject, "entriesText", board);
            }
            panelObject.ApplyModifiedProperties();

            GameObject levelUi = SceneLookup.Find(scene, "LevelUI");
            LevelUIController controller = levelUi != null ? levelUi.GetComponent<LevelUIController>() : null;
            if (controller != null)
            {
                SerializedObject controllerObject = new SerializedObject(controller);
                SceneLookup.SetReference(controllerObject, "leaderboardPanel", panel);
                if (winRank != null)
                {
                    SceneLookup.SetReference(controllerObject, "winRankText", winRank);
                }
                controllerObject.ApplyModifiedProperties();
            }

            ApplyPalette(scene, winPanel, panel);
            Save(scene, "Win screen laid out, coloured and wired.");
        }

        /// <summary>
        /// Colour pass on its own, so the palette can be tweaked without
        /// disturbing a layout you are happy with.
        /// </summary>
        [MenuItem("Tools/Bird Platformer/Level01 — Apply Colour Theme", false, 21)]
        public static void ApplyColourTheme()
        {
            if (SceneLookup.BlockedByPlayMode())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            GameObject winPanel = SceneLookup.Find(scene, "WinPanel");
            if (winPanel == null)
            {
                Debug.LogError("[WinScreen] No 'WinPanel' in the open scene. Open Level01 first.");
                return;
            }

            ApplyPalette(scene, winPanel, winPanel.GetComponent<LeaderboardPanel>());
            Save(scene, "Colour theme applied.");
        }

        private static void ApplyPalette(Scene scene, GameObject winPanel, LeaderboardPanel panel)
        {
            // The headline was black on a night sky — legible only because the
            // panel happens to sit over the pale horizon. Anything that reads
            // by accident stops reading the moment the art changes.
            UiPalette.Tint(SceneLookup.FindLegacyHeadline(winPanel), UiPalette.Sunset);

            UiPalette.Tint(SceneLookup.FindChildText(winPanel, "WinTime"), UiPalette.Cream);
            UiPalette.Tint(SceneLookup.FindChildText(winPanel, "WinRank"), UiPalette.Sunset);
            UiPalette.Tint(SceneLookup.FindChildText(winPanel, "LeaderboardStatus"), UiPalette.Faint);

            // The board recedes deliberately. It is reference information, not
            // the result — the player's own time is what they came to see, so
            // it gets the brightest value and the list sits behind it.
            UiPalette.Tint(SceneLookup.FindChildText(winPanel, "LeaderboardText"), UiPalette.Dusk);

            // The HUD readout lives on the Canvas, not the win panel, because
            // it has to be visible while playing.
            GameObject hud = SceneLookup.Find(scene, "HudTime");
            if (hud != null)
            {
                UiPalette.Tint(hud.GetComponent<TMP_Text>(), UiPalette.Cream);
            }

            GameObject losePanel = SceneLookup.Find(scene, "LosePanel");
            if (losePanel != null)
            {
                UiPalette.Tint(SceneLookup.FindLegacyHeadline(losePanel), UiPalette.Ember);
            }

            if (panel != null)
            {
                SerializedObject panelObject = new SerializedObject(panel);
                SerializedProperty highlight = panelObject.FindProperty("highlightColor");
                if (highlight != null)
                {
                    highlight.colorValue = UiPalette.Sunset;
                }
                panelObject.ApplyModifiedProperties();
            }
        }

        private static void MoveButton(GameObject parent, string name, Vector2 position)
        {
            if (parent.transform.Find(name) is RectTransform rect)
            {
                Undo.RecordObject(rect, "Move button");
                rect.anchoredPosition = position;
                EditorUtility.SetDirty(rect);
            }
        }

        private static void Save(Scene scene, string message)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[WinScreen] {message} Scene saved.");
        }
    }
}
