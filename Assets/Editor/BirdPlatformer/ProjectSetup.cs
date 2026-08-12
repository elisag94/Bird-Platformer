using UnityEditor;
using UnityEngine;

namespace BirdPlatformer.EditorTools
{
    /// <summary>
    /// Project-wide settings this game depends on, written down as code so they
    /// are reproducible rather than folklore in someone's head.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>
        /// Unity 6 refuses plain-HTTP UnityWebRequests by default. That is the
        /// cause of "Non-secure network connections disabled in Player
        /// Settings" in the console, and of the score silently never leaving
        /// the game.
        ///
        /// The rule is a sensible default and wrong here: the whole stack is
        /// http://bird.local, with no certificate and nothing terminating TLS,
        /// so there is nothing to be secure with. Revisit when the game moves
        /// behind CloudFront and gets HTTPS for free — at which point this can
        /// go back to Not Allowed and stay there.
        ///
        /// Worth noting that this lives in ProjectSettings/ProjectSettings.asset,
        /// which means it must be COMMITTED. Leave it out and CI builds revert
        /// to the default and fail to reach the API, which is a miserable thing
        /// to debug from a build log.
        /// </summary>
        [MenuItem("Tools/Bird Platformer/Allow HTTP (for http://bird.local)", false, 60)]
        public static void AllowInsecureHttp()
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Player Settings → Allow downloads over HTTP = Always allowed. " +
                      "Commit ProjectSettings/ProjectSettings.asset.");
        }
    }
}
