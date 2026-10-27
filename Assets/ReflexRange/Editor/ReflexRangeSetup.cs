using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Management;

/// <summary>
/// Editor-only helper that sets up the whole project from the "Reflex Range" menu:
/// 1. imports the XR Interaction Toolkit samples and TextMeshPro resources,
/// 2. configures Android + OpenXR for Meta Quest 3,
/// 3. builds the game scene, the UI and the target prefabs ("Neon Arcade" style),
/// 4. builds the APK.
/// Every step can be run again safely.
/// </summary>
public static class ReflexRangeSetup
{
    const string Root = "Assets/ReflexRange";
    const string ScenePath = Root + "/Scenes/ReflexRange.unity";
    const string ApkPath = "Builds/ReflexRangeVR.apk";
    const string XriPackage = "com.unity.xr.interaction.toolkit";

    // Pistol model (Glock from Sketchfab): real size, barrel along +Z, top along +Y
    const string PistolFolder = Root + "/Models/Pistol";
    const string PistolModelPath = PistolFolder + "/glock_model.fbx";
    static readonly Vector3 GripPoint = new Vector3(0f, -0.01f, -0.05f);    // where the hand holds it
    static readonly Vector3 MuzzlePoint = new Vector3(0f, 0.053f, 0.102f);  // front of the barrel

    // Neon Arcade palette
    static readonly Color Cyan = Hex("#3BE8FF");
    static readonly Color Magenta = Hex("#FF3DA5");
    static readonly Color Yellow = Hex("#FFE45C");
    static readonly Color Green = Hex("#2FD06A");
    static readonly Color Ink = Hex("#07070D");
    static readonly Color Chip = Hex("#12121F");
    static readonly Color RingTrack = Hex("#1C1C33");
    static readonly Color Muted = Hex("#A7A9C4");
    static readonly Color White = Hex("#F4F5FF");

    // ---------------------------------------------------------------- 1. Samples

    [MenuItem("Reflex Range/1. Import XR Samples and TextMeshPro")]
    public static void ImportSamples()
    {
        foreach (Sample sample in Sample.FindByPackage(XriPackage, null))
        {
            bool wanted = sample.displayName == "Starter Assets" || sample.displayName == "XR Interaction Simulator";
            if (wanted && !sample.isImported)
            {
                Debug.Log("[ReflexRange] Importing sample: " + sample.displayName);
                sample.Import(Sample.ImportOptions.HideImportWindow | Sample.ImportOptions.OverridePreviousImports);
            }
        }

        if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
        {
            Debug.Log("[ReflexRange] Importing TMP Essential Resources");
            UnityEditor.AssetPackage.Package.Import("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage", false);
        }

        AssetDatabase.Refresh();
        Debug.Log("[ReflexRange] Step 1 done");
    }

    // ---------------------------------------------------------------- 2. Project settings

    [MenuItem("Reflex Range/2. Configure Project for Meta Quest 3")]
    public static void ConfigureProject()
    {
        // Android player settings recommended for Quest
        PlayerSettings.companyName = "Mirjalol";
        PlayerSettings.productName = "Reflex Range VR";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.mirjalol.reflexrangevr");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        EditorUserBuildSettings.buildAppBundle = false;

        // URP: 4x MSAA makes edges look much smoother in a headset, HDR off saves GPU time
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            EditorUtility.SetDirty(urp);
        }

        // XR Plug-in Management: use the OpenXR loader on Android
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }

        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        XRGeneralSettings androidSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (!XRPackageMetadataStore.AssignLoader(androidSettings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android)
            && !androidSettings.Manager.activeLoaders.Any(loader => loader.GetType().Name == "OpenXRLoader"))
            Debug.LogError("[ReflexRange] Could not enable the OpenXR loader for Android");

        // OpenXR features: Meta Quest support + Quest controller profiles
        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        string[] featureIds =
        {
            "com.unity.openxr.feature.metaquest",          // Meta Quest Support
            "com.unity.openxr.feature.input.oculustouch",  // Oculus Touch controllers
            "com.unity.openxr.feature.input.metaquestplus" // Quest 3 Touch Plus controllers
        };
        foreach (string id in featureIds)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (feature == null)
            {
                Debug.LogError("[ReflexRange] OpenXR feature not found: " + id);
                continue;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[ReflexRange] Step 2 done");
    }

    // ---------------------------------------------------------------- 3. Scene

    [MenuItem("Reflex Range/3. Build Game Scene")]
    public static void BuildScene()
    {
        foreach (string folder in new[] { "Materials", "Prefabs", "Scenes", "UI" })
            Directory.CreateDirectory($"{Root}/{folder}");
        AssetDatabase.Refresh();

        // Open the new scene first: opening a scene unloads assets that nothing references yet
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Style style = CreateStyle();

        // Materials
        Material floorMat = CreateMaterial("Floor", new Color(0.07f, 0.07f, 0.11f), smoothness: 0.5f);
        Material wallMat = CreateMaterial("Wall", new Color(0.09f, 0.09f, 0.15f));
        Material counterMat = CreateMaterial("Counter", new Color(0.12f, 0.12f, 0.18f), smoothness: 0.4f);
        Material neonCyanMat = CreateMaterial("Neon Cyan", Cyan, unlit: true);
        Material neonMagentaMat = CreateMaterial("Neon Magenta", Magenta, unlit: true);
        Material laserMat = CreateMaterial("Laser", new Color(1f, 0.15f, 0.1f), unlit: true);
        Material flashMat = CreateMaterial("Muzzle Flash", new Color(1f, 0.85f, 0.3f), unlit: true);
        Material redMat = CreateMaterial("Target Red", new Color(0.85f, 0.1f, 0.1f));
        Material whiteMat = CreateMaterial("Target White", new Color(0.95f, 0.95f, 0.95f));
        Material blueMat = CreateMaterial("Target Blue", new Color(0.15f, 0.45f, 0.95f));
        Material yellowMat = CreateMaterial("Target Yellow", new Color(1f, 0.85f, 0.1f));
        Material greenMat = CreateMaterial("Target Green", new Color(0.1f, 0.75f, 0.25f));

        // Prefabs
        GameObject targetGo = BuildTargetVisual("Target", 0.35f, redMat, whiteMat, redMat);
        targetGo.AddComponent<Target>();
        Target targetPrefab = SavePrefab(targetGo).GetComponent<Target>();

        GameObject movingGo = BuildTargetVisual("Moving Target", 0.35f, blueMat, whiteMat, yellowMat);
        SetField(movingGo.AddComponent<Target>(), "points", 20);
        movingGo.AddComponent<MovingTarget>();
        Target movingTargetPrefab = SavePrefab(movingGo).GetComponent<Target>();

        HitPopup hitPopupPrefab = BuildHitPopupPrefab(style);

        // Night sky, soft flat ambient light and one directional light
        RenderSettings.skybox = CreateNightSky();
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.45f);
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.9f, 0.92f, 1f);
        light.intensity = 1.1f;
        light.shadows = LightShadows.Hard;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Environment: dark floor and wall with neon lines, and the counter
        var environment = new GameObject("Environment").transform;
        CreateBlock("Floor", PrimitiveType.Plane, environment, new Vector3(0f, 0f, 8f), new Vector3(4f, 1f, 4f), floorMat);
        CreateBlock("Back Wall", PrimitiveType.Cube, environment, new Vector3(0f, 4f, 19f), new Vector3(40f, 8f, 0.5f), wallMat);
        CreateBlock("Wall Neon", PrimitiveType.Cube, environment, new Vector3(0f, 0.3f, 18.7f), new Vector3(40f, 0.08f, 0.05f), neonMagentaMat, keepCollider: false);
        CreateBlock("Left Neon Line", PrimitiveType.Cube, environment, new Vector3(-5f, 0.005f, 9f), new Vector3(0.08f, 0.01f, 17f), neonCyanMat, keepCollider: false);
        CreateBlock("Right Neon Line", PrimitiveType.Cube, environment, new Vector3(5f, 0.005f, 9f), new Vector3(0.08f, 0.01f, 17f), neonCyanMat, keepCollider: false);
        CreateBlock("Counter", PrimitiveType.Cube, environment, new Vector3(0f, 0.45f, 0.55f), new Vector3(1.4f, 0.9f, 0.45f), counterMat);
        CreateBlock("Counter Neon", PrimitiveType.Cube, environment, new Vector3(0f, 0.9f, 0.325f), new Vector3(1.4f, 0.015f, 0.015f), neonCyanMat, keepCollider: false);

        // XR player rig from the XR Interaction Toolkit Starter Assets
        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
        GameObject rig = InstantiatePrefab("XR Origin (XR Rig)", "Starter Assets/Prefabs");
        MakeRigStationary(rig);
        Transform head = rig.GetComponentInChildren<Camera>(true).transform;

        // Keyboard/mouse simulator for testing in the Editor. "EditorOnly" = not included in the APK.
        GameObject simulator = InstantiatePrefab("XR Interaction Simulator", "XR Interaction Simulator");
        simulator.tag = "EditorOnly";

        // Pistol on the counter
        GameObject pistol = BuildPistol(laserMat, flashMat);
        pistol.transform.SetPositionAndRotation(new Vector3(0.2f, 0.93f, 0.5f), Quaternion.Euler(0f, 0f, 90f));

        // START target with its sign
        GameObject startTarget = BuildTargetVisual("Start Target", 0.5f, greenMat, whiteMat, greenMat);
        startTarget.AddComponent<StartTarget>();
        startTarget.transform.position = new Vector3(0f, 1.6f, 6f);
        AddWorldText("Sign", startTarget.transform, new Vector3(0f, 0.85f, 0f), style.display, style.displayGlow, 3.2f, White, "SHOOT TO START");
        AddWorldText("Hint", startTarget.transform, new Vector3(0f, -0.8f, 0f), style.body, null, 2.4f, Muted,
            "<color=#FFE45C>GRIP</color> grab     <color=#FFE45C>TRIGGER</color> fire");

        // Scoreboard (world-space UI) to the front-left of the player, turned to face them.
        // The canvas sits inside an anchor object because a canvas resets its own x/y position.
        ScoreboardUI scoreboard = BuildScoreboard(style);
        Vector3 boardPosition = new Vector3(-2f, 2.1f, 3.4f);
        Vector3 lookDirection = boardPosition - new Vector3(0f, 1.6f, 0f);
        lookDirection.y = 0f;
        var boardAnchor = new GameObject("Scoreboard Anchor").transform;
        boardAnchor.SetPositionAndRotation(boardPosition, Quaternion.LookRotation(lookDirection));
        scoreboard.transform.SetParent(boardAnchor, false);

        // Target spawner
        var spawnerGo = new GameObject("Target Spawner");
        spawnerGo.transform.position = new Vector3(0f, 0f, 0.8f);
        var spawner = spawnerGo.AddComponent<TargetSpawner>();
        SetField(spawner, "targetPrefab", targetPrefab);
        SetField(spawner, "movingTargetPrefab", movingTargetPrefab);
        SetField(spawner, "player", head);

        // Game manager
        var managerGo = new GameObject("Game Manager");
        var manager = managerGo.AddComponent<GameManager>();
        var managerAudio = managerGo.AddComponent<AudioSource>();
        managerAudio.playOnAwake = false;
        SetField(manager, "spawner", spawner);
        SetField(manager, "startTarget", startTarget);
        SetField(manager, "scoreboard", scoreboard);
        SetField(manager, "hitPopupPrefab", hitPopupPrefab);
        SetField(manager, "player", head);
        SetField(manager, "audioSource", managerAudio);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[ReflexRange] Step 3 done: " + ScenePath);
    }

    // ---------------------------------------------------------------- 4. APK

    [MenuItem("Reflex Range/4. Build APK for Quest")]
    public static void BuildApk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"[ReflexRange] Build {summary.result}: {ApkPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors)");

        if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    // ---------------------------------------------------------------- Scoreboard (Neon Arcade)

    // Fonts, glow materials and rounded sprites used by the UI
    class Style
    {
        public TMP_FontAsset display;   // Orbitron: titles and numbers
        public TMP_FontAsset body;      // Exo 2: labels
        public Material displayGlow;    // Orbitron with a cyan glow
        public Material gradeGlow;      // Orbitron with a magenta glow
        public Sprite roundedFill, roundedOutline, ring, ringThin, circle;
    }

    static Style CreateStyle()
    {
        var style = new Style
        {
            display = CreateFontAsset("Orbitron-ExtraBold"),
            body = CreateFontAsset("Exo2-Bold"),
            roundedFill = CreateSprite("Rounded Fill", 128, 48, (x, y) => Fill(RoundedSquareDistance(x, y, 128, 48f))),
            roundedOutline = CreateSprite("Rounded Outline", 128, 48, (x, y) => Stroke(RoundedSquareDistance(x, y, 128, 48f), 7f)),
            ring = CreateSprite("Ring", 256, 0, (x, y) => Stroke(CircleDistance(x, y, 256, 126f), 22f)),
            ringThin = CreateSprite("Ring Thin", 256, 0, (x, y) => Stroke(CircleDistance(x, y, 256, 126f), 12f)),
            circle = CreateSprite("Circle", 64, 0, (x, y) => Fill(CircleDistance(x, y, 64, 31f))),
        };
        style.displayGlow = CreateGlowMaterial(style.display, "Orbitron Glow Cyan", new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f));
        style.gradeGlow = CreateGlowMaterial(style.display, "Orbitron Glow Magenta", new Color(Magenta.r, Magenta.g, Magenta.b, 0.75f));
        return style;
    }

    // The scoreboard is laid out in pixels like the design (900 x 650), x/y measured from the top-left corner
    static ScoreboardUI BuildScoreboard(Style s)
    {
        var canvasGo = new GameObject("Scoreboard", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1;   // draw after the transparent text behind it, so nothing shows through
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900f, 650f);
        rect.localScale = Vector3.one * 0.0025f;   // 900 x 650 pixels -> 2.25 x 1.6 metres
        Transform root = canvasGo.transform;
        var ui = canvasGo.AddComponent<ScoreboardUI>();

        AddImage("Background", root, 0, 0, 900, 650, s.roundedFill, Ink, 1.7f);
        AddImage("Border", root, 0, 0, 900, 650, s.roundedOutline, Cyan, 1.7f);

        // Live panel: shown before and during a round
        Transform live = Place("Live", root, 0, 0, 900, 650);
        AddText("Title", live, 44, 30, 520, 56, s.display, 30, Cyan, "REFLEX RANGE", spacing: 15, material: s.displayGlow);

        Transform streak = Place("Streak Badge", live, 596, 30, 260, 56);
        AddImage("Outline", streak, 0, 0, 260, 56, s.roundedOutline, Magenta, 1.75f);
        TextMeshProUGUI streakText = AddText("Text", streak, 0, 0, 260, 56, s.display, 24, Magenta, "STREAK ×2", TextAlignmentOptions.Center, 8);

        AddImage("Timer Track", live, 44, 130, 260, 260, s.ring, RingTrack);
        Image timerRing = AddImage("Timer Ring", live, 44, 130, 260, 260, s.ring, Cyan);
        timerRing.type = Image.Type.Filled;
        timerRing.fillMethod = Image.FillMethod.Radial360;
        timerRing.fillOrigin = (int)Image.Origin360.Top;
        timerRing.fillClockwise = true;
        TextMeshProUGUI seconds = AddText("Seconds", live, 44, 186, 260, 100, s.display, 88, Cyan, "60", TextAlignmentOptions.Center);
        AddText("Seconds Label", live, 44, 290, 260, 30, s.body, 22, Muted, "SECONDS", TextAlignmentOptions.Center, 12);

        AddText("Score Label", live, 348, 132, 500, 34, s.body, 24, Muted, "SCORE", spacing: 20);
        TextMeshProUGUI score = AddText("Score", live, 344, 164, 520, 140, s.display, 124, White, "0", material: s.displayGlow);
        TextMeshProUGUI accuracy = AddChip(live, s, 348, 316, 210, "ACCURACY", "0%", 40);
        TextMeshProUGUI best = AddChip(live, s, 572, 316, 210, "BEST", "0", 40);

        AddImage("Message Bar", live, 44, 486, 812, 116, s.roundedFill, Chip, 2.67f);
        TextMeshProUGUI message = AddText("Message", live, 64, 486, 772, 116, s.display, 40, Yellow, "", TextAlignmentOptions.Center, 6);
        message.textWrappingMode = TextWrappingModes.Normal;
        message.enableAutoSizing = true;
        message.fontSizeMin = 20;
        message.fontSizeMax = 52;

        // Results panel: shown when the round is over
        Transform results = Place("Results", root, 0, 0, 900, 650);
        AddText("Title", results, 44, 30, 470, 56, s.display, 34, Cyan, "ROUND CLEAR", spacing: 15, material: s.displayGlow);

        Transform newBest = Place("New Best Badge", results, 516, 30, 340, 56);
        AddImage("Fill", newBest, 0, 0, 340, 56, s.roundedFill, Yellow, 1.75f);
        AddText("Text", newBest, 0, 0, 340, 56, s.display, 22, Ink, "NEW HIGH SCORE", TextAlignmentOptions.Center, 6);

        AddImage("Grade Ring", results, 44, 120, 220, 220, s.ringThin, Magenta);
        TextMeshProUGUI grade = AddText("Grade", results, 44, 150, 220, 120, s.display, 112, Magenta, "A", TextAlignmentOptions.Center, material: s.gradeGlow);
        AddText("Grade Label", results, 44, 272, 220, 30, s.body, 20, Muted, "GRADE", TextAlignmentOptions.Center, 20);

        AddText("Final Label", results, 308, 150, 540, 34, s.body, 24, Muted, "FINAL SCORE", spacing: 20);
        TextMeshProUGUI finalScore = AddText("Final Score", results, 304, 182, 560, 140, s.display, 120, White, "0", material: s.displayGlow);

        const float gap = 14f;
        float chipWidth = (812f - 3f * gap) / 4f;
        TextMeshProUGUI hits = AddChip(results, s, 44, 380, chipWidth, "HITS", "0", 34);
        TextMeshProUGUI shots = AddChip(results, s, 44 + (chipWidth + gap), 380, chipWidth, "SHOTS", "0", 34);
        TextMeshProUGUI finalAccuracy = AddChip(results, s, 44 + 2f * (chipWidth + gap), 380, chipWidth, "ACCURACY", "0%", 34);
        TextMeshProUGUI reaction = AddChip(results, s, 44 + 3f * (chipWidth + gap), 380, chipWidth, "REACTION", "-", 34);

        AddImage("Replay Dot", results, 200, 541, 22, 22, s.circle, Green);
        AddText("Replay Hint", results, 236, 524, 620, 56, s.body, 28, White, "Shoot the green target to play again", spacing: 2);
        results.gameObject.SetActive(false);

        // Connect everything to the ScoreboardUI script
        SetField(ui, "livePanel", live.gameObject);
        SetField(ui, "scoreText", score);
        SetField(ui, "secondsText", seconds);
        SetField(ui, "timerRing", timerRing);
        SetField(ui, "accuracyText", accuracy);
        SetField(ui, "bestText", best);
        SetField(ui, "streakBadge", streak.gameObject);
        SetField(ui, "streakText", streakText);
        SetField(ui, "messageText", message);
        SetField(ui, "resultsPanel", results.gameObject);
        SetField(ui, "gradeText", grade);
        SetField(ui, "finalScoreText", finalScore);
        SetField(ui, "hitsText", hits);
        SetField(ui, "shotsText", shots);
        SetField(ui, "finalAccuracyText", finalAccuracy);
        SetField(ui, "reactionText", reaction);
        SetField(ui, "newBestBadge", newBest.gameObject);
        return ui;
    }

    // Small rounded box with a label on top and a big value below; returns the value text
    static TextMeshProUGUI AddChip(Transform parent, Style s, float x, float y, float width, string label, string value, float valueSize)
    {
        Transform chip = Place(label + " Chip", parent, x, y, width, 96);
        AddImage("Fill", chip, 0, 0, width, 96, s.roundedFill, Chip, 3f);
        AddText("Label", chip, 18, 10, width - 36, 28, s.body, 20, Muted, label, spacing: 12);
        return AddText("Value", chip, 18, 38, width - 36, 52, s.display, valueSize, White, value);
    }

    static RectTransform Place(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    // cornerScale: bigger = smaller rounded corners (the sprite's corners are 48 px)
    static Image AddImage(string name, Transform parent, float x, float y, float width, float height, Sprite sprite, Color color, float cornerScale = 1f)
    {
        var image = Place(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        if (sprite.border != Vector4.zero)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerScale;
        }
        return image;
    }

    static TextMeshProUGUI AddText(string name, Transform parent, float x, float y, float width, float height, TMP_FontAsset font, float size,
        Color color, string text, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, float spacing = 0f, Material material = null)
    {
        var tmp = Place(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        if (material != null)
            tmp.fontSharedMaterial = material;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.text = text;
        tmp.alignment = alignment;
        tmp.characterSpacing = spacing;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    // 3D text in the world (not on a canvas), e.g. the sign above the START target
    static TextMeshPro AddWorldText(string name, Transform parent, Vector3 localPosition, TMP_FontAsset font, Material material, float size, Color color, string text)
    {
        var tmp = new GameObject(name).AddComponent<TextMeshPro>();
        tmp.transform.SetParent(parent, false);
        tmp.transform.localPosition = localPosition;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 1f);
        tmp.font = font;
        if (material != null)
            tmp.fontSharedMaterial = material;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    static HitPopup BuildHitPopupPrefab(Style s)
    {
        TextMeshPro text = AddWorldText("Hit Popup", null, Vector3.zero, s.display, s.displayGlow, 6f, White, "+10");
        text.rectTransform.sizeDelta = new Vector2(4f, 2f);
        text.gameObject.AddComponent<HitPopup>();
        return SavePrefab(text.gameObject).GetComponent<HitPopup>();
    }

    // ---------------------------------------------------------------- Generated assets

    // Import settings for the pistol model: no embedded materials or animation, textures marked correctly
    static GameObject PreparePistolModel()
    {
        var model = (ModelImporter)AssetImporter.GetAtPath(PistolModelPath);
        if (model.materialImportMode != ModelImporterMaterialImportMode.None || model.importAnimation)
        {
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.importCameras = false;
            model.importLights = false;
            model.SaveAndReimport();
        }

        SetTextureImport("GLOCK_glock_AlbedoTransparency.png", TextureImporterType.Default, sRGB: true);
        SetTextureImport("GLOCK_glock_MetallicSmoothness.png", TextureImporterType.Default, sRGB: false);
        SetTextureImport("GLOCK_glock_Normal.png", TextureImporterType.NormalMap, sRGB: false);
        return AssetDatabase.LoadAssetAtPath<GameObject>(PistolModelPath);
    }

    static void SetTextureImport(string file, TextureImporterType type, bool sRGB)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath($"{PistolFolder}/{file}");
        importer.textureType = type;
        importer.sRGBTexture = sRGB;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    // URP Lit material using the model's colour, metallic/smoothness and normal textures
    static Material CreatePistolMaterial()
    {
        Material material = CreateMaterial("Pistol", Color.white, smoothness: 1f);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{PistolFolder}/GLOCK_glock_AlbedoTransparency.png"));
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{PistolFolder}/GLOCK_glock_MetallicSmoothness.png"));
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness is in the metallic map's alpha
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{PistolFolder}/GLOCK_glock_Normal.png"));
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    static TMP_FontAsset CreateFontAsset(string fileName)
    {
        string assetPath = $"{Root}/Fonts/{fileName} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null)
            return existing;

        var font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/{fileName}.ttf");
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
        asset.name = fileName + " SDF";
        AssetDatabase.CreateAsset(asset, assetPath);
        asset.material.name = asset.name + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        asset.atlasTextures[0].name = asset.name + " Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);

        // Put every character we use into the atlas now, so nothing is generated on the headset
        asset.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~×·");
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    // A copy of the font material with a soft coloured "underlay" behind the letters = neon glow
    static Material CreateGlowMaterial(TMP_FontAsset font, string name, Color glow)
    {
        string path = $"{Root}/Fonts/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(font.material);
            AssetDatabase.CreateAsset(material, path);
        }
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor", glow);
        material.SetFloat("_UnderlayOffsetX", 0f);
        material.SetFloat("_UnderlayOffsetY", 0f);
        material.SetFloat("_UnderlayDilate", 0.6f);
        material.SetFloat("_UnderlaySoftness", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Draws a white shape into a PNG and imports it as a UI sprite. border > 0 = 9-slice corners.
    static Sprite CreateSprite(string name, int size, int border, System.Func<float, float, float> alpha)
    {
        string path = $"{Root}/UI/{name}.png";
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x + 0.5f, y + 0.5f) * 255f));
        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Signed distance to the edge of a rounded square (negative = inside)
    static float RoundedSquareDistance(float x, float y, int size, float radius)
    {
        float half = size / 2f;
        float qx = Mathf.Abs(x - half) - (half - radius);
        float qy = Mathf.Abs(y - half) - (half - radius);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    static float CircleDistance(float x, float y, int size, float radius)
    {
        return new Vector2(x - size / 2f, y - size / 2f).magnitude - radius;
    }

    static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);
    static float Stroke(float distance, float width) => Mathf.Clamp01(0.5f - distance) * Mathf.Clamp01(distance + width + 0.5f);

    static Material CreateNightSky()
    {
        string path = $"{Root}/Materials/Night Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, path);
        }
        sky.SetFloat("_SunDisk", 0f);
        sky.EnableKeyword("_SUNDISK_NONE");
        sky.DisableKeyword("_SUNDISK_SIMPLE");
        sky.DisableKeyword("_SUNDISK_HIGH_QUALITY");
        sky.SetColor("_SkyTint", new Color(0.3f, 0.2f, 0.5f));
        sky.SetColor("_GroundColor", new Color(0.03f, 0.03f, 0.06f));
        sky.SetFloat("_AtmosphereThickness", 0.4f);
        sky.SetFloat("_Exposure", 0.45f);
        EditorUtility.SetDirty(sky);
        return sky;
    }

    // ---------------------------------------------------------------- Helpers

    // Turns off walking, teleporting and climbing. Only snap turn stays (right thumbstick).
    static void MakeRigStationary(GameObject rig)
    {
        foreach (Transform child in rig.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Move" || child.name == "Teleportation" || child.name == "Climb" || child.name == "Grab Move")
                child.gameObject.SetActive(false);
        }

        foreach (MonoBehaviour behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour.GetType().Name != "ControllerInputActionManager")
                continue;
            var so = new SerializedObject(behaviour);
            so.FindProperty("m_TeleportMode").objectReferenceValue = null;
            so.FindProperty("m_TeleportModeCancel").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static GameObject BuildPistol(Material laserMat, Material flashMat)
    {
        // The root is the grip: this is where the controller holds the pistol
        var pistol = new GameObject("Pistol");

        // The Glock model, moved so its grip sits on the root
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(PreparePistolModel(), pistol.transform);
        model.name = "Glock Model";
        model.transform.localPosition = -GripPoint;

        var renderer = model.GetComponentInChildren<MeshRenderer>();
        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Material glockMat = CreatePistolMaterial();
        Material sightGlowMat = CreateMaterial("Pistol Sight Glow", new Color(0.6f, 1f, 0.6f), unlit: true);
        // The model has two parts: the gun body and the small glowing sights (the part with fewer triangles)
        if (mesh.subMeshCount > 1)
        {
            bool firstIsSights = mesh.GetSubMesh(0).indexCount < mesh.GetSubMesh(1).indexCount;
            renderer.sharedMaterials = firstIsSights ? new[] { sightGlowMat, glockMat } : new[] { glockMat, sightGlowMat };
        }
        else
        {
            renderer.sharedMaterial = glockMat;
        }

        // Convex collider shaped like the gun, used for grabbing and for landing on the table
        var collider = renderer.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(pistol.transform, false);
        muzzle.localPosition = MuzzlePoint - GripPoint;

        GameObject flash = CreateBlock("Muzzle Flash", PrimitiveType.Sphere, muzzle, new Vector3(0f, 0f, 0.02f), Vector3.one * 0.045f, flashMat, keepCollider: false);
        flash.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var laser = new GameObject("Laser").AddComponent<LineRenderer>();
        laser.transform.SetParent(pistol.transform, false);
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.startWidth = 0.008f;
        laser.endWidth = 0.004f;
        laser.sharedMaterial = laserMat;
        laser.shadowCastingMode = ShadowCastingMode.Off;

        var audio = pistol.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        var body = pistol.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = pistol.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.farAttachMode = InteractableFarAttachMode.Near;   // a far grab pulls the pistol into the hand
        grab.throwOnDetach = true;

        var script = pistol.AddComponent<Pistol>();
        SetField(script, "muzzle", muzzle);
        SetField(script, "laser", laser);
        SetField(script, "muzzleFlash", flash);
        SetField(script, "audioSource", audio);
        return pistol;
    }

    // Three flat cylinders = outer ring, middle ring and bullseye. The face points along +Z.
    static GameObject BuildTargetVisual(string name, float radius, Material outer, Material middle, Material inner)
    {
        var root = new GameObject(name);
        AddRing(root.transform, "Outer Ring", radius, 0.02f, outer, addCollider: true);
        AddRing(root.transform, "Middle Ring", radius * 2f / 3f, 0.022f, middle, addCollider: false);
        AddRing(root.transform, "Bullseye", radius / 3f, 0.024f, inner, addCollider: false);
        return root;
    }

    static void AddRing(Transform parent, string name, float radius, float halfThickness, Material material, bool addCollider)
    {
        GameObject ring = CreateBlock(name, PrimitiveType.Cylinder, parent, Vector3.zero,
            new Vector3(radius * 2f, halfThickness, radius * 2f), material, keepCollider: false);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        if (addCollider)
        {
            var collider = ring.AddComponent<MeshCollider>();
            collider.sharedMesh = ring.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = true;
        }
    }

    static GameObject CreateBlock(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider = true)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider)
            Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static Material CreateMaterial(string name, Color color, bool unlit = false, float smoothness = 0.2f)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", color);
        if (!unlit)
            material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject SavePrefab(GameObject go)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{go.name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject InstantiatePrefab(string prefabName, string folderHint)
    {
        string path = AssetDatabase.FindAssets(prefabName + " t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => p.Contains(folderHint) && Path.GetFileNameWithoutExtension(p) == prefabName);
        if (path == null)
            throw new FileNotFoundException($"Prefab '{prefabName}' not found. Run 'Reflex Range/1. Import XR Samples' first.");

        return (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    // Sets a private [SerializeField] field, the same way dragging a reference in the Inspector does
    static void SetField(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetField(Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
