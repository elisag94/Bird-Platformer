using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BirdPlatformer.EditorTools
{
    /// <summary>
    /// Finding things in a scene, and writing to private [SerializeField]
    /// fields. Both are chores every one of these tools needs, and neither is
    /// interesting enough to repeat.
    /// </summary>
    public static class SceneLookup
    {
        /// <summary>Depth-first search of every root object in the scene, by name.</summary>
        public static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }

                Transform found = FindRecursive(root.transform, name);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        public static Transform FindRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }

                Transform found = FindRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static TMP_Text FindChildText(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        /// <summary>
        /// Create a TMP text object under a parent if one of that name is not
        /// already there. Idempotent, which is what lets these tools be re-run
        /// without piling up duplicates.
        /// </summary>
        public static TMP_Text EnsureText(GameObject parent, string name)
        {
            TMP_Text existing = FindChildText(parent, name);
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent.transform, false);
            go.layer = parent.layer;

            TMP_Text text = go.AddComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            return text;
        }

        /// <summary>
        /// The legacy UI.Text that is a direct child of a panel — the headline,
        /// not a button label (button labels live one level deeper, inside the
        /// button).
        /// </summary>
        public static Transform FindLegacyHeadline(GameObject panel)
        {
            foreach (Transform child in panel.transform)
            {
                if (child.GetComponent<UnityEngine.UI.Text>() != null &&
                    child.GetComponent<UnityEngine.UI.Button>() == null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Assign to a private [SerializeField] field.
        ///
        /// SerializedObject is the same path the Inspector itself uses. There
        /// is no other way to reach a private field from outside the class, and
        /// there should not be — the alternative is making every field public
        /// so tooling can poke at it, which is how a codebase loses its
        /// encapsulation one convenience at a time.
        /// </summary>
        public static void SetReference(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[SceneLookup] No serialized field '{propertyName}' — did the script change?");
                return;
            }

            property.objectReferenceValue = value;
        }

        /// <summary>Shared guard: these tools all edit scenes, and play mode throws scene edits away.</summary>
        public static bool BlockedByPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                return false;
            }

            EditorUtility.DisplayDialog(
                "Exit Play Mode",
                "Stop play mode first. Scene edits made while playing are discarded when you press Stop.",
                "OK");
            return true;
        }
    }
}
