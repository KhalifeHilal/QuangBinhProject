using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes one self-contained JSON record per scenario to
/// Application.persistentDataPath/FloodStudyLogs. The logger is deliberately
/// independent of the UI so the same data is collected in the normal and
/// editor-debug launch paths.
/// </summary>
public static class FloodStudyLogger
{
    const float FloodedThresholdMeters = 0.01f;
    const int ScreenshotWidth = 1024;
    const int ScreenshotHeight = 1024;
    static FloodScenarioRecord current;
    static int nextDykeId;
    static float observationRealtime = -1f;
    static float buildingRealtime = -1f;
    static float simulationRealtime = -1f;
    static Transform terrainTransform;
    // General-discomfort surveys are shown between scenarios, when there is
    // intentionally no active scenario record. Keep them at session scope so
    // both answers are still written immediately and can be linked later.
    static int discomfortBeforeA1P4 = -1;
    static int discomfortAfterA1P6 = -1;

    public static bool IsScenarioActive => current != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewRun()
    {
        current = null;
        nextDykeId = 0;
        observationRealtime = -1f;
        buildingRealtime = -1f;
        simulationRealtime = -1f;
        terrainTransform = null;
        discomfortBeforeA1P4 = -1;
        discomfortAfterA1P6 = -1;
    }

    public static void BeginScenario(string scenarioId, string topic, float plannedObservationDuration)
    {
        if (current != null) EndScenario();

        int participantNumber = ParticipantSession.IsCheckedIn ? ParticipantSession.Number : 0;
        current = new FloodScenarioRecord
        {
            participant_id = participantNumber,
            participant_id_mod12 = participantNumber > 0 ? ((participantNumber - 1) % 12) + 1 : 0,
            input_method = ParticipantSession.IsCheckedIn
                ? ParticipantSession.InteractionMode.ToString()
                : "Unknown",
            session = ParticipantSession.IsCheckedIn ? ParticipantSession.QuestionSet.ToString() : "Unknown",
            scenario = scenarioId,
            topic = topic,
            map_version = SceneManager.GetActiveScene().name,
            application_version = Application.version,
            scenario_started_at_utc = UtcNow(),
            planned_observation_duration_s = Mathf.Max(0f, plannedObservationDuration),
            flood_threshold_m = FloodedThresholdMeters,
            general_discomfort_before_A1_P4 = discomfortBeforeA1P4 >= 0 ? discomfortBeforeA1P4 : 0,
            general_discomfort_before_A1_P4_recorded = discomfortBeforeA1P4 >= 0,
            general_discomfort_after_A1_P6 = discomfortAfterA1P6 >= 0 ? discomfortAfterA1P6 : 0,
            general_discomfort_after_A1_P6_recorded = discomfortAfterA1P6 >= 0,
            dykes = new List<FloodDykeRecord>(),
            building_outcomes = new List<FloodBuildingOutcome>()
        };
        nextDykeId = 0;
        observationRealtime = -1f;
        buildingRealtime = -1f;
        simulationRealtime = -1f;
    }

    public static void BeginObservation()
    {
        if (current == null) return;
        if (!string.IsNullOrEmpty(current.observation_started_at_utc)) current.repeat_count++;
        current.observation_started_at_utc = UtcNow();
        observationRealtime = Time.realtimeSinceStartup;
    }

    public static void EndObservation()
    {
        if (current == null || observationRealtime < 0f) return;
        float duration = Mathf.Max(0f, Time.realtimeSinceStartup - observationRealtime);
        current.observation_duration_s += duration;
        current.observation_ended_at_utc = UtcNow();
        observationRealtime = -1f;
    }

    public static void BeginBuildingPhase()
    {
        if (current == null) return;
        current.building_phase_started_at_utc = UtcNow();
        buildingRealtime = Time.realtimeSinceStartup;
    }

    public static void EndBuildingPhase()
    {
        if (current == null || buildingRealtime < 0f) return;
        current.duration_of_building_phase_s = Mathf.Max(0f, Time.realtimeSinceStartup - buildingRealtime);
        current.building_phase_ended_at_utc = UtcNow();
        buildingRealtime = -1f;
        current.build_end_screenshot_path = CaptureTopDownScreenshot(current.scenario, "build_end");
    }

    public static void RecordAnswer(int answerIndex)
    {
        if (current == null) return;
        current.answer = answerIndex >= 0 ? ((char)('A' + answerIndex)).ToString() : "";
        int correctIndex = CorrectAnswerIndex(current.scenario);
        current.correct_answer = correctIndex >= 0 ? ((char)('A' + correctIndex)).ToString() : "";
        current.answer_was_correct = correctIndex >= 0 && answerIndex == correctIndex;
    }

    /// <summary>
    /// Records a 0-20 General Discomfort response. These surveys are shown
    /// between scenarios, so the value is written to its own JSON file
    /// immediately instead of relying on the active scenario lifecycle.
    /// </summary>
    public static void RecordGeneralDiscomfort(string measurementId, int score)
    {
        score = Mathf.Clamp(score, 0, 20);
        string measurement = string.IsNullOrEmpty(measurementId) ? "unspecified" : measurementId;
        if (string.Equals(measurement, "before_A1_P4", StringComparison.OrdinalIgnoreCase))
            discomfortBeforeA1P4 = score;
        else if (string.Equals(measurement, "after_A1_P6", StringComparison.OrdinalIgnoreCase))
            discomfortAfterA1P6 = score;

        FloodGeneralDiscomfortRecord record = new FloodGeneralDiscomfortRecord
        {
            participant_id = ParticipantSession.IsCheckedIn ? ParticipantSession.Number : 0,
            participant_id_mod12 = ParticipantSession.IsCheckedIn && ParticipantSession.Number > 0
                ? ((ParticipantSession.Number - 1) % 12) + 1 : 0,
            input_method = ParticipantSession.IsCheckedIn
                ? ParticipantSession.InteractionMode.ToString() : "Unknown",
            session = ParticipantSession.IsCheckedIn
                ? ParticipantSession.QuestionSet.ToString() : "Unknown",
            measurement = measurement,
            score_0_to_20 = score,
            timestamp_utc = UtcNow()
        };

        string folder = ParticipantLogFolder(record.participant_id);
        Directory.CreateDirectory(folder);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string path = Path.Combine(folder,
            "GeneralDiscomfort_" + SafeFilePart(measurement) + "_" + timestamp + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(record, true));
        Debug.Log("Flood study general-discomfort log written to: " + path);

        // If a survey is shown while a flow still owns the current record,
        // keep the values in that record as well.
        if (current != null)
        {
            current.general_discomfort_before_A1_P4 = discomfortBeforeA1P4 >= 0 ? discomfortBeforeA1P4 : 0;
            current.general_discomfort_before_A1_P4_recorded = discomfortBeforeA1P4 >= 0;
            current.general_discomfort_after_A1_P6 = discomfortAfterA1P6 >= 0 ? discomfortAfterA1P6 : 0;
            current.general_discomfort_after_A1_P6_recorded = discomfortAfterA1P6 >= 0;
        }
    }

    public static void BeginSimulation()
    {
        if (current == null) return;
        current.simulation_started_at_utc = UtcNow();
        simulationRealtime = Time.realtimeSinceStartup;
        current.used_barrier_length_m = ActiveDykeLength();
        current.active_dyke_count_at_simulation_start = ActiveDykeCount();
    }

    public static void EndSimulation(TerrainFloodSimulation simulation)
    {
        if (current == null) return;
        current.simulation_ended_at_utc = UtcNow();
        if (simulationRealtime >= 0f)
            current.simulation_duration_s = Mathf.Max(0f, Time.realtimeSinceStartup - simulationRealtime);
        simulationRealtime = -1f;

        current.used_barrier_length_m = current.used_barrier_length_m > 0f
            ? current.used_barrier_length_m : ActiveDykeLength();
        current.total_built_length_m = TotalBuiltLength();
        current.total_destroyed_length_m = TotalDestroyedLength();
        // Capture before the scenario flow stops and destroys the flood
        // object, preserving the final water state and participant dykes.
        current.simulation_end_screenshot_path = CaptureTopDownScreenshot(current.scenario, "simulation_end");

        if (simulation != null && simulation.TryCaptureWaterDepthMap(out float[] waterMap))
        {
            current.building_outcomes_available = true;
            CaptureBuildingOutcomes(simulation, waterMap);
        }
        else
        {
            current.building_outcomes_available = false;
        }

        current.protected_buildings_count = 0;
        current.flooded_buildings_count = 0;
        current.protected_priority_score_total = 0;
        for (int i = 0; i < current.building_outcomes.Count; i++)
        {
            if (current.building_outcomes[i].flooded) current.flooded_buildings_count++;
            else if (current.building_outcomes[i].protected_building)
            {
                current.protected_buildings_count++;
                current.protected_priority_score_total += current.building_outcomes[i].protected_priority_score;
            }
        }
        current.protection_efficiency_per_m = current.used_barrier_length_m > 0.0001f
            ? current.protected_buildings_count / current.used_barrier_length_m : 0f;
        current.priority_weighted_protection_efficiency_per_m = current.used_barrier_length_m > 0.0001f
            ? current.protected_priority_score_total / current.used_barrier_length_m : 0f;
        current.protection_efficiency_valid = current.building_outcomes_available && current.used_barrier_length_m > 0.0001f;
    }

    public static int RecordDykeBuilt(Vector3 startWorld, Vector3 endWorld, float lengthMeters)
    {
        if (current == null) return 0;
        int id = ++nextDykeId;
        Vector3 start = ToMapLocal(startWorld);
        Vector3 end = ToMapLocal(endWorld);
        FloodDykeRecord record = new FloodDykeRecord
        {
            dyke_id = id,
            built_at_utc = UtcNow(),
            start_position_local = start,
            end_position_local = end,
            position_dyke_local = (start + end) * .5f,
            position_dyke = (start + end) * .5f,
            length_dyke_m = Mathf.Max(0f, lengthMeters),
            length_dyke = Mathf.Max(0f, lengthMeters),
            active = true
        };
        current.dykes.Add(record);
        current.dykes_built_count++;
        return id;
    }

    public static void RecordDykeDestroyed(int dykeId)
    {
        if (current == null || dykeId <= 0) return;
        for (int i = 0; i < current.dykes.Count; i++)
        {
            FloodDykeRecord record = current.dykes[i];
            if (record.dyke_id != dykeId || !record.active) continue;
            record.active = false;
            record.destroyed_at_utc = UtcNow();
            current.dykes_destroyed_count++;
            current.total_destroyed_length_m += record.length_dyke_m;
            return;
        }
    }

    public static void EndScenario()
    {
        if (current == null) return;
        EndObservation();
        EndBuildingPhase();
        current.total_built_length_m = TotalBuiltLength();
        current.total_destroyed_length_m = TotalDestroyedLength();
        current.active_dyke_count_at_end = ActiveDykeCount();
        current.general_discomfort_before_A1_P4 = discomfortBeforeA1P4 >= 0 ? discomfortBeforeA1P4 : 0;
        current.general_discomfort_before_A1_P4_recorded = discomfortBeforeA1P4 >= 0;
        current.general_discomfort_after_A1_P6 = discomfortAfterA1P6 >= 0 ? discomfortAfterA1P6 : 0;
        current.general_discomfort_after_A1_P6_recorded = discomfortAfterA1P6 >= 0;
        current.scenario_ended_at_utc = UtcNow();
        current.time_stamp_beggining = current.scenario_started_at_utc;
        current.time_stamp_end = current.scenario_ended_at_utc;

        string folder = ParticipantLogFolder(current.participant_id);
        Directory.CreateDirectory(folder);
        string safeScenario = SafeFilePart(current.scenario);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string path = Path.Combine(folder, safeScenario + "_" + timestamp + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(current, true));
        Debug.Log("Flood study scenario log written to: " + path);
        current = null;
        observationRealtime = -1f;
        buildingRealtime = -1f;
        simulationRealtime = -1f;
    }

    /// <summary>
    /// Saves a square top-down PNG of the map. The terrain bounds frame the
    /// shot; world objects inside the map, including flood and dykes, remain
    /// visible. UI canvases are hidden only during the render so a panel
    /// cannot cover the study image.
    /// </summary>
    public static string CaptureTopDownScreenshot(string scenarioId, string stageId)
    {
        return CaptureTopDownScreenshot(scenarioId, stageId, null);
    }

    public static string CaptureTopDownScreenshot(string scenarioId, string stageId, string[] solutionObjectNames)
    {
        if (string.IsNullOrEmpty(scenarioId)) scenarioId = "UnknownScenario";
        if (string.IsNullOrEmpty(stageId)) stageId = "snapshot";

        int participantNumber = ParticipantSession.IsCheckedIn ? ParticipantSession.Number : 0;
        string folder = ParticipantLogFolder(participantNumber);
        Directory.CreateDirectory(folder);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string safeScenario = SafeFilePart(scenarioId);
        string safeStage = SafeFilePart(stageId);
        string path = Path.Combine(folder, safeScenario + "_" + safeStage + "_" + timestamp + ".png");

        if (!TryWriteTopDownPng(path))
        {
            Debug.LogWarning("Could not capture flood-study screenshot for " + scenarioId + " (" + stageId + ").");
            return string.Empty;
        }

        FloodScreenshotRecord manifest = new FloodScreenshotRecord
        {
            participant_id = participantNumber,
            participant_id_mod12 = participantNumber > 0 ? ((participantNumber - 1) % 12) + 1 : 0,
            input_method = ParticipantSession.IsCheckedIn ? ParticipantSession.InteractionMode.ToString() : "Unknown",
            session = ParticipantSession.IsCheckedIn ? ParticipantSession.QuestionSet.ToString() : "Unknown",
            scenario = scenarioId,
            stage = stageId,
            image_path = path,
            timestamp_utc = UtcNow(),
            participant_dykes = current != null &&
                string.Equals(current.scenario, scenarioId, StringComparison.OrdinalIgnoreCase)
                ? CloneDykeRecords(current.dykes) : new List<FloodDykeRecord>(),
            solution_dykes = CaptureSolutionGeometry(solutionObjectNames)
        };
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(manifest, true));

        if (current != null && string.Equals(current.scenario, scenarioId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(stageId, "build_end", StringComparison.OrdinalIgnoreCase))
                current.build_end_screenshot_path = path;
            else if (string.Equals(stageId, "simulation_end", StringComparison.OrdinalIgnoreCase))
                current.simulation_end_screenshot_path = path;
            else if (string.Equals(stageId, "feedback_end", StringComparison.OrdinalIgnoreCase))
                current.feedback_end_screenshot_path = path;
        }

        Debug.Log("Flood study screenshot written to: " + path);
        return path;
    }

    static List<FloodSolutionGeometry> CaptureSolutionGeometry(string[] solutionObjectNames)
    {
        List<FloodSolutionGeometry> result = new List<FloodSolutionGeometry>();
        if (solutionObjectNames == null || solutionObjectNames.Length == 0) return result;

        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            bool matches = false;
            for (int i = 0; i < solutionObjectNames.Length; i++)
            {
                if (string.Equals(child.name, solutionObjectNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                    break;
                }
            }
            if (!matches || !child.gameObject.activeInHierarchy) continue;

            if (TryGetBounds(child, out Bounds bounds))
            {
                result.Add(new FloodSolutionGeometry
                {
                    object_name = child.name,
                    position_local = ToMapLocal(child.position),
                    bounds_center_local = ToMapLocal(bounds.center),
                    bounds_size_world = bounds.size
                });
            }
        }
        return result;
    }

    static List<FloodDykeRecord> CloneDykeRecords(List<FloodDykeRecord> source)
    {
        List<FloodDykeRecord> result = new List<FloodDykeRecord>();
        if (source == null) return result;
        for (int i = 0; i < source.Count; i++)
        {
            FloodDykeRecord item = source[i];
            if (item == null) continue;
            result.Add(new FloodDykeRecord
            {
                dyke_id = item.dyke_id,
                built_at_utc = item.built_at_utc,
                destroyed_at_utc = item.destroyed_at_utc,
                start_position_local = item.start_position_local,
                end_position_local = item.end_position_local,
                position_dyke_local = item.position_dyke_local,
                position_dyke = item.position_dyke,
                length_dyke_m = item.length_dyke_m,
                length_dyke = item.length_dyke,
                active = item.active
            });
        }
        return result;
    }

    static bool TryWriteTopDownPng(string path)
    {
        if (!TryGetCaptureBounds(out Bounds bounds)) return false;

        GameObject cameraObject = null;
        Camera captureCamera = null;
        RenderTexture target = null;
        Texture2D image = null;
        RenderTexture previousActive = RenderTexture.active;
        List<Canvas> disabledCanvases = new List<Canvas>();

        try
        {
            float marginX = Mathf.Max(1f, bounds.size.x * .06f);
            float marginZ = Mathf.Max(1f, bounds.size.z * .06f);
            bounds.Expand(new Vector3(marginX * 2f, 0f, marginZ * 2f));

            cameraObject = new GameObject("__FloodStudyTopDownCapture");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.aspect = (float)ScreenshotWidth / ScreenshotHeight;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(.07f, .09f, .11f, 1f);
            captureCamera.cullingMask = ~0;
            captureCamera.nearClipPlane = .1f;
            captureCamera.farClipPlane = Mathf.Max(1000f, bounds.size.magnitude * 4f);
            captureCamera.allowHDR = false;
            captureCamera.allowMSAA = false;
            captureCamera.stereoTargetEye = StereoTargetEyeMask.None;

            float aspect = captureCamera.aspect;
            captureCamera.orthographicSize = Mathf.Max(bounds.extents.z, bounds.extents.x / aspect);
            float cameraHeight = bounds.max.y + Mathf.Max(20f, bounds.size.magnitude);
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, cameraHeight, bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));

            target = new RenderTexture(ScreenshotWidth, ScreenshotHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
            captureCamera.targetTexture = target;

            // World-space study panels are on the default layer, so hide
            // canvases temporarily rather than filtering out map geometry.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy) continue;
                canvas.enabled = false;
                disabledCanvases.Add(canvas);
            }

            captureCamera.Render();
            RenderTexture.active = target;
            image = new Texture2D(ScreenshotWidth, ScreenshotHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ScreenshotWidth, ScreenshotHeight), 0, 0, false);
            image.Apply(false, false);
            byte[] png = image.EncodeToPNG();
            if (png == null || png.Length == 0) return false;
            File.WriteAllBytes(path, png);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Flood-study top-down screenshot failed: " + exception.Message);
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            for (int i = 0; i < disabledCanvases.Count; i++)
                if (disabledCanvases[i] != null) disabledCanvases[i].enabled = true;
            if (image != null) UnityEngine.Object.Destroy(image);
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
        }
    }

    static bool TryGetCaptureBounds(out Bounds bounds)
    {
        Transform terrain = FindTerrainTransform();
        if (terrain != null && TryGetBounds(terrain, out bounds)) return true;

        bounds = default;
        bool found = false;
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    // Kept internal because ScenarioDykeLength is an implementation detail of
    // DykeManagerTutorial and is intentionally not part of the public API.
    internal static void RecordDykeDestroyed(ScenarioDykeLength dyke)
    {
        if (dyke != null) RecordDykeDestroyed(dyke.dykeId);
    }

    static float ActiveDykeLength()
    {
        if (current == null) return 0f;
        float total = 0f;
        for (int i = 0; i < current.dykes.Count; i++)
            if (current.dykes[i].active) total += current.dykes[i].length_dyke_m;
        return total;
    }

    static int ActiveDykeCount()
    {
        if (current == null) return 0;
        int count = 0;
        for (int i = 0; i < current.dykes.Count; i++)
            if (current.dykes[i].active) count++;
        return count;
    }

    static float TotalBuiltLength()
    {
        if (current == null) return 0f;
        float total = 0f;
        for (int i = 0; i < current.dykes.Count; i++) total += current.dykes[i].length_dyke_m;
        return total;
    }

    static float TotalDestroyedLength()
    {
        if (current == null) return 0f;
        float total = 0f;
        for (int i = 0; i < current.dykes.Count; i++)
            if (!current.dykes[i].active) total += current.dykes[i].length_dyke_m;
        return total;
    }

    static void CaptureBuildingOutcomes(TerrainFloodSimulation simulation, float[] waterMap)
    {
        HashSet<int> recorded = new HashSet<int>();
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!child.gameObject.activeInHierarchy || !IsLoggedBuilding(child.name)) continue;
            if (!recorded.Add(child.gameObject.GetInstanceID())) continue;

            Bounds bounds;
            if (!TryGetBounds(child, out bounds)) continue;
            if (!simulation.TryGetWaterDepthAtWorldPosition(waterMap, bounds.center, out float depth)) continue;

            string priority = PriorityFor(child.name);
            string district = DistrictFor(child.name);
            current.building_outcomes.Add(new FloodBuildingOutcome
            {
                building_id = child.name,
                priority = priority,
                district = district,
                water_depth_at_simulation_end_m = depth,
                final_water_depth_m = depth,
                flooded = depth >= FloodedThresholdMeters,
                protected_building = depth < FloodedThresholdMeters,
                protected_priority_score = PriorityScore(priority)
            });
        }
    }

    static bool TryGetBounds(Transform target, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (!found) { bounds = renderers[i].bounds; found = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return found;
    }

    static bool IsLoggedBuilding(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper.StartsWith("HOUSE_") || upper == "HOSPITAL" || upper == "HOSPITAL_B" ||
               upper == "LOW_DISTRICT" || upper == "POPULAR_DISTRICT";
    }

    static string PriorityFor(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper == "HOSPITAL" || upper == "HOSPITAL_B") return "very_high";
        if (upper == "LOW_DISTRICT" || upper == "HOUSE_R" || upper == "HOUSE_S") return "low";
        if (upper == "POPULAR_DISTRICT" || upper == "HOUSE_O" || upper == "HOUSE_P" || upper == "HOUSE_Q") return "high";
        return "normal";
    }

    static string DistrictFor(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper == "LOW_DISTRICT" || upper == "HOUSE_R" || upper == "HOUSE_S") return "low_district";
        if (upper == "POPULAR_DISTRICT" || upper == "HOUSE_O" || upper == "HOUSE_P" || upper == "HOUSE_Q") return "popular_district";
        return "";
    }

    static int PriorityScore(string priority)
    {
        switch (priority)
        {
            case "very_high": return 4;
            case "high": return 3;
            case "normal": return 2;
            default: return 1;
        }
    }

    static Vector3 ToMapLocal(Vector3 worldPosition)
    {
        Transform terrain = FindTerrainTransform();
        return terrain != null ? terrain.InverseTransformPoint(worldPosition) : worldPosition;
    }

    static Transform FindTerrainTransform()
    {
        if (terrainTransform != null) return terrainTransform;
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == "SM_Tutorial_Terrain_01") { terrainTransform = child; return terrainTransform; }
        return null;
    }

    static int CorrectAnswerIndex(string scenario)
    {
        switch (scenario)
        {
            case "A1-P1": return 1;
            case "A1-P2": return 3;
            case "A1-P3": return 0;
            case "A1-P4": return 2;
            case "A1-P5": return 1;
            case "A1-P6": return 2;
            default: return -1;
        }
    }

    static string UtcNow() => DateTime.UtcNow.ToString("o");
    static string ParticipantLogFolder(int participantNumber)
    {
        string participantFolder = participantNumber > 0
            ? $"Participant_{participantNumber:00}"
            : "Participant_Unknown";
        return Path.Combine(Application.persistentDataPath, "FloodStudyLogs", participantFolder);
    }

    static string SafeFilePart(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrEmpty(value) ? "scenario" : value;
    }
}

[Serializable]
public sealed class FloodScenarioRecord
{
    public int participant_id;
    public int participant_id_mod12;
    public string input_method;
    public string session;
    public string scenario;
    public string topic;
    public string map_version;
    public string application_version;
    public string scenario_started_at_utc;
    public string scenario_ended_at_utc;
    // Compatibility aliases matching the original study variable names.
    public string time_stamp_beggining;
    public string time_stamp_end;
    public string observation_started_at_utc;
    public string observation_ended_at_utc;
    public float planned_observation_duration_s;
    public float observation_duration_s;
    public int repeat_count;
    public string building_phase_started_at_utc;
    public string building_phase_ended_at_utc;
    public float duration_of_building_phase_s;
    public string simulation_started_at_utc;
    public string simulation_ended_at_utc;
    public float simulation_duration_s;
    public string answer;
    public string correct_answer;
    public bool answer_was_correct;
    public string build_end_screenshot_path;
    public string simulation_end_screenshot_path;
    public string feedback_end_screenshot_path;
    public int general_discomfort_before_A1_P4;
    public bool general_discomfort_before_A1_P4_recorded;
    public int general_discomfort_after_A1_P6;
    public bool general_discomfort_after_A1_P6_recorded;
    public int dykes_built_count;
    public int dykes_destroyed_count;
    public int active_dyke_count_at_simulation_start;
    public int active_dyke_count_at_end;
    public float used_barrier_length_m;
    public float total_built_length_m;
    public float total_destroyed_length_m;
    public int protected_buildings_count;
    public int flooded_buildings_count;
    public int protected_priority_score_total;
    public float flood_threshold_m;
    public bool building_outcomes_available;
    public float protection_efficiency_per_m;
    public float priority_weighted_protection_efficiency_per_m;
    public bool protection_efficiency_valid;
    public List<FloodDykeRecord> dykes;
    public List<FloodBuildingOutcome> building_outcomes;
}

[Serializable]
public sealed class FloodScreenshotRecord
{
    public int participant_id;
    public int participant_id_mod12;
    public string input_method;
    public string session;
    public string scenario;
    public string stage;
    public string image_path;
    public string timestamp_utc;
    public List<FloodDykeRecord> participant_dykes;
    public List<FloodSolutionGeometry> solution_dykes;
}

[Serializable]
public sealed class FloodSolutionGeometry
{
    public string object_name;
    public Vector3 position_local;
    public Vector3 bounds_center_local;
    public Vector3 bounds_size_world;
}

[Serializable]
public sealed class FloodGeneralDiscomfortRecord
{
    public int participant_id;
    public int participant_id_mod12;
    public string input_method;
    public string session;
    public string measurement;
    public int score_0_to_20;
    public string timestamp_utc;
}

[Serializable]
public sealed class FloodDykeRecord
{
    public int dyke_id;
    public string built_at_utc;
    public string destroyed_at_utc;
    public Vector3 start_position_local;
    public Vector3 end_position_local;
    public Vector3 position_dyke_local;
    public Vector3 position_dyke;
    public float length_dyke_m;
    public float length_dyke;
    public bool active;
}

[Serializable]
public sealed class FloodBuildingOutcome
{
    public string building_id;
    public string priority;
    public string district;
    public float water_depth_at_simulation_end_m;
    public float final_water_depth_m;
    public bool flooded;
    public bool protected_building;
    public int protected_priority_score;
}
