using UnityEngine;
using UnityEngine.UI;
using System;
using GTk.Bindings;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameSLRSample : MonoBehaviour
{
    private int enabledFrame;
    [Header("UI Components")]
    public RawImage previewImage;
    public TMP_Text resultText;

    [Header("Preview")]
    [Tooltip("Auto decides from the graphics API. Override only if Auto is wrong on this device.")]
    public SLRTk.SlrtkPreviewOrientation.FlipOverride forceFlipPreview =
        SLRTk.SlrtkPreviewOrientation.FlipOverride.Auto;

    [Tooltip("Mirror the preview horizontally, like a selfie camera. Display only -- the " +
             "recogniser sees the unmirrored frame regardless, so this cannot change results.")]
    public bool mirrorPreview = true;

    [Header("Recognition")]
    [Tooltip("Which gallery backs recognition. Naming it is load-bearing -- see SetupPipeline.")]
    public string gallery = "popsign";
    public int topK = 5;

    [Header("Overlay")]
    [Tooltip("Draw the landmark overlay on the preview. The hands are drawn light blue " +
             "(left) and light green (right).")]
    public bool enableOverlay = true;

    [Tooltip("Also draw the pose skeleton. Turn off for a hands-only overlay.")]
    public bool drawPose = true;

    [Header("Landmark capture (debugging only)")]
    [Tooltip("Write per-frame landmarks to HDF5 at Capture File Path. OFF by default -- this " +
             "is a research/debug artefact, not something a shipped app should write. It also " +
             "gates the sink server-side, so with this off the graph never builds the writer.")]
    public bool enableHdf5 = false;

    [Tooltip("Destination for landmark capture. Empty = do not write. Only used when Enable Hdf5 is on.")]
    public string captureFilePath = "";

    [Header("Demo")]
    [Tooltip("Trigger recognition on a screen tap / left click. Turn OFF when a host drives " +
             "recognition from its own event -- a game action, a button -- via TriggerRecognizer().")]
    public bool tapToTrigger = true;

    [Tooltip("Log the presence probes (hand / nose / shoulder) at ~2 Hz. A demo aid -- a real " +
             "UI binds to Is*Present() directly. Off by default to avoid console spam.")]
    public bool logPresence = false;

    [SerializeField] private string[] _signs = Array.Empty<string>();

    public string[] signs {
        get { return _signs; }
        set {
            _signs = value;
            UpdateVocab();
        }
    }

    // The pipeline and its texture live for the whole app, not for one scene.
    //
    // They used to be per-component and disposed in OnDestroy -- the obvious thing to write
    // in Unity, and it does not work: after the first camera scene the preview never came
    // back. Opening a second pipeline over a closed one, and rebinding AddDynamicSLR to a
    // freshly created texture, is not a cycle the native side survives. So: open once, keep
    // the same texture pointer bound for the app's life, and manage only RECORDING around
    // scene changes and app pauses. Reopening also costs seconds of camera warm-up each time.
    private static DynamicSLRPipelineService pipeline;
    private static RenderTexture dynamicSlrTexture;
    private static bool recording;
    private static bool quitHooked;

    // Computed, not a bool field: with a static pipeline a per-instance flag goes stale the
    // moment a scene reload instantiates a fresh component against an already-open pipeline.
    private bool isPipelineReady { get { return pipeline != null; } }

    private float _lastPresenceLog = 0f;
    // When the clip buffer was last emptied. The capture window for a trigger is the span
    // from here to now, so logging it separates "the sign was not in the window" from "the
    // sign was there and the model missed it".
    private float lastClearTime = -1f;

    /// <summary>Per-trigger diagnostics, compiled out entirely unless SLRTK_DEBUG_LOGS is
    /// defined -- a production player pays nothing, not even the string interpolation,
    /// because [Conditional] removes the call and its arguments at the call site. Gated
    /// rather than deleted, deliberately: genuine failures are NOT routed through here and
    /// still report unconditionally.</summary>
    [System.Diagnostics.Conditional("SLRTK_DEBUG_LOGS")]
    private static void Dbg(string message)
    {
        Debug.Log("[SLRDBG] " + message);
    }

    void Start()
    {
        if (previewImage == null)
        {
            Debug.LogError("[InteractiveSLRSample] Please assign a RawImage to display the camera preview.");
            return;
        }

        Debug.Log("[InteractiveSLRSample] Initializing Dynamic SLR Pipeline...");

        // Wait for GPU initialization before starting.
        NativePipelineHooks.OnGpuInitialized += SetupPipeline;
    }

    /// <summary>Point this scene's RawImage at the shared texture. Runs on every activation,
    /// because the component can be re-enabled -- or replaced by a scene reload -- long after
    /// the texture was created.</summary>
    private void AttachPreview()
    {
        if (previewImage == null || dynamicSlrTexture == null) return;
        previewImage.texture = dynamicSlrTexture;

        var uv = SLRTk.SlrtkPreviewOrientation.UvRect(forceFlipPreview);

        // Mirror horizontally so the preview behaves like a bathroom mirror rather than a
        // photo of the user: front cameras deliver an unmirrored image and apps flip it for
        // display by convention -- raising a hand and watching the opposite side of the
        // screen move is disorienting when you are trying to copy a sign. DISPLAY ONLY: this
        // changes the uvRect the RawImage samples and nothing else, so it cannot affect
        // recognition in either direction.
        if (mirrorPreview) uv = new Rect(uv.x + uv.width, uv.y, -uv.width, uv.height);

        // NO aspect crop here, deliberately. A centre-crop computed from rectTransform.rect
        // does not match the panel's real size during gameplay -- it samples a narrow
        // vertical sliver and stretches it. The orientation rule is measured good across
        // device/API combinations; flip and mirror only.
        previewImage.uvRect = uv;
    }

    private void ResumeRecording()
    {
        if (pipeline == null || recording) return;
        // Empty string, not null: OutputHdf5File runs the value through
        // ProtoPreconditions.CheckNotNull, so null throws rather than disabling capture.
        string path = (enableHdf5 && !string.IsNullOrEmpty(captureFilePath)) ? captureFilePath : "";
        pipeline.StartRecording(new DynamicSLRStartRequest { OutputHdf5File = path });
        // Clear so frames from before a pause cannot bleed into the next recognition.
        pipeline.ClearBuffer(new DynamicSLREmpty());
        recording = true;
        Dbg($"recording resumed -> {(string.IsNullOrEmpty(path) ? "no capture file" : path)}, buffer cleared");
    }

    /// <summary>Stop capturing and drop whatever is buffered, without closing the pipeline.
    /// Called when the component is disabled and when the app is paused, so frames from one
    /// scene -- or from whatever was in front of the camera while the app sat in the
    /// background -- can never be part of the next recognition.</summary>
    private void SuspendRecording(string why)
    {
        if (pipeline == null || !recording) return;
        pipeline.StopRecording(new DynamicSLREmpty());
        pipeline.ClearBuffer(new DynamicSLREmpty());
        recording = false;
        Dbg($"recording suspended ({why}), buffer cleared");
    }

    void OnEnable()
    {
        enabledFrame = Time.frameCount;
        // Null until the GPU callback has run; SetupPipeline handles the first time.
        if (pipeline == null) return;
        AttachPreview();
        ResumeRecording();
        UpdateVocab();
    }

    void OnDisable()
    {
        SuspendRecording("component disabled");
    }

    void OnApplicationPause(bool paused)
    {
        // Do not hold the camera while the app is backgrounded.
        if (paused) SuspendRecording("app paused");
        else { AttachPreview(); ResumeRecording(); }
    }

    private void SetupPipeline()
    {
        try
        {
            if (pipeline != null)
            {
                // Already open from an earlier scene -- reattach and resume, never reopen.
                AttachPreview();
                ResumeRecording();
                UpdateVocab();
                Dbg("reusing existing pipeline (no reopen)");
                return;
            }

            HookQuit();

            // Setup Render Texture. DontUnloadUnusedAsset because nothing in a scene
            // references this -- only a static field does -- and a scene transition would
            // otherwise be free to unload it out from under the bound native pointer.
            dynamicSlrTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.BGRA32);
            dynamicSlrTexture.enableRandomWrite = true;
            dynamicSlrTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            dynamicSlrTexture.Create();
            AttachPreview();

            pipeline = new DynamicSLRPipelineService();

            // Open explicitly BEFORE binding the texture -- this is the only place
            // PipelineBaseOptions reaches the graph builder. AddDynamicSLR and StartRecording
            // used to open implicitly with DEFAULT options when the pipeline was closed, so a
            // failed or missing OpenPipeline silently gave you a different graph and reported
            // success.
            //
            // Naming the gallery is load-bearing: BuildDynamicSlrGraph dispatches on the
            // PRESENCE of the sign_recognition submessage, and Modality/Language/TopK are all
            // proto defaults, so a message carrying only those serialises to zero bytes,
            // arrives absent, and silently builds the LEGACY DynamicISLR recogniser -- a
            // different model with its own dictionary. Gallery has no default, so setting it
            // guarantees the submessage carries real content over the FFI.
            var baseOptions = new CoreTk.Pipeline.PipelineBaseOptions
            {
                // Gates the HDF5 sink server-side: with this false the graph never builds the
                // writer, so no capture happens regardless of the path. Off by default.
                EnableHdf5 = enableHdf5,
                // Draw the overlay so the hand landmarks (light blue left / light green right)
                // are visible. Every OverlayOptions field defaults true, so DrawPose is set
                // explicitly -- a config that meant "hands only" by omission would serialise
                // to zero bytes and be ignored.
                EnableOverlay = enableOverlay,
                Overlay = new CoreTk.Pipeline.OverlayOptions { DrawPose = drawPose },
                SignRecognition = new CoreTk.Pipeline.SignRecognitionOptions
                {
                    Modality = CoreTk.Pipeline.SignRecognitionOptions.Types.Modality.Isolated,
                    Language = CoreTk.Pipeline.SignRecognitionOptions.Types.Language.Asl,
                    Gallery  = gallery,
                    TopK     = topK,
                },
            };
            var openResp = pipeline.OpenPipeline(new DynamicSLROpenRequest { Options = baseOptions });
            if (openResp != null && !openResp.Success)
            {
                Debug.LogError("[InteractiveSLRSample] OpenPipeline failed: " + openResp.ErrorMessage);
                return;
            }

            // Bind DynamicSLR Texture.
            IntPtr nativePtr = dynamicSlrTexture.GetNativeTexturePtr();
            pipeline.AddDynamicSLR(new AddDynamicSLRRequest {
                TargetGraph = "GTk/Pipelines/DynamicSLR",
                TexturePtr = (ulong)nativePtr.ToInt64(),
                ViewPtr = 0,
                // A GL texture name carries no dimensions, so without these the native
                // viewport falls back to the CAMERA's size -- e.g. 1920x1080 rendered into a
                // 1280x720 framebuffer, which GL clips to the bottom-left: a 1.5x corner crop,
                // not a stretch. Metal and D3D read the size off the texture and ignore them,
                // so this only bites on GL (Android).
                TextureWidth = (uint)dynamicSlrTexture.width,
                TextureHeight = (uint)dynamicSlrTexture.height
            });

            // Start the camera + pipeline.
            ResumeRecording();
            UpdateVocab();

            Debug.Log("[InteractiveSLRSample] Pipeline successfully started and vocabulary subset applied.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[InteractiveSLRSample] Failed to start pipeline: {ex.Message}\n{ex.StackTrace}");
        }
    }

    void UpdateVocab() {
        if (pipeline == null || _signs.Length == 0) return;
        var vocabRequest = new DynamicSLRSetVocabRequest();
        foreach (string sign in _signs)
        {
            vocabRequest.Vocab.Add(sign);
        }
        pipeline.SetVocabulary(vocabRequest);
    }

    /// <summary>Recognise the buffered sign now and return the top label, or null when the
    /// pipeline is not ready, recognition fails, or nothing is classified.</summary>
    ///
    /// <remarks>The caller-invokable entry point. A host that fires recognition from its own
    /// event -- a game's shoot, a button -- rather than a screen tap needs the label BACK,
    /// not just a UI field updated (PopSign calls SetSign(slrEngine.TriggerRecognizer())).
    /// resultText is still written when one is wired, but a scene with no label recognises
    /// and returns the same value. Update()'s tap path calls this same method, so the tap
    /// and caller paths cannot drift.</remarks>
    public string TriggerRecognizer()
    {
        if (pipeline == null || !isPipelineReady)
        {
            Debug.LogWarning($"[InteractiveSLRSample] trigger ignored: pipeline={(pipeline == null ? "null" : "ok")} ready={isPipelineReady}");
            return null;
        }

        float window = lastClearTime < 0f ? -1f : Time.realtimeSinceStartup - lastClearTime;
        Dbg($"---- trigger ---- capture window = {(window < 0f ? "first (since start)" : window.ToString("F2") + "s")}");
        Dbg($"vocabulary ({(signs == null ? 0 : signs.Length)}): [{(signs == null ? "" : string.Join(", ", signs))}]");
        if (window >= 0f && window < 1.5f)
            Dbg($"window {window:F2}s is far short of a signed clip (reference clips are ~2s); expect a meaningless query.");

        DynamicSLRRecognitionResponse response = pipeline.TriggerRecognition(new DynamicSLREmpty());

        try
        {
            if (response == null || !response.Success)
            {
                Debug.LogError($"[InteractiveSLRSample] recognition FAILED: {(response?.ErrorMessage ?? "null response")}");
                if (resultText != null) resultText.text = "Error recognizing";
                return null;
            }

            int n = response.Classifications.Count;
            Dbg($"classifications returned: {n}");
            if (n == 0)
            {
                Dbg("no classifications -- graph produced no candidate for this clip");
                if (resultText != null) resultText.text = "No sign detected";
                return null;
            }

            // Full ranked list. If every candidate scores nearly the same, the query
            // embedding is not discriminating and the label is arbitrary.
            var ranked = response.Classifications.OrderByDescending(c => c.Score).ToList();
            foreach (var c in ranked)
                Dbg($"   {c.Label} = {c.Score:F4}");
            if (ranked.Count > 1)
            {
                float spread = ranked[0].Score - ranked[ranked.Count - 1].Score;
                Dbg($"top-vs-bottom spread = {spread:F4}" +
                          (spread < 0.05f ? "  <-- FLAT: candidates indistinguishable" : ""));
            }

            var topMatch = ranked[0];
            Dbg($"WINNER: {topMatch.Label} ({topMatch.Score:F4})");
            // Return the label even when resultText is unwired -- a caller needs it back;
            // only the on-screen text is optional.
            if (resultText != null) resultText.text = $"Sign: {topMatch.Label} (Score: {topMatch.Score:F2})";
            return topMatch.Label;
        }
        finally
        {
            // Always clear -- even on the early returns above -- so a failed or empty
            // recognition never leaves the previous clip's frames in the buffer.
            pipeline.ClearBuffer(new DynamicSLREmpty());
            lastClearTime = Time.realtimeSinceStartup;
            Dbg("buffer cleared");
        }
    }

    void Update()
    {
        if (!isPipelineReady) return;

        bool tapped = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) tapped = true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) tapped = true;
#else
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) tapped = true;
#endif

        // Trigger on mouse click or screen tap -- the same path a caller uses. Off for hosts
        // that drive recognition themselves (see tapToTrigger).
        if (tapToTrigger && tapped && Time.frameCount > enabledFrame &&
            !EventSystem.current.IsPointerOverGameObject())
        {
            var words = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<WordBank>();
            if (string.IsNullOrEmpty(words.CurrentWord)) return;
            if (signs != words.Words) signs = words.Words;
            Debug.Log("[InteractiveSLRSample] Screen tapped! Triggering recognition...");
            string sign = TriggerRecognizer();
            if (sign == null)
            {
                Debug.LogWarning("[GameSLRSample] No sign was recognized.");
                return;
            }
            words.SubmitSign(sign);
        }

        // Presence probes for UI feedback, polled at ~2 Hz. The graph updates these every
        // frame; move a hand / your nose / a shoulder in and out of frame and watch them
        // flip. These are the FFI functions a real UI binds to. Hand is same-side: a hand
        // only counts together with its own-side shoulder in frame. Logging is opt-in
        // (logPresence) so it does not spam a shipping host's console.
        if (logPresence && Time.time - _lastPresenceLog >= 0.5f)
        {
            _lastPresenceLog = Time.time;
            bool hand     = pipeline.IsHandPresent().Present;
            bool nose     = pipeline.IsNosePresent().Present;
            bool shoulder = pipeline.IsShoulderPresent().Present;
            Debug.Log($"[Presence] hand={hand} nose={nose} shoulder={shoulder}");
        }
    }

    void OnDestroy()
    {
        // Deliberately does NOT close the pipeline or release the texture. OnDestroy runs on
        // every scene change, and closing here is exactly what left the preview black on
        // every camera scene after the first. Real teardown belongs to app quit (CloseForGood).
        NativePipelineHooks.OnGpuInitialized -= SetupPipeline;
    }

    private static void HookQuit()
    {
        if (quitHooked) return;
        Application.quitting += CloseForGood;
        quitHooked = true;
    }

    /// <summary>Full teardown, once, when the app itself is going away.</summary>
    private static void CloseForGood()
    {
        if (pipeline != null && recording)
        {
            pipeline.StopRecording(new DynamicSLREmpty());
            recording = false;
        }
        if (pipeline != null)
        {
            pipeline.Dispose();
            pipeline = null;
        }
        if (dynamicSlrTexture != null)
        {
            dynamicSlrTexture.Release();
            dynamicSlrTexture = null;
        }
        Dbg("pipeline closed (app quit)");
    }
}
