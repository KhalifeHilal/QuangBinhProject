using System.Collections.Generic;

/// <summary>Keeps round-end frames ordered; coalesces only ordinary updates.</summary>
public sealed class MainSceneWorldQueue
{
    readonly LinkedList<WorldJSONInfo> pending = new LinkedList<WorldJSONInfo>();

    public void Enqueue(WorldJSONInfo world)
    {
        WorldJSONInfo last = pending.Last != null ? pending.Last.Value : null;
        if (last != null && !last.isInit && !world.isInit && last.state == world.state &&
            IsOrdinary(last.main_scene_log) && IsOrdinary(world.main_scene_log) && RoundOf(last) == RoundOf(world))
            pending.Last.Value = world;
        else
            pending.AddLast(world);
    }

    public bool TryTake(out WorldJSONInfo world)
    {
        world = null;
        if (pending.Count == 0) return false;
        world = pending.First.Value;
        pending.RemoveFirst();
        return true;
    }

    static bool IsOrdinary(MainSceneTelemetry value) => value == null || value.event_type == "world_state";
    static int RoundOf(WorldJSONInfo value) => value.main_scene_log != null ? value.main_scene_log.round : 0;
}
