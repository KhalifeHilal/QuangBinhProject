using System;
using System.Collections.Generic;

// Runs against the actual MainSceneRoundLog source. A tiny Vector3 shim is
// used by the standalone runner; no Unity editor or GAMA session is altered.
public static class MainSceneRoundLogChecks
{
    static int checks;
    static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        checks++;
    }

    static MainSceneBarrierTelemetry Barrier(string name, string type, float length)
    {
        return new MainSceneBarrierTelemetry
        {
            gama_name = name, structure_type = type, player_id = "Player_63",
            length_m = length, remaining_length_m = length,
            start_gama = new[] { 10f, 20f, 3f }, end_gama = new[] { 70f, 20f, 3f }
        };
    }

    static MainSceneTelemetry Event(string id, string type, MainSceneBarrierTelemetry barrier = null)
    {
        return new MainSceneTelemetry
        {
            schema_version = 1, event_id = id, event_type = type,
            round = 4, success = true, barrier = barrier,
            dyke_length = 60, dam_length = 40, evacuated = 8, casualties = 2, score = 987.125
        };
    }

    public static int Main()
    {
        try
        {
            var record = new MainSceneRoundRecord { round = 4 };
            var log = new MainSceneRoundLog(record);
            var dyke = Barrier("dyke0", "dyke", 60);
            var dam = Barrier("dyke1", "dam", 40);
            Check(log.Apply(Event("1", "barrier_built", dyke), "built1"), "Build was not accepted");
            Check(log.Apply(Event("2", "barrier_built", dam), "built2"), "Dam build was not accepted");
            Check(record.dykes_built_count == 1 && record.dams_built_count == 1, "Type counts differ");
            Check(record.total_built_length_m == 100, "Built lengths do not add up");
            Check(record.dykes[0].start_position_world.x == 10 && record.dykes[0].start_position_world.y == 3 &&
                record.dykes[0].start_position_world.z == -20, "GAMA-to-world axes are wrong");
            Check(record.dykes[0].length_dyke_m == 60 && record.dams[0].length_dyke_m == 40, "Per-piece lengths were lost");
            Check(!log.Apply(Event("2", "barrier_built", dam), "duplicate"), "Duplicate event was accepted");
            Check(record.total_built_length_m == 100, "Duplicate build changed totals");
            Check(record.score == 987.125 && record.evacuated == 8 && record.casualties == 2, "Authoritative metrics were rounded/inferred");

            var flooded = Event("3", "world_state");
            dam.drowned = true;
            dam.remaining_length_m = 5;
            flooded.barriers = new List<MainSceneBarrierTelemetry> { dyke, dam };
            log.Apply(flooded, "flood");
            Check(!record.dams[0].active && record.dams[0].drowned, "Flooded dam remained active");
            Check(record.dams_destroyed_count == 0 && record.total_destroyed_length_m == 0, "Flood loss counted as manual destruction");
            Check(record.dams[0].remaining_length_m == 5 && record.dams[0].length_dyke_m == 40, "Flood erosion overwrote original length");

            var rejected = Event("4", "barrier_destroyed", dyke);
            rejected.success = false;
            log.Apply(rejected, "rejected");
            Check(record.dykes_destroyed_count == 0 && record.dykes[0].active, "Rejected destruction counted");
            var destroyed = Event("5", "barrier_destroyed", dyke);
            destroyed.barriers = new List<MainSceneBarrierTelemetry> { dam };
            log.Apply(destroyed, "destroyed");
            Check(record.dykes_destroyed_count == 1 && record.total_destroyed_length_m == 60, "Confirmed destruction missing");
            Check(!record.dykes[0].active && !record.dykes[0].present_in_gama, "Destroyed structure is still present");
            Check(record.dykes[0].destroyed_at_utc == "destroyed", "Destruction time missing");
            log.Apply(Event("6", "barrier_destroyed", dyke), "duplicate destruction");
            Check(record.dykes_destroyed_count == 1, "Repeated acknowledgement counted twice");
            var wrongRound = Event("7", "barrier_built", Barrier("wrong", "dam", 1000));
            wrongRound.round = 5;
            Check(!log.Apply(wrongRound, "wrong round"), "Another round contaminated this record");
            Check(record.total_built_length_m == 100, "Wrong round changed totals");

            var joinRecord = new MainSceneRoundRecord { round = 4 };
            var join = new MainSceneRoundLog(joinRecord);
            var observed = Event("8", "world_state");
            observed.barriers = new List<MainSceneBarrierTelemetry> { Barrier("existing", "dyke", 25) };
            join.Apply(observed, "joined");
            Check(joinRecord.dykes.Count == 1 && joinRecord.dykes_built_count == 0, "Joining mid-round invented build events");
            Check(joinRecord.dykes[0].built_at_utc == null && !joinRecord.dykes[0].build_confirmed_by_gama, "Observation is marked as confirmed build");
            Check(MainSceneRoundLog.Point(null).x == 0, "Missing point handling failed");

            record.simulation_end = Event("9", "simulation_end");
            double finalScore = record.score;
            var late = Event("10", "world_state");
            late.score = 0;
            Check(!log.Apply(late, "late") && record.score == finalScore, "Completed round was overwritten by a delayed packet");
            var queue = new MainSceneWorldQueue();
            var initial = new WorldJSONInfo { isInit = true, state = "s_diking" };
            var ordinary1 = new WorldJSONInfo { state = "s_diking", main_scene_log = Event("11", "world_state") };
            var ordinary2 = new WorldJSONInfo { state = "s_diking", main_scene_log = Event("12", "world_state") };
            var buildEnd = new WorldJSONInfo { state = "s_diking", main_scene_log = Event("13", "build_end") };
            var flood = new WorldJSONInfo { state = "s_flooding", main_scene_log = Event("14", "world_state") };
            var simulationEnd = new WorldJSONInfo { state = "s_flooding", main_scene_log = Event("15", "simulation_end") };
            var nextRound = new WorldJSONInfo { state = "s_diking", main_scene_log = Event("16", "world_state") };
            nextRound.main_scene_log.round = 5;
            queue.Enqueue(initial); queue.Enqueue(ordinary1); queue.Enqueue(ordinary2);
            queue.Enqueue(buildEnd); queue.Enqueue(flood); queue.Enqueue(simulationEnd); queue.Enqueue(nextRound);
            WorldJSONInfo frame;
            Check(queue.TryTake(out frame) && frame == initial, "Initial geometry was dropped");
            Check(queue.TryTake(out frame) && frame == ordinary2, "Ordinary frames did not coalesce to latest");
            Check(queue.TryTake(out frame) && frame == buildEnd, "build_end geometry was dropped or reordered");
            Check(queue.TryTake(out frame) && frame == flood, "Flood transition was lost");
            Check(queue.TryTake(out frame) && frame == simulationEnd, "Final water frame was dropped or reordered");
            Check(queue.TryTake(out frame) && frame == nextRound, "Next round was lost");
            Check(!queue.TryTake(out frame) && frame == null, "Queue did not drain");
            queue.Enqueue(nextRound); queue.Enqueue(ordinary1);
            Check(queue.TryTake(out frame) && frame == nextRound, "Ordinary frames of different rounds were merged");
            Console.WriteLine("PASS: " + checks + " main-scene round/event checks.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
