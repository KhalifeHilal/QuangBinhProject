using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Main-scene-only GAMA round logs. Tutorial logging is untouched.</summary>
public sealed class MainSceneFloodLogger : MonoBehaviour
{
    public const string SceneName = "Main Scene - ArtUpdate_Flood";
    readonly MainSceneWorldQueue pendingWorlds = new MainSceneWorldQueue();
    readonly Dictionary<int, MainSceneRoundLog> rounds = new Dictionary<int, MainSceneRoundLog>();
    readonly Dictionary<int, float> buildStarted = new Dictionary<int, float>();
    readonly Dictionary<int, float> simulationStarted = new Dictionary<int, float>();
    string folder;
    string runId;
    string connectionId;
    int participantId;
    int highestRound;
    int gameSequence = 1;
    float initializedAt;
    bool receivedTelemetry;
    bool warnedMissingTelemetry;

    public void Initialize()
    {
        connectionId = ConnectionManager.Instance != null ? ConnectionManager.Instance.GetConnectionId() : "Unknown";
        participantId = ParticipantSession.IsCheckedIn ? ParticipantSession.Number : 0;
        runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        SetFolder();
        initializedAt = Time.realtimeSinceStartup;
        Debug.Log("Main-scene study logs: " + folder, this);
    }

    void SetFolder()
    {
        string participant = participantId > 0 ? $"Participant_{participantId:00}" : "Participant_Unknown";
        folder = Path.Combine(Application.persistentDataPath, "FloodStudyLogs", participant,
            "MainScene_" + runId + "_Game_" + gameSequence.ToString("00"));
    }

    // Preserve terminal geometry packets even if GAMA sends several messages
    // between Unity frames. Only ordinary updates of the same phase coalesce.
    public bool TryHandleMessage(string content)
    {
        try
        {
            JObject root = JObject.Parse(content);
            if (root["names"] != null && root["state"] != null && root["pointsLoc"] != null)
            {
                WorldJSONInfo world = WorldJSONInfo.CreateFromJSON(content);
                pendingWorlds.Enqueue(world);
                return true;
            }
            if (root["main_scene_log"] is JObject telemetry)
            {
                Receive(JsonUtility.FromJson<MainSceneTelemetry>(telemetry.ToString(Formatting.None)));
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Main-scene logging message could not be read: " + exception.Message, this);
        }
        return false;
    }

    public bool TryTakeWorld(out WorldJSONInfo world)
    {
        return pendingWorlds.TryTake(out world);
    }

    /// <summary>Called only after the packet's geometry, attributes and removals have been applied.</summary>
    public void WorldApplied(WorldJSONInfo world)
    {
        if (world == null || world.main_scene_log == null) return;
        Receive(world.main_scene_log, true);
    }

    void Receive(MainSceneTelemetry telemetry, bool geometryApplied = false)
    {
        if (telemetry == null || telemetry.schema_version != 1 || telemetry.round <= 0) return;
        try
        {
            receivedTelemetry = true;
            if (telemetry.event_type == "round_start" && telemetry.round < highestRound)
            {
                FlushIncomplete();
                rounds.Clear();
                buildStarted.Clear();
                simulationStarted.Clear();
                highestRound = 0;
                gameSequence++;
                SetFolder();
            }
            highestRound = Math.Max(highestRound, telemetry.round);
            MainSceneRoundLog log = GetRound(telemetry.round);
            MainSceneRoundRecord record = log.Record;
            string utc = UtcNow();
            if (!log.Apply(telemetry, utc)) return;
            if (telemetry.state == "s_diking" && string.IsNullOrEmpty(record.building_phase_started_at_utc))
            {
                record.building_phase_started_at_utc = utc;
                buildStarted[record.round] = Time.realtimeSinceStartup;
            }
            if (telemetry.state == "s_flooding" && string.IsNullOrEmpty(record.simulation_started_at_utc))
            {
                record.simulation_started_at_utc = utc;
                simulationStarted[record.round] = Time.realtimeSinceStartup;
            }

            // Terminal telemetry belongs to a forced final geometry packet.
            // Never photograph the previous rendered state from a side message.
            if (geometryApplied && telemetry.event_type == "build_end" && record.build_end == null)
            {
                record.build_end = telemetry;
                record.building_phase_ended_at_utc = utc;
                if (buildStarted.TryGetValue(record.round, out float start))
                    record.duration_of_building_phase_s = Mathf.Max(0f, Time.realtimeSinceStartup - start);
                record.used_barrier_length_m = (float)(telemetry.dyke_length + telemetry.dam_length);
                foreach (MainSceneBarrierRecord barrier in record.dykes)
                    if (barrier.active) record.active_dyke_count_at_simulation_start++;
                foreach (MainSceneBarrierRecord barrier in record.dams)
                    if (barrier.active) record.active_dam_count_at_simulation_start++;
                record.build_end_screenshot_path = Capture(record, telemetry);
            }
            else if (geometryApplied && telemetry.event_type == "simulation_end" && record.simulation_end == null)
            {
                record.simulation_end = telemetry;
                record.simulation_ended_at_utc = record.scenario_ended_at_utc = record.time_stamp_end = utc;
                if (simulationStarted.TryGetValue(record.round, out float start))
                    record.simulation_duration_s = Mathf.Max(0f, Time.realtimeSinceStartup - start);
                record.status = "completed";
                record.simulation_end_screenshot_path = Capture(record, telemetry);
            }

            if (telemetry.event_type != "world_state")
            {
                AppendEvent(telemetry, utc);
                Save(record);
                WriteRoundCsv();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Main-scene study log could not be written: " + exception.Message, this);
        }
    }

    MainSceneRoundLog GetRound(int round)
    {
        if (rounds.TryGetValue(round, out MainSceneRoundLog value)) return value;
        string utc = UtcNow();
        var record = new MainSceneRoundRecord
        {
            run_id = runId,
            connection_id = connectionId,
            participant_id = participantId,
            participant_id_mod12 = participantId > 0 ? ((participantId - 1) % 12) + 1 : 0,
            input_method = ParticipantSession.IsCheckedIn ? ParticipantSession.InteractionMode.ToString() : "Unknown",
            session = ParticipantSession.IsCheckedIn ? ParticipantSession.QuestionSet.ToString() : "Unknown",
            scenario = "Main_Round_" + round.ToString("000"),
            round = round,
            map_version = gameObject.scene.name,
            application_version = Application.version,
            scenario_started_at_utc = utc,
            time_stamp_beggining = utc
        };
        value = new MainSceneRoundLog(record);
        rounds.Add(round, value);
        return value;
    }

    string Capture(MainSceneRoundRecord record, MainSceneTelemetry telemetry)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, record.scenario + "_" + telemetry.event_type + "_" +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        bool captured = MainSceneTopDownCapture.TryWrite(path, gameObject.scene, out Bounds bounds);
        var manifest = new MainSceneScreenshotRecord
        {
            participant_id = record.participant_id,
            participant_id_mod12 = record.participant_id_mod12,
            input_method = record.input_method,
            session = record.session,
            scenario = record.scenario,
            round = record.round,
            stage = telemetry.event_type,
            image_path = captured ? path : string.Empty,
            timestamp_utc = UtcNow(),
            capture_succeeded = captured,
            bounds_center_world = bounds.center,
            bounds_size_world = bounds.size,
            metrics = telemetry,
            participant_dykes = new List<MainSceneBarrierRecord>(record.dykes)
        };
        manifest.participant_dykes.AddRange(record.dams);
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(manifest, true));
        return captured ? path : string.Empty;
    }

    void AppendEvent(MainSceneTelemetry value, string utc)
    {
        Directory.CreateDirectory(folder);
        JObject entry = JObject.Parse(JsonUtility.ToJson(value));
        entry["timestamp_utc"] = utc;
        entry["connection_id"] = connectionId;
        File.AppendAllText(Path.Combine(folder, "events.jsonl"), entry.ToString(Formatting.None) + Environment.NewLine);
    }

    void Save(MainSceneRoundRecord value)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, value.scenario + ".json"), JsonUtility.ToJson(value, true));
    }

    void WriteRoundCsv()
    {
        var csv = new System.Text.StringBuilder("round,dyke_length,dam_length,evacuated,casualties,score,status,build_end_screenshot_path,simulation_end_screenshot_path\n");
        var ordered = new List<int>(rounds.Keys);
        ordered.Sort();
        foreach (int round in ordered)
        {
            MainSceneRoundRecord value = rounds[round].Record;
            csv.Append(round).Append(',').Append(value.dyke_length.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.dam_length.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(value.evacuated).Append(',')
                .Append(value.casualties).Append(',').Append(value.score.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.status).Append(',').Append(Csv(value.build_end_screenshot_path)).Append(',')
                .Append(Csv(value.simulation_end_screenshot_path)).Append('\n');
        }
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "rounds.csv"), csv.ToString());
    }

    static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    static string UtcNow() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    void Update()
    {
        if (!receivedTelemetry && !warnedMissingTelemetry && Time.realtimeSinceStartup - initializedAt > 30f)
        {
            warnedMissingTelemetry = true;
            Debug.LogWarning("Main-scene logging is waiting for GAMA main_scene_log telemetry. Use the matching instrumented Flooding Model/VR.gaml files; missing values are not estimated.", this);
        }
    }

    void FlushIncomplete()
    {
        if (string.IsNullOrEmpty(folder)) return;
        foreach (MainSceneRoundLog log in rounds.Values)
        {
            if (log.Record.status != "completed") log.Record.status = "interrupted";
            Save(log.Record);
        }
        if (rounds.Count > 0) WriteRoundCsv();
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        try { foreach (MainSceneRoundLog log in rounds.Values) Save(log.Record); }
        catch (Exception exception) { Debug.LogWarning("Main-scene log flush failed: " + exception.Message, this); }
    }

    void OnDestroy()
    {
        try { FlushIncomplete(); }
        catch (Exception exception) { Debug.LogWarning("Main-scene log flush failed: " + exception.Message); }
    }
}

[Serializable]
public sealed class MainSceneScreenshotRecord
{
    public int participant_id;
    public int participant_id_mod12;
    public string input_method;
    public string session;
    public string scenario;
    public int round;
    public string stage;
    public string image_path;
    public string timestamp_utc;
    public bool capture_succeeded;
    public Vector3 bounds_center_world;
    public Vector3 bounds_size_world;
    public MainSceneTelemetry metrics;
    public List<MainSceneBarrierRecord> participant_dykes;
}
