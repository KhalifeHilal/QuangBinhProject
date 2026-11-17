using System;
using System.Collections.Generic;
using UnityEngine;

// Main-scene records deliberately use the tutorial logger's field names for
// shared measures. They do not reuse or mutate its static scenario state.
[Serializable]
public sealed class MainSceneTelemetry
{
    public int schema_version;
    public string event_id;
    public string event_type;
    public int round;
    public string state;
    public int current_step;
    public int num_step;
    public double gama_machine_time_ms;
    public double dyke_length;
    public double dam_length;
    public int evacuated;
    public int casualties;
    public double score;
    public string player_id;
    public bool success;
    public MainSceneBarrierTelemetry barrier;
    public List<MainSceneBarrierTelemetry> barriers;
    public float[] request_start_gama;
    public float[] request_end_gama;
}

[Serializable]
public sealed class MainSceneBarrierTelemetry
{
    public string gama_name;
    public string structure_type;
    public string player_id;
    public float length_m;
    public float remaining_length_m;
    public float[] start_gama;
    public float[] end_gama;
    public bool drowned;
}

[Serializable]
public sealed class MainSceneBarrierRecord
{
    public int dyke_id;
    public string gama_name;
    public string structure_type;
    public string player_id;
    public string built_at_utc;
    public string destroyed_at_utc;
    public string first_observed_at_utc;
    public bool build_confirmed_by_gama;
    public bool active;
    public bool present_in_gama;
    public bool drowned;
    public Vector3 start_position_gama;
    public Vector3 end_position_gama;
    public Vector3 start_position_world;
    public Vector3 end_position_world;
    public Vector3 start_position_local;
    public Vector3 end_position_local;
    public Vector3 position_dyke;
    public Vector3 position_dyke_local;
    public float length_dyke_m;
    public float length_dyke;
    public float remaining_length_m;
}

[Serializable]
public sealed class MainSceneRoundRecord
{
    public int schema_version = 1;
    public string source = "gama_main_scene";
    public string run_id;
    public string connection_id;
    public int participant_id;
    public int participant_id_mod12;
    public string input_method;
    public string session;
    public string scenario;
    public string topic = "Quang Binh GAMA round";
    public string map_version;
    public string application_version;
    public string coordinate_reference = "GAMA metres; Unity world = (gama.x, gama.z, -gama.y); local = world (unscaled map origin)";
    public int round;
    public string status = "in_progress";
    public string scenario_started_at_utc;
    public string scenario_ended_at_utc;
    public string time_stamp_beggining;
    public string time_stamp_end;
    public string building_phase_started_at_utc;
    public string building_phase_ended_at_utc;
    public float duration_of_building_phase_s;
    public string simulation_started_at_utc;
    public string simulation_ended_at_utc;
    public float simulation_duration_s;
    public string build_end_screenshot_path;
    public string simulation_end_screenshot_path;
    public int dykes_built_count;
    public int dams_built_count;
    public int dykes_destroyed_count;
    public int dams_destroyed_count;
    public int active_dyke_count_at_simulation_start;
    public int active_dam_count_at_simulation_start;
    public float used_barrier_length_m;
    public float total_built_length_m;
    public float total_destroyed_length_m;
    public double dyke_length;
    public double dam_length;
    public int evacuated;
    public int casualties;
    public double score;
    public bool gama_metrics_available;
    public MainSceneTelemetry build_end;
    public MainSceneTelemetry simulation_end;
    public List<MainSceneBarrierRecord> dykes = new List<MainSceneBarrierRecord>();
    public List<MainSceneBarrierRecord> dams = new List<MainSceneBarrierRecord>();
}

/// <summary>Accumulates confirmed GAMA events, independently of rendering/UI.</summary>
public sealed class MainSceneRoundLog
{
    public MainSceneRoundRecord Record { get; }
    readonly Dictionary<string, MainSceneBarrierRecord> barriers = new Dictionary<string, MainSceneBarrierRecord>();
    readonly HashSet<string> eventIds = new HashSet<string>();
    int nextId;

    public MainSceneRoundLog(MainSceneRoundRecord record)
    {
        Record = record;
    }

    public bool Apply(MainSceneTelemetry telemetry, string utc)
    {
        if (telemetry == null || telemetry.round != Record.round) return false;
        if (!string.IsNullOrEmpty(telemetry.event_id) && !eventIds.Add(telemetry.event_id)) return false;
        // A delayed packet must never overwrite completed-round results.
        if (Record.simulation_end != null) return false;
        Record.gama_metrics_available = true;
        Record.dyke_length = telemetry.dyke_length;
        Record.dam_length = telemetry.dam_length;
        Record.evacuated = telemetry.evacuated;
        Record.casualties = telemetry.casualties;
        Record.score = telemetry.score;

        if (telemetry.barriers != null)
        {
            foreach (MainSceneBarrierRecord barrier in barriers.Values)
            {
                barrier.present_in_gama = false;
                barrier.active = false;
            }
            foreach (MainSceneBarrierTelemetry barrier in telemetry.barriers)
                Observe(barrier, utc);
        }

        if (telemetry.event_type == "barrier_built" && telemetry.success && telemetry.barrier != null)
        {
            MainSceneBarrierRecord barrier = Observe(telemetry.barrier, utc);
            if (barrier != null && !barrier.build_confirmed_by_gama)
            {
                barrier.build_confirmed_by_gama = true;
                barrier.built_at_utc = utc;
                if (barrier.structure_type == "dam") Record.dams_built_count++;
                else Record.dykes_built_count++;
                Record.total_built_length_m += barrier.length_dyke_m;
            }
        }
        else if (telemetry.event_type == "barrier_destroyed" && telemetry.success && telemetry.barrier != null)
        {
            MainSceneBarrierRecord barrier = Observe(telemetry.barrier, utc);
            if (barrier != null && string.IsNullOrEmpty(barrier.destroyed_at_utc))
            {
                barrier.destroyed_at_utc = utc;
                barrier.present_in_gama = false;
                barrier.active = false;
                if (barrier.structure_type == "dam") Record.dams_destroyed_count++;
                else Record.dykes_destroyed_count++;
                Record.total_destroyed_length_m += barrier.length_dyke_m;
            }
        }
        return true;
    }

    MainSceneBarrierRecord Observe(MainSceneBarrierTelemetry value, string utc)
    {
        if (value == null || string.IsNullOrEmpty(value.gama_name)) return null;
        if (!barriers.TryGetValue(value.gama_name, out MainSceneBarrierRecord record))
        {
            record = new MainSceneBarrierRecord
            {
                dyke_id = ++nextId,
                gama_name = value.gama_name,
                structure_type = value.structure_type,
                first_observed_at_utc = utc
            };
            barriers.Add(value.gama_name, record);
            if (value.structure_type == "dam") Record.dams.Add(record);
            else Record.dykes.Add(record);
        }
        record.player_id = value.player_id;
        record.start_position_gama = Point(value.start_gama);
        record.end_position_gama = Point(value.end_gama);
        record.start_position_world = World(record.start_position_gama);
        record.end_position_world = World(record.end_position_gama);
        record.start_position_local = record.start_position_world;
        record.end_position_local = record.end_position_world;
        record.position_dyke = (record.start_position_world + record.end_position_world) * .5f;
        record.position_dyke_local = record.position_dyke;
        record.length_dyke_m = record.length_dyke = value.length_m;
        record.remaining_length_m = value.remaining_length_m;
        record.drowned = value.drowned;
        record.present_in_gama = string.IsNullOrEmpty(record.destroyed_at_utc);
        record.active = record.present_in_gama && !record.drowned;
        return record;
    }

    public static Vector3 Point(float[] values)
    {
        return values != null && values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.zero;
    }

    public static Vector3 World(Vector3 gama)
    {
        return new Vector3(gama.x, gama.z, -gama.y);
    }
}
