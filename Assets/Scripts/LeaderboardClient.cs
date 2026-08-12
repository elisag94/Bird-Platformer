using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The only thing in the game that talks to the leaderboard API.
///
/// Transport only: it knows about URLs, JSON and HTTP status codes, and knows
/// nothing about panels, win screens or game state. Callers hand it data and a
/// pair of callbacks. Keeping the boundary here means the day the API moves to
/// a different host (Week 4, when the game goes to CloudFront and the API does
/// not), exactly one file changes.
///
/// WHY UnityWebRequest AND NOT HttpClient
/// A WebGL build runs inside the browser's sandbox. There are no raw sockets
/// and no System.Net.Http — the only way out is the browser's own fetch/XHR
/// machinery, and UnityWebRequest is Unity's wrapper over it. Code using
/// HttpClient compiles happily in the Editor and then fails in the build,
/// which is the worst possible order to find out.
///
/// WHY COROUTINES AND NOT async/await
/// WebGL is single-threaded. A coroutine yields back to the frame loop and
/// resumes when the response lands, so the game keeps rendering while the
/// request is in flight — no thread, no blocking.
/// </summary>
public class LeaderboardClient : MonoBehaviour
{
    public static LeaderboardClient Instance { get; private set; }

    [Header("Editor / standalone only")]
    [Tooltip("Where the API lives when NOT running as a Web build. Ignored in a browser build, " +
             "which always uses a relative URL. Requires 'minikube tunnel' and the bird.local " +
             "entry in /etc/hosts.")]
    [SerializeField] private string editorBaseUrl = "http://bird.local";

    [Tooltip("Seconds before a request is abandoned. Long enough for a cold pod, short enough " +
             "that the win screen is not stuck waiting forever.")]
    [SerializeField] private int timeoutSeconds = 10;

    /// <summary>
    /// The prefix put in front of "/api/...".
    ///
    /// In the browser this is deliberately EMPTY. The game files and the API
    /// are served from the same origin — the Ingress routes "/" to the nginx
    /// pods and "/api" to the Flask pods — so "/api/scores" resolves against
    /// whatever host the page was loaded from. One build then works on
    /// bird.local, on a laptop, and on a real domain later, with nothing baked
    /// in and no CORS to configure.
    ///
    /// The catch, and the reason for the split: in the Editor there is no page
    /// and therefore no origin to resolve against, so a relative URL simply
    /// fails. Only the Editor and standalone builds need a host spelled out.
    /// </summary>
    private string BaseUrl
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return string.Empty;
#else
            return string.IsNullOrWhiteSpace(editorBaseUrl)
                ? "http://bird.local"
                : editorBaseUrl.TrimEnd('/');
#endif
        }
    }

    // Creates itself before the first scene loads, exactly like
    // GameManagerBootstrap. Nothing to drag into a scene, nothing to forget to
    // re-add after a scene is rebuilt.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<LeaderboardClient>() != null)
        {
            return;
        }

        GameObject go = new GameObject("LeaderboardClient");
        go.AddComponent<LeaderboardClient>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Read in every build so the field is never "assigned but unused" in a
        // WebGL compile, and so a blank inspector value can't produce a null.
        if (editorBaseUrl == null)
        {
            editorBaseUrl = string.Empty;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ----------------------------------------------------------------------
    // Wire format
    //
    // JsonUtility maps PUBLIC FIELDS to JSON keys by exact name, which is why
    // these are snake_case rather than C# PascalCase: they have to match
    // api/app.py character for character. Properties are invisible to it, and
    // so are private fields without [SerializeField]. Unknown keys in a
    // response are ignored rather than throwing, which is the forgiving
    // behaviour you want from a client.
    // ----------------------------------------------------------------------

    [Serializable]
    public class ScoreSubmission
    {
        public string player_name;
        public string level_id;
        public int duration_ms;
        public int deaths;
    }

    [Serializable]
    public class ScoreResponse
    {
        public int id;
        public string player_name;
        public string level_id;
        public int duration_ms;
        public int deaths;
        public int rank;
        public bool personal_best;
    }

    [Serializable]
    public class LeaderboardEntry
    {
        public int rank;
        public string player_name;
        public int duration_ms;
        public int deaths;
        public string achieved_at;
    }

    [Serializable]
    public class LeaderboardResponse
    {
        public string level_id;
        public int count;
        public LeaderboardEntry[] entries;
    }

    [Serializable]
    private class ApiError
    {
        public string error;
        public string detail;
    }

    // ----------------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------------

    /// <summary>POST /api/scores. Calls onSuccess with rank and personal-best, or onError with a displayable message.</summary>
    public void SubmitScore(string playerName, string levelId, int durationMs, int deaths,
                            Action<ScoreResponse> onSuccess, Action<string> onError)
    {
        ScoreSubmission body = new ScoreSubmission
        {
            player_name = playerName,
            level_id = levelId,
            duration_ms = durationMs,
            deaths = deaths,
        };

        StartCoroutine(PostJson("/api/scores", JsonUtility.ToJson(body), onSuccess, onError));
    }

    /// <summary>GET /api/scores/top. Fastest first, best run per player.</summary>
    public void GetTopScores(string levelId, int limit,
                             Action<LeaderboardResponse> onSuccess, Action<string> onError)
    {
        // The trailing _t is a cache-buster, and it exists because of a
        // difference between the Editor and the browser that is easy to lose a
        // morning to.
        //
        // In the Editor, UnityWebRequest talks to the network itself and there
        // is no HTTP cache in the way. In a WebGL build there is no socket —
        // the request becomes a browser fetch, and the browser is free to
        // answer a repeated GET from cache. The API sends no Cache-Control
        // header at all, which does not mean "do not cache"; it means "decide
        // for yourself", and browsers may heuristically reuse the response.
        //
        // The symptom would be a leaderboard that refuses to update in the
        // deployed game while working perfectly in the Editor. A changing
        // query string makes each request a different URL, so there is nothing
        // to reuse. Flask ignores the extra parameter.
        //
        // The tidier fix belongs on the server — `Cache-Control: no-store` on
        // the API responses — and is worth doing next time the image is
        // rebuilt. This one works without redeploying anything.
        string path = $"/api/scores/top?level_id={UnityWebRequest.EscapeURL(levelId)}" +
                      $"&limit={limit}" +
                      $"&_t={DateTime.UtcNow.Ticks}";

        StartCoroutine(GetJson(path, onSuccess, onError));
    }

    // ----------------------------------------------------------------------
    // Plumbing
    // ----------------------------------------------------------------------

    private IEnumerator PostJson<T>(string path, string json,
                                    Action<T> onSuccess, Action<string> onError) where T : class
    {
        // Constructed by hand rather than with UnityWebRequest.Post so the
        // Content-Type is unambiguous. The convenience overloads have a long
        // history of sending form-encoded bodies when you meant JSON, and
        // Flask's get_json() then quietly returns None.
        using (UnityWebRequest request = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            Complete(request, onSuccess, onError);
        }
    }

    private IEnumerator GetJson<T>(string path, Action<T> onSuccess, Action<string> onError) where T : class
    {
        using (UnityWebRequest request = UnityWebRequest.Get(BaseUrl + path))
        {
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            Complete(request, onSuccess, onError);
        }
    }

    private static void Complete<T>(UnityWebRequest request, Action<T> onSuccess, Action<string> onError)
        where T : class
    {
        // ConnectionError   — never reached the server (DNS, tunnel down, timeout)
        // ProtocolError     — the server answered, with 4xx or 5xx
        // Those are different problems and deserve different messages: one is
        // "the cluster isn't reachable", the other is "the cluster said no".
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            onError?.Invoke("Leaderboard unreachable.");
            Debug.LogWarning($"[LeaderboardClient] connection error on {request.url}: {request.error}");
            return;
        }

        if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            string detail = ExtractErrorDetail(request.downloadHandler?.text);
            onError?.Invoke(detail ?? $"Leaderboard error ({request.responseCode}).");
            Debug.LogWarning($"[LeaderboardClient] HTTP {request.responseCode} on {request.url}: {request.downloadHandler?.text}");
            return;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke("Leaderboard request failed.");
            Debug.LogWarning($"[LeaderboardClient] {request.result} on {request.url}: {request.error}");
            return;
        }

        T parsed;
        try
        {
            parsed = JsonUtility.FromJson<T>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            // A 200 whose body is not the JSON we expect usually means the
            // request was answered by something other than the API — the game's
            // own nginx returning index.html, for instance, which is what a
            // missing /api Ingress rule looks like from in here.
            Debug.LogWarning($"[LeaderboardClient] could not parse response from {request.url}: {exception.Message}");
            onError?.Invoke("Unexpected response from leaderboard.");
            return;
        }

        if (parsed == null)
        {
            onError?.Invoke("Empty response from leaderboard.");
            return;
        }

        onSuccess?.Invoke(parsed);
    }

    /// <summary>
    /// Pull the human-readable reason out of the API's error envelope, so a
    /// 400 shows "duration_ms must be at least 3000" instead of "400".
    /// Server-side validation messages are the most useful thing on screen
    /// when something is wrong; hiding them behind a generic string is a habit
    /// worth breaking.
    /// </summary>
    private static string ExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            ApiError error = JsonUtility.FromJson<ApiError>(body);
            if (error == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(error.detail))
            {
                return error.detail;
            }

            return string.IsNullOrWhiteSpace(error.error) ? null : error.error;
        }
        catch
        {
            return null;
        }
    }
}
